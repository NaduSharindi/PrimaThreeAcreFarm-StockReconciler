using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrimaFarm.StockReconciler.Web.Data;

namespace PrimaFarm.StockReconciler.Web.Controllers;

[Authorize(Policy = Policies.CanViewAudit)]
public class AdminController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Audit(string? user, string? action, CancellationToken ct)
    {
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.Username == user);
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        ViewBag.User = user;
        ViewBag.Action = action;
        return View(await q.OrderByDescending(a => a.AuditId).Take(200).ToListAsync(ct));
    }

    public async Task<IActionResult> Integrations(CancellationToken ct) =>
        View(await db.IntegrationLogs.AsNoTracking().OrderByDescending(l => l.IntegrationLogId).Take(100).ToListAsync(ct));
}
