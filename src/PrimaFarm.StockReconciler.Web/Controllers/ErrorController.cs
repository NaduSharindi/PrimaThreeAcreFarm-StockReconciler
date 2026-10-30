using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PrimaFarm.StockReconciler.Web.Infrastructure;

namespace PrimaFarm.StockReconciler.Web.Controllers;

[AllowAnonymous, IgnoreAntiforgeryToken, ApiExplorerSettings(IgnoreApi = true)]
public class ErrorController : Controller
{
    [Route("/error")]
    public IActionResult Index()
    {
        var correlationId = CorrelationIdMiddleware.Get(HttpContext);
        var path = HttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? "";

        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, 500, "An unexpected error occurred.");
            problem.Extensions["correlationId"] = correlationId;
            return new ObjectResult(problem) { StatusCode = 500 };
        }

        Response.StatusCode = 500;
        return View("~/Views/Shared/Error.cshtml", correlationId);
    }
}
