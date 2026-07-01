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

/// <summary>Body for PATCH /api/schema/columns. Columns not in ExportSchema.Columns are silently ignored.</summary>
record ColumnPatchRequest(string[] Columns);

/// <summary>Body for PATCH /api/schema/mappings. Keys not in ExportSchema.Columns are silently ignored. Empty/whitespace values remove the override.</summary>
record MappingPatchRequest(Dictionary<string, string> Mappings);

record LoginRequest(string Username, string Password);

record LoginResponse(string Token, string Username);

record HashRequest(string Password);

record ErpRecordsResult(IReadOnlyList<ErpCiRecord> Records, int Total);

record ErpCiRecord(
    string Id,
    string? Serial,
    string? Status,
    string? CommissionDate,
    string? ArticleName,
    string? PartNumber,
    string? Manufacturer,
    string? MaintenancePlanStatus,
    string? AllocationChartRef,
    string? ParentId,
    string? ParentSerial,
    bool InScope,
    string? ExclusionReason,
    string? TechnicianName,
    string? StorageLocation
);

record SchemaColumnDto(
    string Name,
    string ErpSource,
    string Type,
    string Notes,
    bool Active,
    string? ExportName
);

record SchemaDto(string Version, SchemaColumnDto[] Columns);

record SourceColumnDto(string Name, string Type, bool Nullable, bool PrimaryKey);

record SourceTableDto(string Name, string Description, SourceColumnDto[] Columns);

record SourceSchemaDto(string ConnectionLabel, SourceTableDto[] Tables);

record RunNowResult(int SequenceNo, int RecordCount, string Sha256Short);

record PreviewResult(
    int RecordCount,
    string SchemaVersion,
    IReadOnlyList<string> Columns,
    IList<Dictionary<string, string>> Records,
    string Source = "demo",
    string? SourceTable = null,
    string? Error = null
);

/// <summary>Public view of the stored connection — no password field.</summary>
record ErpConnectionInfo(string Host, int Port, string Database, string Username);

/// <summary>Body for POST /api/exports/{seqNo}/skip. Reason is stored in the audit log.</summary>
record SkipRequest(string? Reason);

/// <summary>Body for PATCH /api/gdpr-denied-fields.</summary>
record GdprDenylistRequest(List<string> Fields);

/// <summary>Single row returned by GET /api/audit.</summary>
record AuditEntryDto(int Id, string Timestamp, string Username, string Action, string? Detail);
