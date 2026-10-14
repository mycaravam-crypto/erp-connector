using Connector.Core.Domain;

namespace Connector.Infrastructure;

/// <summary>
/// Pairing keys live in the encrypted AppSetting.Value column, never in API responses or logs.
/// The outbound peer selects one key for signing; inbound verification selects by Producer.InstanceId.
/// </summary>
public sealed class ManifestSigningSettings
{
    public Dictionary<string, string> TrustedPeerSecrets { get; set; } = new(StringComparer.Ordinal);
    public string? OutboundPeerInstanceId { get; set; }
    public bool RequireSignedConnectorFiles { get; set; }
}

public static class ManifestSigningPolicy
{
    /// <summary>Sign an outgoing manifest when an outbound pairing is configured.</summary>
    public static async Task<ExportManifest> SignAsync(ExportLogDbContext db, ExportManifest manifest)
    {
        var settings = await db.GetSettingAsync<ManifestSigningSettings>(SettingsKeys.ManifestSigning);
        if (settings?.OutboundPeerInstanceId is not { } peer)
            return manifest;
        if (!settings.TrustedPeerSecrets.TryGetValue(peer, out var secret))
            throw new InvalidOperationException("Outbound pairing key is not configured.");

        var key = Convert.FromBase64String(secret);
        return manifest with { Signature = ManifestSignature.Sign(manifest.Sha256Checksum, manifest.Producer, key) };
    }

    /// <summary>
    /// Null means authenticated/allowed; other values are safe, non-secret quarantine reasons.
    /// A configured trusted peer must always sign, even if global enforcement is off.
    /// </summary>
    public static string? Verify(ImportManifest manifest, ManifestSigningSettings? settings, bool pairedDefinition)
    {
        if (manifest.Producer is null)
            return settings?.RequireSignedConnectorFiles == true && pairedDefinition
                    ? "unsigned file targets a paired import definition"
                : manifest.Signature is null ? null
                : "signature has no producer identity";

        if (settings is null || !settings.TrustedPeerSecrets.TryGetValue(manifest.Producer.InstanceId, out var secret))
            return settings?.RequireSignedConnectorFiles == true || manifest.Signature is not null
                ? "producer instance is not trusted"
                : null;

        if (manifest.Signature is null)
            return "missing manifest signature from trusted producer";

        try
        {
            var key = Convert.FromBase64String(secret);
            return ManifestSignature.Verify(manifest.Sha256Checksum, manifest.Producer, manifest.Signature, key)
                ? null
                : "invalid manifest signature";
        }
        catch (FormatException)
        {
            return "trusted producer key is invalid";
        }
    }
}
