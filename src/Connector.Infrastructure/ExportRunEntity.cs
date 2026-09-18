namespace Connector.Infrastructure;

/// <summary>
/// EF Core entity for the export log table.
/// Each row represents a completed or failed export run.
/// </summary>
public sealed class ExportRunEntity
{
    public int Id { get; set; }

    /// <summary>Monotonic sequence number — unique and never reused.</summary>
    public int SequenceNo { get; set; }

    /// <summary>UTC timestamp of the ERP run as an ISO-8601 string (SQLite has no native DateTimeOffset).</summary>
    public string ExtractedAt { get; set; } = string.Empty;

    /// <summary>Number of exported CI records. 0 when Status is Failed.</summary>
    public int RecordCount { get; set; }

    /// <summary>SHA-256 of the export file, hex lowercase. Empty when Status is Failed.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Pending | Released | Failed</summary>
    public string Status { get; set; } = ExportRunStatus.Pending;

    /// <summary>UTC timestamp of the four-eyes release. Null if not yet released.</summary>
    public string? ReleasedAt { get; set; }

    /// <summary>Username of the operator (creator) who triggered the export.</summary>
    public string? OperatedBy { get; set; }

    /// <summary>Username of the approver (release). Must differ from OperatedBy.</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>File name of the Excel file on the staging path. Empty when Status is Failed.</summary>
    public string DataFileName { get; set; } = string.Empty;

    // ── Delivery fields (Phase 6.4) ───────────────────────────────────────────
    // Populated after the export package has been physically transferred to the vendor.
    // All nullable — delivery tracking is optional and post-release.

    /// <summary>UTC timestamp of physical handover to vendor. Null = not yet delivered.</summary>
    public string? DeliveredAt { get; set; }

    /// <summary>Username of the person who performed the physical delivery.</summary>
    public string? DeliveredBy { get; set; }

    /// <summary>Number of records the vendor confirmed were imported downstream. Null = no confirmation.</summary>
    public int? ImportedRecordCount { get; set; }

    /// <summary>Free-text notes from the delivery or import confirmation (medium, handover ref, etc.).</summary>
    public string? DeliveryNotes { get; set; }
}

public static class ExportRunStatus
{
    public const string Pending = "Pending";
    public const string Released = "Released";
    public const string Failed = "Failed";

    /// <summary>Operator explicitly bypassed this run to recover from a permanent failure or sequence gap.</summary>
    public const string Skipped = "Skipped";
}
