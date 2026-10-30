using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Models;

namespace PrimaFarm.StockReconciler.Web.Controllers.Api;

[ApiController]
[Route("api/v1/locations")]
[Authorize(Policy = Policies.ApiClient)]
[EnableRateLimiting("api")]
[IgnoreAntiforgeryToken]   // header-authenticated, no cookies: CSRF does not apply
[Produces("application/json", "application/xml")]
public class LocationsController(AppDbContext db) : ControllerBase
{
    /// <summary>All farm locations and whether they are enabled for reconciliation.</summary>
    [HttpGet]
    public async Task<List<LocationDto>> Get(CancellationToken ct) =>
        await db.Locations.AsNoTracking().OrderBy(l => l.Name)
            .Select(l => new LocationDto { Code = l.Code, Name = l.Name, ErpLocationNo = l.ErpLocationNo, Enabled = l.IsEnabled, Status = l.Status })
            .ToListAsync(ct);
}
