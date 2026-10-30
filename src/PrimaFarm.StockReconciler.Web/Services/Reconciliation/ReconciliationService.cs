using Microsoft.EntityFrameworkCore;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Models;
using PrimaFarm.StockReconciler.Web.Services.Integration;

namespace PrimaFarm.StockReconciler.Web.Services.Reconciliation;

public sealed record UploadedFile(string FileName, Stream Content);

public class BusinessRuleException(string message) : Exception(message);

/// <summary>
/// Orchestrates a reconciliation: read files -> compare -> persist run + lines -> audit.
/// Also owns the maker-checker approval workflow and the finance posting hand-off.
/// </summary>
public class ReconciliationService(
    AppDbContext db,
    AuditService audit,
    FinancePostingClient finance,
    ILogger<ReconciliationService> logger)
{
    public async Task<ReconciliationRun> RunFromFilesAsync(int locationId, UploadedFile erpFile,
        IReadOnlyList<UploadedFile> farmFiles, string user, CancellationToken ct)
    {
        var location = await GetEnabledLocationAsync(l => l.LocationId == locationId, ct);
        if (farmFiles.Count == 0) throw new BusinessRuleException("At least one farm stock file is required.");

        var erpLines = StockFileReader.Read(erpFile.Content, erpFile.FileName, StockFileOptions.ForErp());
        if (erpLines.Count == 0)
            throw new BusinessRuleException($"No stock lines could be read from the ERP file '{erpFile.FileName}'. Check the column headers (Item no / On-hand).");

        var farmOptions = StockFileOptions.ForFarm(location);
        var farmLines = farmFiles.SelectMany(f => StockFileReader.Read(f.Content, f.FileName, farmOptions)).ToList();
        if (farmLines.Count == 0)
            throw new BusinessRuleException("No stock lines could be read from the farm file(s). Check the column headers and sheet names.");

        var erpSource = Path.GetExtension(erpFile.FileName).ToLowerInvariant() is ".txt" or ".dat" ? ErpSources.AS400 : ErpSources.Upload;
        return await SaveAsync(location, erpLines, farmLines, erpSource, erpFile.FileName,
            string.Join(", ", farmFiles.Select(f => f.FileName)), user, ct);
    }

    public async Task<ReconciliationRun> RunFromApiAsync(string locationCode, IReadOnlyList<StockLine> erpLines,
        IReadOnlyList<StockLine> farmLines, string client, CancellationToken ct)
    {
        var location = await GetEnabledLocationAsync(l => l.Code == locationCode, ct);
        return await SaveAsync(location, erpLines, farmLines, ErpSources.Api, "API payload", "API payload", client, ct);
    }

    private async Task<Location> GetEnabledLocationAsync(System.Linq.Expressions.Expression<Func<Location, bool>> predicate, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking().FirstOrDefaultAsync(predicate, ct)
            ?? throw new BusinessRuleException("Unknown farm location.");
        if (!location.IsEnabled)
            throw new BusinessRuleException($"{location.Name} is not enabled yet ({location.Status}).");
        return location;
    }

    private async Task<ReconciliationRun> SaveAsync(Location location, IReadOnlyList<StockLine> erpLines,
        IReadOnlyList<StockLine> farmLines, string erpSource, string erpFileName, string farmFileNames, string user, CancellationToken ct)
    {
        var result = ReconciliationEngine.Compare(erpLines, farmLines, location.Tolerance, location.IncludeZeroErpMissing);

        var run = new ReconciliationRun
        {
            RunReference = await db.GetNextRunReferenceAsync(ct),
            LocationId = location.LocationId,
            ErpSource = erpSource,
            ErpFileName = Truncate(erpFileName, 260),
            FarmFileNames = Truncate(farmFileNames, 800),
            Status = RunStatuses.Completed,
            MatchedCount = result.Count(LineCategories.Matched),
            MismatchedCount = result.Count(LineCategories.Mismatched),
            MissingInFarmCount = result.Count(LineCategories.MissingInFarm),
            NotInErpCount = result.Count(LineCategories.NotInErp),
            SummedLineCount = result.SummedLineCount,
            TotalErpQty = result.TotalErpQty,
            TotalFarmQty = result.TotalFarmQty,
            CreatedBy = user,
            CreatedUtc = DateTime.UtcNow,
            Lines = result.Lines.Select(l => new ReconciliationLine
            {
                ItemCode = l.ItemCode,
                ItemName = Truncate(l.ItemName, 200),
                Category = l.Category,
                ErpQty = l.ErpQty,
                FarmQty = l.FarmQty,
                Difference = l.Difference,
                FarmLineCount = l.FarmLineCount,
                SourceRows = Truncate(l.SourceRows, 1000)
            }).ToList()
        };

        db.ReconciliationRuns.Add(run);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Reconciliation {RunReference} for {Location}: {Matched} matched, {Mismatched} mismatched, {Missing} missing, {NotInErp} not in ERP",
            run.RunReference, location.Name, run.MatchedCount, run.MismatchedCount, run.MissingInFarmCount, run.NotInErpCount);
        await audit.WriteAsync("RECONCILIATION_RUN", "ReconciliationRun", run.RunReference,
            $"Location={location.Name}; Source={erpSource}; ERP lines={erpLines.Count}; Farm lines={farmLines.Count}; Mismatched={run.MismatchedCount}", user, ct);
        return run;
    }

    // ---------------------------------------------------------------- maker-checker workflow

    public async Task ApproveAsync(int runId, byte[] rowVersion, string reviewer, string? comment, CancellationToken ct)
    {
        var run = await LoadForReviewAsync(runId, rowVersion, reviewer, ct);
        run.Status = RunStatuses.Approved;
        run.ReviewedBy = reviewer;
        run.ReviewedUtc = DateTime.UtcNow;
        run.ReviewComment = comment;
        await SaveReviewAsync(ct);
        await audit.WriteAsync("RECONCILIATION_APPROVED", "ReconciliationRun", run.RunReference, comment, reviewer, ct);

        await PostToFinanceAsync(run, reviewer, ct);
    }

    public async Task RejectAsync(int runId, byte[] rowVersion, string reviewer, string comment, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(comment)) throw new BusinessRuleException("A comment is required when rejecting.");
        var run = await LoadForReviewAsync(runId, rowVersion, reviewer, ct);
        run.Status = RunStatuses.Rejected;
        run.ReviewedBy = reviewer;
        run.ReviewedUtc = DateTime.UtcNow;
        run.ReviewComment = comment;
        await SaveReviewAsync(ct);
        await audit.WriteAsync("RECONCILIATION_REJECTED", "ReconciliationRun", run.RunReference, comment, reviewer, ct);
    }

    public async Task RetryPostingAsync(int runId, string user, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.RunId == runId, ct)
            ?? throw new BusinessRuleException("Reconciliation not found.");
        if (run.Status != RunStatuses.PostingFailed)
            throw new BusinessRuleException("Only runs whose finance posting failed can be retried.");
        await PostToFinanceAsync(run, user, ct);
    }

    private async Task<ReconciliationRun> LoadForReviewAsync(int runId, byte[] rowVersion, string reviewer, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns.Include(r => r.Lines).FirstOrDefaultAsync(r => r.RunId == runId, ct)
            ?? throw new BusinessRuleException("Reconciliation not found.");
        if (run.Status != RunStatuses.Completed)
            throw new BusinessRuleException($"This reconciliation is already {run.Status}.");
        if (string.Equals(run.CreatedBy, reviewer, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Segregation of duties: you cannot review a reconciliation you created.");
        // Optimistic concurrency: if someone else reviewed it meanwhile, SaveChanges throws.
        db.Entry(run).Property(r => r.RowVer).OriginalValue = rowVersion;
        return run;
    }

    private async Task SaveReviewAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new BusinessRuleException("This reconciliation was changed by another user. Reload the page and try again.");
        }
    }

    private async Task PostToFinanceAsync(ReconciliationRun run, string user, CancellationToken ct)
    {
        var varianceLines = run.Lines.Where(l => l.Category != LineCategories.Matched).ToList();
        var outcome = await finance.PostStockAdjustmentAsync(run, varianceLines, ct);

        run.Status = outcome.Succeeded ? RunStatuses.Posted : RunStatuses.PostingFailed;
        run.FinanceReference = outcome.JournalNo;
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(outcome.Succeeded ? "FINANCE_POSTED" : "FINANCE_POSTING_FAILED", "ReconciliationRun",
            run.RunReference, outcome.Succeeded ? $"Journal {outcome.JournalNo}" : outcome.Error, user, ct);
    }

    private static string? Truncate(string? s, int max) => s is null || s.Length <= max ? s : s[..max];
}
