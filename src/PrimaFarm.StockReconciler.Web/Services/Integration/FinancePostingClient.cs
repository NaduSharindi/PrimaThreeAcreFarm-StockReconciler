using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Infrastructure;
using PrimaFarm.StockReconciler.Web.Models;

namespace PrimaFarm.StockReconciler.Web.Services.Integration;

public class FinanceOptions
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";          // supplied via environment / IIS app settings in production
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed record PostingOutcome(bool Succeeded, string? JournalNo, string? Error);

public sealed record FinanceJournalLine(string ItemCode, decimal VarianceQty);

public sealed record FinanceJournalRequest(
    string SourceSystem,
    string SourceReference,
    string LocationCode,
    DateOnly PostingDate,
    decimal NetVarianceQty,
    IReadOnlyList<FinanceJournalLine> Lines);

public sealed record FinanceJournalResponse(string JournalNo, string Status);

/// <summary>
/// Outbound integration with the finance / core ledger system: posts a stock-variance adjustment
/// journal once a reconciliation is approved.
///  - Retries, timeouts and circuit breaker come from the resilience pipeline configured in Program.cs.
///  - Retrying a POST is safe because every request carries an Idempotency-Key (the run reference);
///    the receiver returns the original journal instead of creating a duplicate.
///  - Every call is written to dbo.IntegrationLogs for incident analysis (secrets are never logged).
/// </summary>
public class FinancePostingClient(
    HttpClient http,
    AppDbContext db,
    IOptions<FinanceOptions> options,
    IHttpContextAccessor accessor,
    ILogger<FinancePostingClient> logger)
{
    public const string TargetSystem = "FinanceLedger";

    public async Task<PostingOutcome> PostStockAdjustmentAsync(ReconciliationRun run, IReadOnlyList<ReconciliationLine> varianceLines, CancellationToken ct)
    {
        var location = run.Location ?? await db.Locations.FindAsync([run.LocationId], ct);
        var body = new FinanceJournalRequest(
            "PRIMA-STOCK-RECON",
            run.RunReference,
            location?.Code ?? run.LocationId.ToString(),
            DateOnly.FromDateTime(DateTime.UtcNow),
            varianceLines.Sum(l => l.Difference),
            varianceLines.Select(l => new FinanceJournalLine(l.ItemCode, l.Difference)).ToList());

        var correlationId = accessor.HttpContext is { } ctx ? CorrelationIdMiddleware.Get(ctx) : Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/journals") { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", run.RunReference);
        request.Headers.Add("X-Api-Key", options.Value.ApiKey);
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);

        var log = new IntegrationLog
        {
            CreatedUtc = DateTime.UtcNow,
            TargetSystem = TargetSystem,
            Direction = "Outbound",
            Operation = "POST api/journals",
            RunId = run.RunId,
            IdempotencyKey = run.RunReference,
            RequestPayload = JsonSerializer.Serialize(body),
            CorrelationId = correlationId
        };

        var sw = Stopwatch.StartNew();
        PostingOutcome outcome;
        try
        {
            using var response = await http.SendAsync(request, ct);
            log.HttpStatus = (int)response.StatusCode;
            log.ResponsePayload = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var parsed = JsonSerializer.Deserialize<FinanceJournalResponse>(log.ResponsePayload, JsonSerializerOptions.Web);
                outcome = new PostingOutcome(true, parsed?.JournalNo, null);
            }
            else
            {
                outcome = new PostingOutcome(false, null, $"Finance system returned HTTP {(int)response.StatusCode}.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Polly.CircuitBreaker.BrokenCircuitException
                                       or Polly.Timeout.TimeoutRejectedException)
        {
            outcome = new PostingOutcome(false, null, $"Finance system unavailable: {ex.GetType().Name}");
            logger.LogWarning(ex, "Finance posting failed for {RunReference}", run.RunReference);
        }

        log.DurationMs = (int)sw.ElapsedMilliseconds;
        log.Succeeded = outcome.Succeeded;
        log.ErrorMessage = outcome.Error;
        db.IntegrationLogs.Add(log);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Finance posting {RunReference}: success={Succeeded} journal={JournalNo} in {DurationMs} ms",
            run.RunReference, outcome.Succeeded, outcome.JournalNo, log.DurationMs);
        return outcome;
    }
}
