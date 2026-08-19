namespace Connector.Infrastructure;

/// <summary>
/// One saved, independently named and scheduled export: a name, the table it's rooted at, its
/// <see cref="Connector.Core.DynamicExport.ExportNode"/> tree, and the output format/schedule it runs
/// with. Phase 14's replacement for the single AppSetting-backed mapping + presets
/// (<see cref="SettingsKeys.ExportMapping"/>/<see cref="SettingsKeys.ExportPresets"/>).
/// </summary>
public sealed class ExportDefinitionEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string RootTable { get; set; } = string.Empty;

    /// <summary><see cref="Connector.Core.DynamicExport.ExportNode"/> serialized as JSON — same
    /// storage approach as <see cref="AppSettingEntity.Value"/>, since this codebase has no prior use
    /// of EF's native JSON-column support. Read/write only via
    /// <see cref="Connector.Core.DynamicExport.ExportNodeJson"/>, never a raw
    /// <see cref="System.Text.Json.JsonSerializer"/> call, so missing-property backfill always applies.</summary>
    public string RootNode { get; set; } = string.Empty;

    /// <summary>csv | xlsx | json — same format strings <see cref="SchedulerConfigData.Format"/> and the
    /// legacy export-mapping settings endpoint already use.</summary>
    public string OutputFormat { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    /// <summary>Cron expression at hourly-or-coarser granularity. Null means manual-only — no scheduled runs.</summary>
    public string? Schedule { get; set; }

    /// <summary>Bumped on every save; carried onto each <see cref="ExportDefinitionRunEntity"/> so a run's
    /// history row records exactly which version of the definition produced it.</summary>
    public int ConfigVersion { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? UpdatedBy { get; set; }
    public string? UpdatedAt { get; set; }
}

/// <summary>Status values for <see cref="ExportDefinitionRunEntity.Status"/>. Deliberately separate from
/// <see cref="ExportRunStatus"/>, which models the legacy pipeline's four-eyes release workflow — a
/// per-definition run has no approval step, just success/failure.</summary>
public static class ExportDefinitionRunStatus
{
    public const string Running = "Running";
    public const string Success = "Success";
    public const string Failed = "Failed";
}

/// <summary>One completed, failed, or in-progress run of an <see cref="ExportDefinitionEntity"/> — the
/// Phase 14 per-definition analogue of <see cref="ExportRunEntity"/>, which stays scoped to the legacy
/// single-mapping pipeline. Every run (scheduled, manual, or test) writes exactly one row here, with
/// <see cref="Status"/> set to <see cref="ExportDefinitionRunStatus.Failed"/> and
/// <see cref="ErrorMessage"/> populated on any failure — never a silent partial success.</summary>
public sealed class ExportDefinitionRunEntity
{
    public int Id { get; set; }
    public int ExportDefinitionId { get; set; }
    public int ConfigVersion { get; set; }
    public string StartedAt { get; set; } = string.Empty;
    public string? FinishedAt { get; set; }
    public string Status { get; set; } = ExportDefinitionRunStatus.Running;
    public int RecordCount { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Username for a manual/test run, or a fixed marker (e.g. <c>"scheduler"</c>) for a scheduled one.</summary>
    public string TriggeredBy { get; set; } = string.Empty;

    /// <summary>True for a capped preview/test run (see Phase 14's 50-row test cap) — kept out of
    /// normal execution-history summaries the same way a dry run shouldn't count as a real export.</summary>
    public bool IsTestRun { get; set; }
}
