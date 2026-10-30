using Microsoft.AspNetCore.Mvc;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Models;

namespace PrimaFarm.StockReconciler.Web.Controllers;

public class DashboardController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var to = DateTime.UtcNow.AddMinutes(1);
        var model = new DashboardViewModel
        {
            Days = days,
            Rows = await db.GetDashboardAsync(to.AddDays(-days), to, ct),
            PendingReview = await db.SearchRunsAsync(null, RunStatuses.Completed, null, null, 1, 10, ct)
        };
        return View(model);
    }
}
