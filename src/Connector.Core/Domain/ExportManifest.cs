namespace Connector.Core.Domain;

/// <summary>
/// Accompanies every export file. Lets the recipient verify integrity and detect gaps without a
/// return channel — the sequence number exposes gaps (e.g. a jump from #41 to #43).
/// </summary>
public sealed record ExportManifest(
    /// <summary>Monotonically increasing, starting at 1. Gaps indicate lost exports.</summary>
    int SequenceNumber,
    /// <summary>Schema version in MAJOR.MINOR format. Breaking changes increment MAJOR.</summary>
    string SchemaVersion,
    /// <summary>Time of the ERP extraction run (UTC).</summary>
    DateTimeOffset ExtractedAt,
    /// <summary>Number of records in the data file — must match the actual row count.</summary>
    int RecordCount,
    /// <summary>SHA-256 of the data file (hex, lowercase). Verified by the gateway before USB release.</summary>
    string Sha256Checksum
);
