using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Models;
using PrimaFarm.StockReconciler.Web.Services;
using PrimaFarm.StockReconciler.Web.Services.Reconciliation;

namespace PrimaFarm.StockReconciler.Web.Controllers;

public class RunsController(AppDbContext db, ReconciliationService service, AuditService audit) : Controller
{
    public async Task<IActionResult> Index(int? locationId, string? status, DateTime? from, DateTime? to, int page = 1, CancellationToken ct = default)
    {
        var model = new RunsIndexViewModel
        {
            LocationId = locationId,
            Status = string.IsNullOrWhiteSpace(status) ? null : status,
            From = from,
            To = to,
            Page = Math.Max(1, page),
            Locations = await db.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync(ct)
        };
        model.Rows = await db.SearchRunsAsync(locationId, model.Status, from, to?.AddDays(1), model.Page, model.PageSize, ct);
        model.TotalCount = model.Rows.FirstOrDefault()?.TotalCount ?? 0;
        return View(model);
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns.AsNoTracking().Include(r => r.Location).FirstOrDefaultAsync(r => r.RunId == id, ct);
        if (run is null) return NotFound();

        var isAdmin = User.IsInRole(Roles.Admin);
        return View(new RunDetailsViewModel
        {
            Run = run,
            TopVariances = await db.GetVarianceReportAsync(id, 50, ct),
            CanReview = isAdmin && run.Status == RunStatuses.Completed
                        && !string.Equals(run.CreatedBy, User.Identity?.Name, StringComparison.OrdinalIgnoreCase),
            CanRetryPosting = isAdmin && run.Status == RunStatuses.PostingFailed
        });
    }

    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var run = await db.ReconciliationRuns.AsNoTracking().AsSplitQuery()
            .Include(r => r.Location).Include(r => r.Lines)
            .FirstOrDefaultAsync(r => r.RunId == id, ct);
        if (run is null) return NotFound();

        await audit.WriteAsync("REPORT_DOWNLOADED", "ReconciliationRun", run.RunReference, null, ct: ct);
        return File(ExcelReportWriter.Build(run),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"Stock_Reconciliation_{run.Location!.Code}_{run.RunReference}.xlsx");
    }

    [HttpPost, Authorize(Policy = Policies.CanReview)]
    public Task<IActionResult> Approve(int id, string rowVersion, string? comment, CancellationToken ct) =>
        Review(id, () => service.ApproveAsync(id, Convert.FromBase64String(rowVersion), User.Identity!.Name!, Trim(comment), ct),
            "Approved. Variance journal sent to the finance system.");

    [HttpPost, Authorize(Policy = Policies.CanReview)]
    public Task<IActionResult> Reject(int id, string rowVersion, string? comment, CancellationToken ct) =>
        Review(id, () => service.RejectAsync(id, Convert.FromBase64String(rowVersion), User.Identity!.Name!, Trim(comment) ?? "", ct),
            "Reconciliation rejected and returned to the preparer.");

    [HttpPost, Authorize(Policy = Policies.CanReview)]
    public Task<IActionResult> RetryPosting(int id, CancellationToken ct) =>
        Review(id, () => service.RetryPostingAsync(id, User.Identity!.Name!, ct), "Finance posting retried.");

    private async Task<IActionResult> Review(int id, Func<Task> action, string success)
    {
        try
        {
            await action();
            TempData["Success"] = success;
        }
        catch (Exception ex) when (ex is BusinessRuleException or FormatException)
        {
            TempData["Error"] = ex is FormatException ? "Invalid request." : ex.Message;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, 500)];
}
