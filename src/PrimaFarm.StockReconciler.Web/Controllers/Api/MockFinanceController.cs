using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Models;
using PrimaFarm.StockReconciler.Web.Services.Integration;

namespace PrimaFarm.StockReconciler.Web.Controllers.Api;

/// <summary>
/// SIMULATED finance / core ledger API, so the outbound integration can be demonstrated and tested
/// end-to-end without the real system. Disabled outside Development (MockFinance:Enabled=false).
/// Behaves like a well-designed third-party API: API key, Idempotency-Key replay, transient 503s.
/// </summary>
[ApiController]
[Route("mock/finance/api/journals")]
[AllowAnonymous, IgnoreAntiforgeryToken, ApiExplorerSettings(IgnoreApi = true)]
public class MockFinanceController(AppDbContext db, IConfiguration config, ILogger<MockFinanceController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(FinanceJournalRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Api-Key")] string? apiKey, CancellationToken ct)
    {
        if (!config.GetValue<bool>("MockFinance:Enabled")) return NotFound();

        var expected = Encoding.UTF8.GetBytes(config["MockFinance:ApiKey"] ?? "");
        if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(apiKey ?? ""), expected))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 64)
            return BadRequest(new { error = "Idempotency-Key header is required." });

        // Replay: same key -> same journal, never a duplicate posting.
        var existing = await db.MockFinanceJournals.AsNoTracking().FirstOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
            return Ok(new FinanceJournalResponse(existing.JournalNo, "Duplicate"));

        if (Random.Shared.NextDouble() < config.GetValue<double>("MockFinance:TransientFailureRate"))
        {
            logger.LogWarning("Mock finance: simulating transient 503 for {Key}", idempotencyKey);
            return StatusCode(503, new { error = "Ledger temporarily unavailable." });
        }

        var journal = new MockFinanceJournal
        {
            JournalNo = $"JNL-{DateTime.UtcNow:yyyyMMdd}-{RandomNumberGenerator.GetInt32(100000, 999999)}",
            IdempotencyKey = idempotencyKey,
            SourceReference = request.SourceReference,
            LineCount = request.Lines.Count,
            NetVarianceQty = request.NetVarianceQty,
            Narrative = $"Stock variance adjustment {request.LocationCode} {request.PostingDate:yyyy-MM-dd}",
            ReceivedUtc = DateTime.UtcNow
        };
        db.MockFinanceJournals.Add(journal);
        await db.SaveChangesAsync(ct);
        return StatusCode(201, new FinanceJournalResponse(journal.JournalNo, "Posted"));
    }
}
