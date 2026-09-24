namespace Connector.Core.Domain;

/// <summary>
/// Complete export package: data file bytes + manifest.
/// Both parts are written atomically to the staging path — only once both are
/// complete is the export considered ready for four-eyes release.
/// </summary>
public sealed record ExportPackage(
    ExportManifest Manifest,
    /// <summary>Content of the data file (e.g. .xlsx).</summary>
    byte[] DataFileBytes,
    /// <summary>File name without path, e.g. "export_0042_20260628T060000Z.xlsx".</summary>
    string DataFileName
);
