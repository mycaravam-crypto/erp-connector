using System.Security.Claims;
using System.Text.Encodings.Web;
using Connector.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Connector.Api;

/// <summary>
/// Authenticates a request via the <c>X-Api-Key</c> header against <see cref="ApiKeyStore"/>, as an
/// alternative to the interactive JWT login flow (<see cref="Endpoints.AuthEndpoints"/>) for a "dedicated
/// API user" — a machine calling a specific endpoint directly (e.g. an external system triggering a saved
/// export preset). Registered as its own scheme ("ApiKey") alongside the default JWT Bearer scheme, so
/// only endpoints that opt in via <c>RequireAuthorization(policy => policy.AddAuthenticationSchemes(...))</c>
/// accept it — every other endpoint keeps requiring a JWT exactly as before.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ApiKeyStore store
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    private const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var rawKey = values[0];
        if (string.IsNullOrEmpty(rawKey) || !store.TryAuthenticate(rawKey, out var name))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, name)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
