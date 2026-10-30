using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Models;
using PrimaFarm.StockReconciler.Web.Services.Reconciliation;

namespace PrimaFarm.StockReconciler.Web.Controllers.Api;

/// <summary>
/// REST API for system integration (ERP gateway, BI tools). Authenticated with X-Api-Key.
/// Supports JSON and XML via the Accept / Content-Type headers.
/// </summary>
[ApiController]
[Route("api/v1/reconciliations")]
[Authorize(Policy = Policies.ApiClient)]
[EnableRateLimiting("api")]
[IgnoreAntiforgeryToken]
[Produces("application/json", "application/xml")]
public class ReconciliationsController(AppDbContext db, ReconciliationService service) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<RunSummaryDto>> Search(string? locationCode, string? status, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        int? locationId = null;
        if (!string.IsNullOrWhiteSpace(locationCode))
            locationId = await db.Locations.Where(l => l.Code == locationCode).Select(l => (int?)l.LocationId).FirstOrDefaultAsync(ct) ?? -1;

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var rows = await db.SearchRunsAsync(locationId, status, null, null, page, pageSize, ct);
        return new PagedResult<RunSummaryDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = rows.FirstOrDefault()?.TotalCount ?? 0,
            Items = rows.Select(r => new RunSummaryDto
            {
                RunReference = r.RunReference, Location = r.LocationName, ErpSource = r.ErpSource, Status = r.Status,
                Matched = r.MatchedCount, Mismatched = r.MismatchedCount, MissingInFarm = r.MissingInFarmCount,
                NotInErp = r.NotInErpCount, AccuracyPct = r.AccuracyPct, CreatedBy = r.CreatedBy,
                CreatedUtc = r.CreatedUtc, FinanceReference = r.FinanceReference
            }).ToList()
        };
    }

    [HttpGet("{reference}")]
    [ProducesResponseType<RunSummaryDto>(200), ProducesResponseType(404)]
    public async Task<ActionResult<RunSummaryDto>> Get(string reference, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns.AsNoTracking().Include(r => r.Location)
            .FirstOrDefaultAsync(r => r.RunReference == reference, ct);
        return run is null ? NotFound() : ToDto(run);
    }

    [HttpGet("{reference}/variances")]
    public async Task<ActionResult<List<VarianceDto>>> Variances(string reference, int top = 100, CancellationToken ct = default)
    {
        var runId = await db.ReconciliationRuns.Where(r => r.RunReference == reference).Select(r => (int?)r.RunId).FirstOrDefaultAsync(ct);
        if (runId is null) return NotFound();
        var rows = await db.GetVarianceReportAsync(runId.Value, Math.Clamp(top, 1, 1000), ct);
        return rows.Select(v => new VarianceDto
        {
            ItemCode = v.ItemCode, ItemName = v.ItemName, Category = v.Category, ErpQty = v.ErpQty, FarmQty = v.FarmQty, Difference = v.Difference
        }).ToList();
    }

    /// <summary>Runs a reconciliation from line data pushed by another system (e.g. the ERP gateway).</summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType<RunSummaryDto>(201), ProducesResponseType<ValidationProblemDetails>(400)]
    public async Task<ActionResult<RunSummaryDto>> Create(ReconcileApiRequest request, CancellationToken ct)
    {
        static List<StockLine> Map(IEnumerable<ApiStockLine> lines, string source) =>
            lines.Select((l, i) => new StockLine(StockFileReader.CleanCode(l.ItemCode) is { Length: > 0 } c ? c : l.ItemCode.Trim().ToUpperInvariant(),
                l.ItemName?.Trim() ?? "", l.Qty, source, source, i + 1)).ToList();

        try
        {
            var run = await service.RunFromApiAsync(request.LocationCode, Map(request.ErpLines, "erp"), Map(request.FarmLines, "farm"),
                User.Identity!.Name!, ct);
            run.Location = await db.Locations.FindAsync([run.LocationId], ct);
            return CreatedAtAction(nameof(Get), new { reference = run.RunReference }, ToDto(run));
        }
        catch (BusinessRuleException ex)
        {
            return ValidationProblem(new ValidationProblemDetails { Title = "Business rule violation", Detail = ex.Message });
        }
    }

    private static RunSummaryDto ToDto(ReconciliationRun r) => new()
    {
        RunReference = r.RunReference,
        Location = r.Location?.Name ?? "",
        ErpSource = r.ErpSource,
        Status = r.Status,
        Matched = r.MatchedCount,
        Mismatched = r.MismatchedCount,
        MissingInFarm = r.MissingInFarmCount,
        NotInErp = r.NotInErpCount,
        AccuracyPct = r.MatchedCount + r.MismatchedCount == 0 ? 0 : Math.Round(100m * r.MatchedCount / (r.MatchedCount + r.MismatchedCount), 1),
        CreatedBy = r.CreatedBy,
        CreatedUtc = r.CreatedUtc,
        FinanceReference = r.FinanceReference
    };
}
