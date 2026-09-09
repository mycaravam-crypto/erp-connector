namespace Connector.Api;

record ExportRunSummary(
    int SequenceNo,
    string ExtractedAt,
    int RecordCount,
    string Sha256Short,
    string Status,
    string DataFileName,
    bool IsStale
);

/// <summary>
/// Full export run detail. SequenceGapWarning is non-null when a Pending run has a gap
/// relative to the last released run — operators should investigate before releasing.
/// Delivery fields are null until the physical handover is recorded via POST …/deliver.
/// </summary>
record ExportDetailDto(
    int Id,
    int SequenceNo,
    string ExtractedAt,
    int RecordCount,
    string Sha256,
    string Status,
    string? ReleasedAt,
    string? OperatedBy,
    string? ApprovedBy,
    string DataFileName,
    string? DeliveredAt,
    string? DeliveredBy,
    int? ImportedRecordCount,
    string? DeliveryNotes,
    string? SequenceGapWarning
);

/// <summary>
/// Operator is taken from the JWT; the approver's own name and password are supplied in the body.
/// ApproverPassword proves the named approver actually participated in the release — see
/// <see cref="Connector.Api.Endpoints.FourEyesReview.ValidateApprover"/> — rather than letting the operator
/// unilaterally release by typing a colleague's username with nothing to back it.
/// </summary>
record ReleaseRequest(string Approver, string? ApproverPassword);

/// <summary>Body for POST …/deliver. ImportedRecordCount and Notes are optional confirmation data.</summary>
record DeliverRequest(int? ImportedRecordCount, string? Notes);

record LoginRequest(string Username, string Password);

record LoginResponse(string Token, string Username);

record HashRequest(string Password);

record SchemaColumnDto(string Name, string ErpSource, string Type, string Notes, bool Active, string? ExportName);

record SchemaDto(string Version, SchemaColumnDto[] Columns);

record SourceColumnDto(
    string Name,
    string Type,
    bool Nullable,
    bool PrimaryKey,
    string? ForeignKeyTable = null,
    string? ForeignKeyColumn = null,
    // Populated by ConnectionEndpoints.IntrospectSchemaAsync from information_schema.columns'
    // own is_identity/is_generated columns. Added for Phase 17 Slice 5 (import-definitions.md §6
    // Open Decision #9): the save-time AllowedWritableColumns validator needs to reject a
    // TargetColumn the ERP itself manages (an identity sequence or a GENERATED ... STORED
    // expression), not just one that's the primary key or an FK. Defaults false so every existing
    // SourceColumnDto call site (the hardcoded demo schema) stays valid without updating.
    bool IsIdentity = false,
    bool IsGenerated = false
);

record SourceTableDto(string Name, string Description, SourceColumnDto[] Columns);

record SourceSchemaDto(string ConnectionLabel, SourceTableDto[] Tables);

record RunNowResult(int SequenceNo, int RecordCount, string Sha256Short);

/// <summary>
/// Source is "dynamic" for a flat mapping (Columns/Records populated), "dynamic-nested" for a mapping
/// with nested JSON groups (NestedRecords populated instead — arbitrary object/array shape, not a flat
/// table), or "error" when nothing could be previewed.
/// </summary>
record PreviewResult(
    int RecordCount,
    string SchemaVersion,
    IReadOnlyList<string> Columns,
    IList<Dictionary<string, string>> Records,
    string Source = "demo",
    string? SourceTable = null,
    string? Error = null,
    System.Text.Json.Nodes.JsonArray? NestedRecords = null
);

/// <summary>Public view of the stored connection — no password field.</summary>
record ErpConnectionInfo(string Host, int Port, string Database, string Username);

/// <summary>Body for POST /api/exports/{seqNo}/skip. Reason is stored in the audit log.</summary>
record SkipRequest(string? Reason);

/// <summary>Body for PATCH /api/gdpr-denied-fields.</summary>
record GdprDenylistRequest(List<string> Fields);

