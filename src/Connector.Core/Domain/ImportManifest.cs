namespace Connector.Core.Domain;

/// <summary>
/// Accompanies every inbound vendor file — the reverse-direction counterpart to <see cref="ExportManifest"/>,
/// mirroring <see cref="FileSystemExportSink"/>'s outbound contract. No <c>SequenceNumber</c>: vendor
/// confirmations don't arrive 1:1 per export run, so gap detection doesn't apply — the SHA-256 checksum
/// alone covers file integrity. Which saved <c>ImportDefinition</c> a file targets is carried inside the
/// data file's own <c>ImportEnvelope</c> (its <c>definition</c> field), not here — the manifest's only job
/// is integrity.
/// </summary>
public sealed record ImportManifest(
    /// <summary>SHA-256 over the data file (hex, lowercase), verified by
    /// <c>Connector.Infrastructure.ImportWorker</c> before the file is parsed.</summary>
    string Sha256Checksum
);
