using Connector.Infrastructure;

namespace Connector.Integration.Tests;

public sealed class ApiKeyStoreTests
{
    [Fact]
    public void TryAuthenticate_MatchingKey_ReturnsTrueWithConfiguredName()
    {
        var store = new ApiKeyStore(
            new Dictionary<string, string> { [ApiKeyStore.Hash("secret-key-1")] = "erp-bot" }
        );

        var authenticated = store.TryAuthenticate("secret-key-1", out var name);

        Assert.True(authenticated);
        Assert.Equal("erp-bot", name);
    }

    [Fact]
    public void TryAuthenticate_WrongKey_ReturnsFalse()
    {
        var store = new ApiKeyStore(
            new Dictionary<string, string> { [ApiKeyStore.Hash("secret-key-1")] = "erp-bot" }
        );

        var authenticated = store.TryAuthenticate("wrong-key", out var name);

        Assert.False(authenticated);
        Assert.Equal("", name);
    }

    [Fact]
    public void TryAuthenticate_NoConfiguredKeys_ReturnsFalse()
    {
        var store = new ApiKeyStore(new Dictionary<string, string>());

        Assert.False(store.TryAuthenticate("anything", out _));
    }

    [Fact]
    public void TryAuthenticate_MultipleConfiguredKeys_MatchesTheRightOne()
    {
        var store = new ApiKeyStore(
            new Dictionary<string, string>
            {
                [ApiKeyStore.Hash("key-for-alice-bot")] = "alice-bot",
                [ApiKeyStore.Hash("key-for-bob-bot")] = "bob-bot",
            }
        );

        Assert.True(store.TryAuthenticate("key-for-bob-bot", out var name));
        Assert.Equal("bob-bot", name);
    }

    [Fact]
    public void Hash_SameInput_IsDeterministicAndLowercaseHex()
    {
        var hash = ApiKeyStore.Hash("some-key");

        Assert.Equal(hash, ApiKeyStore.Hash("some-key"));
        Assert.Equal(64, hash.Length); // SHA-256 = 32 bytes = 64 hex chars
        Assert.Equal(hash, hash.ToLowerInvariant());
    }
}
