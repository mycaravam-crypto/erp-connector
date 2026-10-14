using System.Security.Cryptography;
using System.Text;

namespace Connector.Core.Domain;

/// <summary>
/// Versioned, deterministic HMAC-SHA256 binding a data checksum to the complete producer identity.
/// Length-prefixed UTF-8 fields prevent concatenation ambiguities; comparisons are constant-time.
/// </summary>
public static class ManifestSignature
{
    private static byte[] Payload(string sha256Checksum, ExportProducer producer)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)1);
        writer.Write(sha256Checksum.ToLowerInvariant());
        writer.Write(producer.Application);
        writer.Write(producer.Version);
        writer.Write(producer.InstanceId);
        writer.Flush();
        return stream.ToArray();
    }

    public static string Sign(string sha256Checksum, ExportProducer producer, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(producer);
        if (key.Length < 32)
            throw new ArgumentException("Signing key must contain at least 32 bytes.", nameof(key));

        return Convert.ToHexString(HMACSHA256.HashData(key, Payload(sha256Checksum, producer))).ToLowerInvariant();
    }

    /// <summary>Verify a manifest HMAC with a constant-time comparison; reject malformed signatures.</summary>
    public static bool Verify(string sha256Checksum, ExportProducer producer, string? signature, byte[] key)
    {
        if (signature is null || signature.Length != 64)
            return false;
        try
        {
            var supplied = Convert.FromHexString(signature);
            var expected = HMACSHA256.HashData(key, Payload(sha256Checksum, producer));
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