/// <summary>Single row returned by GET /api/audit.</summary>
record AuditEntryDto(int Id, string Timestamp, string Username, string Action, string? Detail);

/// <summary>Body for POST/PUT /api/export-definitions — everything an operator configures for one saved,
/// independently triggerable export. RootNode must be a "root"-kind <see cref="Connector.Core.DynamicExport.ExportNode"/>.
/// IntegrationKey/ContractVersion/CorrelationKeySourceField (knowledge/pipeline/import-mapping-presets.md
/// §3.1) are optional and only used by the not-yet-built import-mapping-presets suggestion feature — set
/// together or not at all, validated at save time by
/// <see cref="Connector.Api.Endpoints.ExportDefinitionEndpoints"/>.</summary>
record ExportDefinitionRequest(
    string Name,
    string? Description,
    string RootTable,
    Connector.Core.DynamicExport.ExportNode RootNode,
    string OutputFormat,
    bool IsEnabled,
    string? Schedule,
    string? IntegrationKey = null,
    int? ContractVersion = null,
    string? CorrelationKeySourceField = null
);

/// <summary>Full view of a saved export definition, returned by GET/POST/PUT .../{id}.</summary>
record ExportDefinitionDto(
    int Id,
    string Name,
    string? Description,
    string RootTable,
    Connector.Core.DynamicExport.ExportNode RootNode,
    string OutputFormat,
    bool IsEnabled,
    string? Schedule,
    int ConfigVersion,
    string CreatedBy,
    string CreatedAt,
    string? UpdatedBy,
    string? UpdatedAt,
    string? IntegrationKey,
    int? ContractVersion,
    string? CorrelationKeySourceField
);

/// <summary>Lightweight list-view row for GET /api/export-definitions — omits RootNode, which can be
/// arbitrarily deep and isn't needed to identify/trigger a definition.</summary>
record ExportDefinitionSummaryDto(
    int Id,
    string Name,
    string? Description,
    string RootTable,
    string OutputFormat,
    bool IsEnabled,
    string? Schedule,
    int ConfigVersion,
    string CreatedBy,
    string CreatedAt,
    string? UpdatedBy,
    string? UpdatedAt
);

/// <summary>Body for POST .../duplicate. Name is optional — defaults to "{original} (Copy)".</summary>
record DuplicateExportDefinitionRequest(string? Name);

/// <summary>Body for PATCH .../enable.</summary>
record EnableRequest(bool Enabled);

/// <summary>Result of a run/test — the tracked <c>ExportDefinitionRunEntity</c> row as an API-facing shape.</summary>
record ExportDefinitionRunResultDto(
    int RunId,
    string Status,
    int RecordCount,
    int ConfigVersion,
    string StartedAt,
    string? FinishedAt,
    string? ErrorMessage,
    bool IsTestRun
);

/// <summary>One row returned by GET .../{id}/runs.</summary>
record ExportDefinitionRunDto(
    int Id,
    int ConfigVersion,
    string StartedAt,
    string? FinishedAt,
    string Status,
    int RecordCount,
    string? ErrorMessage,
    string TriggeredBy,
    bool IsTestRun
);

/// <summary>Response for POST .../preview: capped, tree-shaped rows for on-screen inspection — never
/// written to run history (see <see cref="Connector.Api.Endpoints.ExportDefinitionEndpoints"/>).</summary>
record ExportDefinitionPreviewDto(int RecordCount, System.Text.Json.Nodes.JsonArray Records);

