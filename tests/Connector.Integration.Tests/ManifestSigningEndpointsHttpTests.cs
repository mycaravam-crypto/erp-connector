using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Connector.Integration.Tests;

namespace Connector.Integration.Tests;

/// <summary>HTTP coverage for secure pairing administration and consistent instance-id validation.</summary>
[Collection(ApiCollection.Name)]
public sealed class ManifestSigningEndpointsHttpTests(ApiFactory factory)
{
    [Fact]
    public async Task InvalidPeerId_ReturnsBadRequestForPutAndDelete()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var put = await client.PutAsJsonAsync(
            "/api/settings/manifest-signing/peers/not-a-guid",
            new { sharedSecret = secret, useForOutbound = false }
        );
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);

        var delete = await client.DeleteAsync("/api/settings/manifest-signing/peers/not-a-guid");
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
    }

    [Fact]
    public async Task PairingSecret_IsNeverReturnedInGetResponse()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var id = Guid.NewGuid().ToString();
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        try
        {
            var put = await client.PutAsJsonAsync(
                $"/api/settings/manifest-signing/peers/{id}",
                new { sharedSecret = secret, useForOutbound = false }
            );
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

            var get = await client.GetAsync("/api/settings/manifest-signing");
            Assert.Equal(HttpStatusCode.OK, get.StatusCode);
            var payload = await get.Content.ReadAsStringAsync();
            Assert.Contains(id, payload);
            Assert.DoesNotContain(secret, payload);
            Assert.DoesNotContain("trustedPeerSecrets", payload, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await client.DeleteAsync($"/api/settings/manifest-signing/peers/{id}");
        }
    }
}
