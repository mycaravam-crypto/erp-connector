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

/// <summary>Operator is taken from the JWT; only the approver name is supplied in the body.</summary>
record ReleaseRequest(string Approver);

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
    string? ForeignKeyColumn = null
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