/// <summary>Response for the Phase 17 import-run release/reject endpoints
/// (<see cref="Connector.Api.Endpoints.ImportRunEndpoints"/>) — the post-action state of one
/// <c>ImportRunEntity</c> (Connector.Infrastructure), including the six Open Decision #11 counts and, once
/// released, the conflict count Open Decision #12's optimistic-concurrency check populated.</summary>
record ImportRunDto(
    int Id,
    int ImportDefinitionId,
    string Status,
    int RecordCount,
    int MatchedCount,
    int ChangedCount,
    int UnchangedCount,
    int RejectedCount,
    int ConflictCount,
    int InvalidCount,
    string? OperatedBy,
    string? ApprovedBy,
    string? ReleasedAt,
    string? ErrorMessage
);

/// <summary>One <see cref="Connector.Core.DynamicImport.ImportPlanOperation"/>, projected verbatim for the
/// Slice 6 review UI's field-level diff — see <see cref="ImportRunDetailDto"/>.</summary>
record ImportRunOperationDto(
    string CorrelationValue,
    string Table,
    string KeyColumn,
    string KeyValue,
    string Column,
    string? ExpectedOldValue,
    string? NewValue
);

/// <summary>Response for GET /api/import-runs/{id} (Phase 17 Slice 6): everything the review/diff view needs
/// to render a <c>PendingReview</c> run before an Approver commits — the Open Decision #11 count breakdown
/// plus the persisted <c>PlanJson</c> operations (empty once none exist, e.g. a Failed run that never
/// produced a plan). Not returned by the release/reject endpoints themselves, which stay on the smaller
/// <see cref="ImportRunDto"/> shape — this is a read, not a mutation response.</summary>
record ImportRunDetailDto(
    int Id,
    int ImportDefinitionId,
    string ImportDefinitionName,
    int ConfigVersion,
    string SourceFileName,
    string StartedAt,
    string? FinishedAt,
    string Status,
    int RecordCount,
    int MatchedCount,
    int ChangedCount,
    int UnchangedCount,
    int RejectedCount,
    int ConflictCount,
    int InvalidCount,
    string? ErrorMessage,
    string TriggeredBy,
    string? OperatedBy,
    string? ApprovedBy,
    string? ReleasedAt,
    IReadOnlyList<ImportRunOperationDto> Operations
);

/// <summary>Body for POST/PUT /api/import-definitions (Phase 17 Slice 5) — everything an operator
/// configures for one saved inbound mapping. RootNode must be a "root"-kind
/// <see cref="Connector.Core.DynamicImport.ImportNode"/>, and must have an enabled scalar-field child
/// whose TargetColumn equals RootMatchColumn (see <c>ImportNodeWalker.FindMatchField</c>).
/// AllowedWritableColumns is validated against the live ERP schema at save time (Open Decision #9) — see
/// <see cref="Connector.Api.Endpoints.ImportDefinitionEndpoints"/>. IntegrationKey/ContractVersion
/// (knowledge/pipeline/import-mapping-presets.md §3.1) are optional and only used by the not-yet-built
/// import-mapping-presets suggestion feature — set together or not at all, validated at save time by the
/// same endpoints class.</summary>
record ImportDefinitionRequest(
    string Name,
    string? Description,
    string RootTable,
    string RootMatchColumn,
    Connector.Core.DynamicImport.ImportNode RootNode,
    List<string> AllowedWritableColumns,
    string UnmatchedRootPolicy,
    bool IsEnabled,
    string? IntegrationKey = null,
    int? ContractVersion = null
);

/// <summary>Full view of a saved import definition, returned by GET/POST/PUT .../{id}.</summary>
record ImportDefinitionDto(
    int Id,
    string Name,
    string? Description,
    string RootTable,
    string RootMatchColumn,
    Connector.Core.DynamicImport.ImportNode RootNode,
    List<string> AllowedWritableColumns,
    string UnmatchedRootPolicy,
    bool IsEnabled,
    int ConfigVersion,
    string CreatedBy,
    string CreatedAt,
    string? UpdatedBy,
    string? UpdatedAt,
    string? IntegrationKey,
    int? ContractVersion
);

