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
    string Sha256Checksum,
    /// <summary>Which connector instance wrote the export, so a receiving connector can tell another connector's
    /// export apart from a vendor file, and two connector instances apart from each other.</summary>
    ExportProducer Producer
);

/// <summary>Identity of the connector that wrote an export.</summary>
public sealed record ExportProducer(
    /// <summary>Always <see cref="ApplicationName"/> — marks the file as written by this connector.</summary>
    string Application,
    /// <summary>Connector version (the repo's VERSION file) of the writing instance.</summary>
    string Version,
    /// <summary>Random id generated once per installation and kept in its database. Stable across restarts
    /// and upgrades.</summary>
    string InstanceId
)
{
    public const string ApplicationName = "x5-connector";
}
