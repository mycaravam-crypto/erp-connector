using Connector.Core.Domain;

namespace Connector.Core.Tests;

public sealed class ManifestSignatureTests
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
    private static readonly ExportProducer Producer = new("x5-connector", "1.2.3", Guid.NewGuid().ToString());
    private const string Checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void RoundTrip_VerifiesWithSameKeyAndProducer()
    {
        var signature = ManifestSignature.Sign(Checksum, Producer, Key);
        Assert.Equal(64, signature.Length);
        Assert.True(ManifestSignature.Verify(Checksum, Producer, signature, Key));
    }

    [Fact]
    public void TamperingOrWrongKey_InvalidatesSignature()
    {
        var signature = ManifestSignature.Sign(Checksum, Producer, Key);
        Assert.False(ManifestSignature.Verify(new string('a', 64), Producer, signature, Key));
        Assert.False(ManifestSignature.Verify(Checksum, Producer with { Version = "2.0" }, signature, Key));
        Assert.False(ManifestSignature.Verify(Checksum, Producer with { InstanceId = Guid.NewGuid().ToString() }, signature, Key));
        Assert.False(ManifestSignature.Verify(Checksum, Producer, signature, Enumerable.Repeat((byte)7, 32).ToArray()));
        Assert.False(ManifestSignature.Verify(Checksum, Producer, null, Key));
        Assert.False(ManifestSignature.Verify(Checksum, Producer, "invalid", Key));
    }
}
