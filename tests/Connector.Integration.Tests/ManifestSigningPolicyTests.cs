using Connector.Core.Domain;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

public sealed class ManifestSigningPolicyTests : SqliteDbContextTestBase
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly ExportProducer Remote = new(
        ExportProducer.ApplicationName, "1.2.3", "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
    );
    private const string Checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static ManifestSigningSettings TrustRemote() =>
        new()
        {
            RequireSignedConnectorFiles = true,
            TrustedPeerSecrets = new Dictionary<string, string> { [Remote.InstanceId] = Convert.ToBase64String(Key) },
        };

    [Fact]
    public void TrustedPeer_RejectsMissingInvalidOrChangedSignature()
    {
        var config = TrustRemote();
        var signature = ManifestSignature.Sign(Checksum, Remote, Key);
        Assert.Null(ManifestSigningPolicy.Verify(new ImportManifest(Checksum, Remote, signature), config, true));
        Assert.Equal("missing manifest signature from trusted producer",
            ManifestSigningPolicy.Verify(new ImportManifest(Checksum, Remote), config, true));
        Assert.Equal("invalid manifest signature",
            ManifestSigningPolicy.Verify(new ImportManifest(Checksum, Remote with { Version = "5" }, signature), config, true));
    }

    [Fact]
    public void Policy_RejectsUnknownProducerAndUnsignedPairedImport_ButAllowsVendor()
    {
        var config = TrustRemote();
        var unknown = Remote with { InstanceId = Guid.NewGuid().ToString() };
        Assert.NotNull(ManifestSigningPolicy.Verify(new ImportManifest(Checksum, unknown), config, false));
        Assert.NotNull(ManifestSigningPolicy.Verify(new ImportManifest(Checksum), config, true));
        Assert.Null(ManifestSigningPolicy.Verify(new ImportManifest(Checksum), config, false));
    }

    [Fact]
    public async Task OutboundSign_UsesEncryptedSettingAndCanBeVerifiedByReceivingPeer()
    {
        var peerId = Guid.NewGuid().ToString();
        var config = new ManifestSigningSettings
        {
            OutboundPeerInstanceId = peerId,
            TrustedPeerSecrets = new Dictionary<string, string> { [peerId] = Convert.ToBase64String(Key) },
        };
        await Db.SetSettingAsync(SettingsKeys.ManifestSigning, config);
        var producer = await Db.GetProducerAsync();
        var manifest = new ExportManifest(null, "2.0", DateTimeOffset.UtcNow, 1, Checksum, producer);
        var signed = await ManifestSigningPolicy.SignAsync(Db, manifest);

        Assert.NotNull(signed.Signature);
        Assert.Null(ManifestSigningPolicy.Verify(
            new ImportManifest(Checksum, producer, signed.Signature),
            new ManifestSigningSettings {
                RequireSignedConnectorFiles = true,
                TrustedPeerSecrets = new Dictionary<string, string> { [producer.InstanceId] = Convert.ToBase64String(Key) }
            },
            true
        ));
    }
}
