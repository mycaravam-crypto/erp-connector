using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Connector.Integration.Tests;

/// <summary>
/// Shared login helper for every HTTP-layer test class in <see cref="ApiCollection"/>. Caches one token
/// per username for the lifetime of the test run instead of every authorization-requiring test logging in
/// independently — <c>/api/auth/login</c> is rate-limited to 20/minute per client (see
/// <c>AuthEndpointsHttpTests</c>), a ceiling the whole collection shares since it's all one app instance,
/// and that ceiling only gets tighter as more endpoint test files land here. Only <c>AuthEndpointsHttpTests</c>
/// itself calls <c>/api/auth/login</c> directly, since exercising login is the point there.
///
/// "alice" is reserved as the generic authenticated identity for every other test class — never revoke her
/// sessions in a test, or her cached token here goes stale for everyone else still relying on it.
/// </summary>
internal static class ApiAuth
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, Task<string>> TokenCache = new();

    internal static async Task<HttpClient> CreateAuthenticatedClientAsync(
        this ApiFactory factory,
        string username = "alice",
        string password = "alice123"
    )
    {
        var client = factory.CreateClient();
        var token = await TokenCache.GetOrAdd(username, name => LoginAsync(factory, name, password));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> LoginAsync(ApiFactory factory, string username, string password)
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password }, Json);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("token").GetString()!;
    }
}
