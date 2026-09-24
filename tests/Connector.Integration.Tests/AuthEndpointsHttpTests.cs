using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api;

namespace Connector.Integration.Tests;

/// <summary>
/// HTTP-layer coverage for <see cref="AuthEndpoints"/> against the real <see cref="Program"/> pipeline (see
/// <see cref="ApiFactory"/>) — login (including the unknown-username-vs-wrong-password timing-safety
/// branch), the dev-only <c>/api/auth/hash</c> endpoint, and the session-revocation flow. Shares one
/// <see cref="ApiFactory"/> (and therefore one login rate-limit bucket, capped at 20/minute) across every
/// test in this class, so the handful of tests here stay well under that ceiling.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthEndpointsHttpTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ApiFactory _factory;

    public AuthEndpointsHttpTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_ValidDevCredentials_ReturnsTokenAndUsername()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "alice", password = "alice123" },
            Json
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("alice", body.GetProperty("username").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
    }

    [Fact]
    public async Task Login_WrongPasswordForKnownUser_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "alice", password = "not-alices-password" },
            Json
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // An unknown username must fail the same way (401, same rough latency shape) as a known
    // username with a wrong password — never a distinguishable response that leaks which usernames exist.
    [Fact]
    public async Task Login_UnknownUsername_ReturnsUnauthorizedNotNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "no-such-user", password = "whatever" },
            Json
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Hash_DevOnlyEndpoint_ReturnsBcryptHashThatVerifiesAgainstThePlaintext()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/hash", new { password = "correct horse battery" }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var hash = body.GetProperty("hash").GetString();
        Assert.NotNull(hash);
        Assert.True(BCrypt.Net.BCrypt.Verify("correct horse battery", hash));
    }

    [Fact]
    public async Task RevokeMySessions_NoAuthorizationHeader_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/auth/revoke-my-sessions", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // Revoking invalidates every outstanding token for that user, including — after this call
    // completes — the very token used to make it (validated against the pre-revocation cutover, so this
    // first call still succeeds; a second call with the same now-stale token must not).
    [Fact]
    public async Task RevokeMySessions_ThenReusingTheSameToken_TheSecondCallIsRejected()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "bob", password = "bob123" },
            Json
        );
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var firstRevoke = await client.PostAsync("/api/auth/revoke-my-sessions", content: null);
        Assert.Equal(HttpStatusCode.OK, firstRevoke.StatusCode);

        var secondRevoke = await client.PostAsync("/api/auth/revoke-my-sessions", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, secondRevoke.StatusCode);
    }
}