/// <summary>Lightweight list-view row for GET /api/import-definitions — omits RootNode/AllowedWritableColumns,
/// which aren't needed to identify or enable/disable a definition.</summary>
record ImportDefinitionSummaryDto(
    int Id,
    string Name,
    string? Description,
    string RootTable,
    string UnmatchedRootPolicy,
    bool IsEnabled,
    int ConfigVersion,
    string CreatedBy,
    string CreatedAt,
    string? UpdatedBy,
    string? UpdatedAt
);

/// <summary>Body for POST .../duplicate. Name is optional — defaults to "{original} (Copy)".</summary>
record DuplicateImportDefinitionRequest(string? Name);

/// <summary>Body for POST /api/import-definitions/suggest-from-export (Slice 4,
/// knowledge/pipeline/import-mapping-presets.md §3.4/§4) — the same sample <c>ImportEnvelope</c> JSON the
/// preview panel already accepts, pasted before any <c>ImportDefinition</c> exists yet.</summary>
record ImportMappingSuggestionRequest(string InboundJson);

/// <summary>One best-effort candidate field, projected from <c>Connector.Core.DynamicImport.ImportMappingCandidateField</c>
/// verbatim for the wire.</summary>
record ImportMappingSuggestionCandidateFieldDto(string SourceKey, string TargetColumn);

/// <summary><c>RootMatchSourceKey</c> is the matched export's own JSON key for the correlation field (its
/// <c>TargetKey</c>) — not necessarily the same string as <c>RootMatchColumn</c>, which is a column
/// name.</summary>
record ImportMappingSuggestionDto(
    int ExportDefinitionId,
    string ExportDefinitionName,
    string IntegrationKey,
    int ContractVersion,
    string RootTable,
    string RootMatchColumn,
    string RootMatchSourceKey,
    IReadOnlyList<ImportMappingSuggestionCandidateFieldDto> CandidateFields
);

/// <summary>Response for POST .../suggest-from-export. Exactly one of <c>Suggestion</c>/<c>Reason</c> is
/// set: a hit carries <c>Suggestion</c> with <c>Reason</c> null, and a miss carries a null
/// <c>Suggestion</c> with an operator-facing explanation of which of
/// <c>ImportMappingSuggestion.Evaluate</c>'s gates stopped it (no provenance in the pasted sample, no
/// export tagged with that key/version at all, one tagged but disabled, or one tagged and enabled but
/// missing <c>CorrelationKeySourceField</c>) — never a flat, unexplained "no match," and never a 4xx: this
/// is a lookup that can legitimately come up empty, not a client error.</summary>
record ImportMappingSuggestionCheckResult(ImportMappingSuggestionDto? Suggestion, string? Reason);

/// <summary>Body for POST .../preview: the raw inbound file content, since Slice 4's inbound/ folder
/// watcher doesn't exist yet — an operator pastes/uploads the vendor JSON directly to preview against a
/// saved definition. Must be a well-formed <c>ImportEnvelope</c> (schemaVersion + records) per
/// <c>ImportNodeWalker.SupportedSchemaVersion</c>.</summary>
record ImportDefinitionPreviewRequest(string InboundJson);

/// <summary>One row returned by GET /api/import-definitions/{id}/runs — the full Open Decision #11 count
/// breakdown plus the four-eyes fields, mirroring <see cref="ImportRunDto"/> with the run-history fields
/// <see cref="ExportDefinitionRunDto"/> also carries (StartedAt/FinishedAt/TriggeredBy).</summary>
record ImportDefinitionRunDto(
    int Id,
    int ConfigVersion,
    string StartedAt,
    string? FinishedAt,
    string Status,
    int RecordCount,
    int MatchedCount,
    int ChangedCount,
    int UnchangedCount,
    int RejectedCount,
    int ConflictCount,
    int InvalidCount,
    string? ErrorMessage,
    string TriggeredBy,
    string? OperatedBy,
    string? ApprovedBy,
    string? ReleasedAt
);
