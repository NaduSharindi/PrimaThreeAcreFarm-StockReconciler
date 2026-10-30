using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PrimaFarm.StockReconciler.Web.Services.Security;

namespace PrimaFarm.StockReconciler.Web.Infrastructure;

/// <summary>Authenticates system-to-system calls (REST API and SOAP) with the X-Api-Key header.</summary>
public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string ClientRole = "ApiClient";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyValidator.HeaderName, out var key))
            return Task.FromResult(AuthenticateResult.NoResult());

        var client = validator.Validate(key);
        if (client is null)
        {
            Logger.LogWarning("Rejected API key from {Ip}", Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, $"api:{client}"), new Claim(ClaimTypes.Role, ClientRole)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
