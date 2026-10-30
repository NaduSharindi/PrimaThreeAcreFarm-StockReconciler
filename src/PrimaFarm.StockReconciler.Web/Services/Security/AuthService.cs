using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrimaFarm.StockReconciler.Web.Data;
using PrimaFarm.StockReconciler.Web.Models;

namespace PrimaFarm.StockReconciler.Web.Services.Security;

public class SecurityOptions
{
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int SessionTimeoutMinutes { get; set; } = 30;
    public long MaxUploadBytes { get; set; } = 10 * 1024 * 1024;
}

public enum LoginOutcome { Success, InvalidCredentials, LockedOut }

public sealed record LoginResult(LoginOutcome Outcome, AppUser? User = null);

public class AuthService(AppDbContext db, IOptions<SecurityOptions> options, AuditService audit)
{
    // Verified against when the username does not exist, so response time does not reveal valid usernames.
    private static readonly string DummyHash = PasswordHasher.Hash(Guid.NewGuid().ToString());

    public async Task<LoginResult> SignInAsync(string username, string password, CancellationToken ct)
    {
        var opts = options.Value;
        username = username.Trim();
        var user = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username, ct);

        if (user is null || !user.IsActive)
        {
            PasswordHasher.Verify(password, DummyHash);
            await audit.WriteAsync("LOGIN_FAILED", "AppUser", username, "Unknown or inactive user", username, ct);
            return new LoginResult(LoginOutcome.InvalidCredentials);
        }

        if (user.LockoutEndUtc > DateTime.UtcNow)
        {
            await audit.WriteAsync("LOGIN_LOCKED", "AppUser", username, $"Locked until {user.LockoutEndUtc:u}", username, ct);
            return new LoginResult(LoginOutcome.LockedOut);
        }

        var ok = PasswordHasher.Verify(password, user.PasswordHash);
        await db.RecordLoginAttemptAsync(user.Username, ok, opts.MaxFailedLogins, opts.LockoutMinutes, ct);
        await audit.WriteAsync(ok ? "LOGIN_SUCCESS" : "LOGIN_FAILED", "AppUser", user.Username, ok ? null : "Bad password", user.Username, ct);
        return ok ? new LoginResult(LoginOutcome.Success, user) : new LoginResult(LoginOutcome.InvalidCredentials);
    }
}
