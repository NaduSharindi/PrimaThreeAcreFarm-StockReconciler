using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Infrastructure;
using PrimaFarm.StockReconciler.Web.Models;
using PrimaFarm.StockReconciler.Web.Services.Reconciliation;
using PrimaFarm.StockReconciler.Web.Services.Security;

namespace PrimaFarm.StockReconciler.Web.Controllers;

[Authorize(Policy = Policies.CanRun)]
public class ReconcileController(
    AppDbContext db,
    ReconciliationService service,
    IOptions<SecurityOptions> security) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new ReconcileViewModel { Locations = await LoadLocationsAsync(ct) });

    [HttpPost]
    [RequestSizeLimit(40 * 1024 * 1024)]
    public async Task<IActionResult> Index(int locationId, IFormFile? erpFile, List<IFormFile> farmFiles, CancellationToken ct)
    {
        var model = new ReconcileViewModel { Locations = await LoadLocationsAsync(ct), SelectedLocationId = locationId };
        var opened = new List<UploadedFile>();
        try
        {
            if (erpFile is null) throw new BusinessRuleException("Please choose the ERP / system file.");
            var files = farmFiles.Where(f => f.Length > 0).Take(3).ToList();
            if (files.Count == 0) throw new BusinessRuleException("Please choose at least one farm stock file.");

            var max = security.Value.MaxUploadBytes;
            var erp = await FileUploadValidator.ReadAsync(erpFile, max, ct);
            opened.Add(erp);
            foreach (var f in files) opened.Add(await FileUploadValidator.ReadAsync(f, max, ct));

            var run = await service.RunFromFilesAsync(locationId, erp, opened.Skip(1).ToList(), User.Identity!.Name!, ct);
            TempData["Success"] = $"Reconciliation {run.RunReference} completed. Download the Excel report or send it for review.";
            return RedirectToAction("Details", "Runs", new { id = run.RunId });   // Post/Redirect/Get
        }
        catch (Exception ex) when (ex is BusinessRuleException or StockFileFormatException)
        {
            model.Error = ex.Message;
        }
        finally
        {
            foreach (var f in opened) await f.Content.DisposeAsync();
        }
        return View(model);
    }

    private Task<List<Location>> LoadLocationsAsync(CancellationToken ct) =>
        db.Locations.AsNoTracking().OrderByDescending(l => l.IsEnabled).ThenBy(l => l.Name).ToListAsync(ct);
}
