using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Infrastructure;

namespace PrimaFarm.StockReconciler.Web.Services;

/// <summary>Writes append-only audit records (dbo.usp_WriteAuditLog) with IP and correlation id.</summary>
public class AuditService(AppDbContext db, IHttpContextAccessor http, ILogger<AuditService> logger)
{
    public async Task WriteAsync(string action, string? entity, string? entityId, string? details,
        string? username = null, CancellationToken ct = default)
    {
        var ctx = http.HttpContext;
        var user = username ?? ctx?.User.Identity?.Name ?? "system";
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        var correlationId = ctx is null ? null : CorrelationIdMiddleware.Get(ctx);
        if (details is { Length: > 2000 }) details = details[..2000];

        try
        {
            await db.WriteAuditAsync(user, action, entity, entityId, details, ip, correlationId, ct);
        }
        catch (Exception ex)
        {
            // Never lose the event: fall back to the structured log, which is shipped off-box.
            logger.LogError(ex, "AUDIT WRITE FAILED {Action} {Entity} {EntityId} by {User}: {Details}", action, entity, entityId, user, details);
        }
    }
}
