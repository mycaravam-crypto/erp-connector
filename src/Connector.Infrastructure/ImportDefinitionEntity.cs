namespace Connector.Infrastructure;

/// <summary>
/// One saved import mapping: the ERP table/column an inbound record's correlation key must resolve
/// against, its <see cref="Connector.Core.DynamicImport.ImportNode"/> tree, and the explicit allowlist
/// of columns it may ever write. Phase 17's write-side counterpart to <see cref="ExportDefinitionEntity"/>.
/// </summary>
public sealed class ImportDefinitionEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RootTable { get; set; } = string.Empty;

    /// <summary>Column on <see cref="RootTable"/> an inbound record's correlation key (the same Guid
    /// exported today, per Open Point #1) is matched against. No match → the record is excluded per
    /// <see cref="UnmatchedRootPolicy"/> — never auto-created.</summary>
    public string RootMatchColumn { get; set; } = string.Empty;

    /// <summary><see cref="Connector.Core.DynamicImport.ImportNode"/> serialized as JSON — same
    /// storage approach as <see cref="ExportDefinitionEntity.RootNode"/>. Read/write only via
    /// <see cref="Connector.Core.DynamicImport.ImportNodeJson"/>, never a raw
    /// <see cref="System.Text.Json.JsonSerializer"/> call, so missing-property backfill always applies.</summary>
    public string RootNode { get; set; } = string.Empty;

    /// <summary>Explicit allowlist of columns this definition may ever write, serialized as a JSON
    /// string array (same storage approach as <see cref="RootNode"/>) — the inverse of the export
    /// side's GDPR denylist (default-deny instead of default-allow-except). Validated at save time
    /// (Slice 5) and re-checked by the walker at run time (Slice 2), so a stale saved definition can
    /// never be trusted silently.</summary>
    public string AllowedWritableColumns { get; set; } = "[]";

    /// <summary>reject | quarantine — see <see cref="Connector.Core.DynamicImport.UnmatchedRootPolicy"/>.
    /// Deliberately has no "auto-create" option (see import-definitions.md §1).</summary>
    public string UnmatchedRootPolicy { get; set; } = Connector.Core.DynamicImport.UnmatchedRootPolicy.Reject;

    public bool IsEnabled { get; set; }

    /// <summary>Bumped on every save; carried onto each <see cref="ImportRunEntity"/> so a run's
    /// history row records exactly which version of the definition produced it — same convention as
    /// <see cref="ExportDefinitionEntity.ConfigVersion"/>.</summary>
    public int ConfigVersion { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
    public string? UpdatedAt { get; set; }
}

/// <summary>Status values for <see cref="ImportRunEntity.Status"/>. Unlike
/// <see cref="ExportDefinitionRunStatus"/> (no approval step), an import run requires four-eyes review
/// before it commits — mirroring the legacy <see cref="ExportRunStatus"/> lifecycle instead.</summary>
public static class ImportRunStatus
{
    public const string PendingReview = "PendingReview";
    public const string Released = "Released";
    public const string Rejected = "Rejected";
    public const string Failed = "Failed";
}

/// <summary>
/// One staged, released, rejected, or failed run of an <see cref="ImportDefinitionEntity"/> — mirrors
/// <see cref="ExportDefinitionRunEntity"/>'s per-definition run-history shape, combined with
/// <see cref="ExportRunEntity"/>'s four-eyes fields (an import commits to the system of record, a
/// materially bigger risk than reading, so it requires the same Operator/Approver review as export
/// release — see import-definitions.md §3).
/// </summary>
public sealed class ImportRunEntity
{
    public int Id { get; set; }
    public int ImportDefinitionId { get; set; }
    public int ConfigVersion { get; set; }

    public string SourceFileName { get; set; } = string.Empty;

    /// <summary>SHA-256 of the inbound file, hex lowercase. No SequenceNumber field — per resolved
    /// Open Decision #8, the manifest alone covers file integrity for v1 (see import-definitions.md §6).</summary>
    public string Sha256Checksum { get; set; } = string.Empty;

    public string StartedAt { get; set; } = string.Empty;
    public string? FinishedAt { get; set; }
    public string Status { get; set; } = ImportRunStatus.PendingReview;

    public int RecordCount { get; set; }
    public int AcceptedCount { get; set; }
    public int RejectedCount { get; set; }

    /// <summary>The persisted field-level diff (old value → new value per accepted row), computed once
    /// by the walker (Slice 2) and reused verbatim by both the review UI and the commit step (Slice 3)
    /// so preview and commit can never disagree about what a row means.</summary>
    public string? DiffJson { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Username of the Operator, inferred from JWT at release time — null until a human acts
    /// on a <see cref="ImportRunStatus.PendingReview"/> run.</summary>
    public string? OperatedBy { get; set; }

    /// <summary>Username of the Approver — must be distinct from <see cref="OperatedBy"/>, enforced server-side.</summary>
    public string? ApprovedBy { get; set; }

    public string? ReleasedAt { get; set; }

    /// <summary>Username for a manually triggered run, or a fixed marker (e.g. "watcher") for the
    /// inbound folder-poll trigger (Slice 4).</summary>
    public string TriggeredBy { get; set; } = string.Empty;
}
