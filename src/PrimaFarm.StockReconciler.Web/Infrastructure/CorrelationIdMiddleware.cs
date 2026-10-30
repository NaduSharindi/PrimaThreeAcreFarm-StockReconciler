using System.Text.RegularExpressions;
using Serilog.Context;

namespace PrimaFarm.StockReconciler.Web.Infrastructure;

/// <summary>
/// Gives every request a correlation id (accepts a safe inbound X-Correlation-ID or creates one),
/// echoes it on the response and pushes it into every log line. Users quote it when reporting an
/// incident, so support can find the exact request in the logs, audit and integration tables.
/// </summary>
public partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    private const string ItemKey = "CorrelationId";

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$")]
    private static partial Regex SafeId();

    public async Task InvokeAsync(HttpContext context)
    {
        var inbound = context.Request.Headers[HeaderName].ToString();
        // Reject anything that could be used for log injection.
        var id = SafeId().IsMatch(inbound) ? inbound : Guid.NewGuid().ToString("N");
        context.Items[ItemKey] = id;
        context.Response.Headers[HeaderName] = id;

        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(context);
        }
    }

    public static string Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var v) && v is string s ? s : context.TraceIdentifier;
}
