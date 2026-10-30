using Microsoft.Extensions.Options;
using PrimaFarm.StockReconciler.Web.Services.Security;

namespace PrimaFarm.StockReconciler.Tests;

public class SecurityTests
{
    [Fact]
    public void Password_hash_round_trips_and_is_salted()
    {
        var a = PasswordHasher.Hash("Officer@2026");
        var b = PasswordHasher.Hash("Officer@2026");

        Assert.StartsWith("PBKDF2-SHA256$600000$", a);
        Assert.NotEqual(a, b);                                   // unique salt per hash
        Assert.True(PasswordHasher.Verify("Officer@2026", a));
        Assert.False(PasswordHasher.Verify("officer@2026", a));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("PBKDF2-SHA256$abc$salt$hash")]
    [InlineData("PBKDF2-SHA256$1000$!!notbase64!!$x")]
    public void Malformed_hashes_never_verify(string stored) =>
        Assert.False(PasswordHasher.Verify("anything", stored));

    [Fact]
    public void Seeded_admin_hash_matches_documented_default_password()
    {
        const string seeded = "PBKDF2-SHA256$600000$ml3RdACtEY9AdCIjFiy6lA==$llLXb7oN+fDZv0kV2gLE7ovLs8lffqz0bjxi4OOLgig=";
        Assert.True(PasswordHasher.Verify("Admin@2026", seeded));
    }

    [Fact]
    public void Api_key_validator_accepts_only_known_keys()
    {
        var options = new ApiClientOptions { Clients = { ["ErpGateway"] = ApiKeyValidator.HashKey("secret-key-1") } };
        var validator = new ApiKeyValidator(new StaticMonitor(options));

        Assert.Equal("ErpGateway", validator.Validate("secret-key-1"));
        Assert.Null(validator.Validate("secret-key-2"));
        Assert.Null(validator.Validate(""));
        Assert.Null(validator.Validate(new string('x', 500)));
    }

    private sealed class StaticMonitor(ApiClientOptions value) : IOptionsMonitor<ApiClientOptions>
    {
        public ApiClientOptions CurrentValue => value;
        public ApiClientOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<ApiClientOptions, string?> listener) => null;
    }
}
