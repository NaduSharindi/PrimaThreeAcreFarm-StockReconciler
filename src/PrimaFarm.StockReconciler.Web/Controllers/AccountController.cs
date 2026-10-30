using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PrimaFarm.StockReconciler.Web.Models;
using PrimaFarm.StockReconciler.Web.Services;
using PrimaFarm.StockReconciler.Web.Services.Security;

namespace PrimaFarm.StockReconciler.Web.Controllers;

public class AccountController(AuthService auth, AuditService audit) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Dashboard");
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous, HttpPost, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var result = await auth.SignInAsync(model.Username, model.Password, ct);
        if (result.Outcome != LoginOutcome.Success)
        {
            // One generic message for unknown user / bad password (no username enumeration).
            ModelState.AddModelError("", result.Outcome == LoginOutcome.LockedOut
                ? "This account is temporarily locked after too many failed attempts. Try again later or contact IT."
                : "Invalid username or password.");
            model.Password = "";
            return View(model);
        }

        var user = result.User!;
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.GivenName, user.DisplayName),
            new Claim(ClaimTypes.Role, user.Role)
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // Only local redirects: prevents open-redirect phishing via ?returnUrl=https://evil
        return Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl!) : RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await audit.WriteAsync("LOGOUT", "AppUser", User.Identity?.Name, null, ct: ct);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
