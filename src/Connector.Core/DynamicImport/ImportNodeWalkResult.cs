namespace Connector.Core.DynamicImport;

/// <summary>One column-level change a walked row would make: the value currently stored in the ERP versus
/// the (already <see cref="FieldMapping"/>-coerced) value the inbound record supplies. Only emitted when the
/// two differ — an unchanged column contributes no entry, since "diff" means "what would actually change."</summary>
public sealed record ImportFieldDiff(string Column, string? OldValue, string? NewValue);

/// <summary>Outcome of matching one inbound record's correlation key against <c>RootTable</c>/
/// <c>RootMatchColumn</c>. <see cref="Rejected"/> and <see cref="Quarantined"/> both mean "excluded from the
/// accepted set" (see <see cref="UnmatchedRootPolicy"/>) — kept distinct here so a review UI can show which
/// policy fired, not just that the record didn't match.</summary>
public enum ImportRowStatus
{
    Accepted,
    Rejected,
    Quarantined,
}

/// <summary>One <see cref="ImportNodeKind.Object"/>/<see cref="ImportNodeKind.Array"/> child's resolution
/// result for one parent row: whether its <c>JoinKey</c> resolved to an existing related row, and — for
/// <see cref="ImportNodeKind.Object"/> children only, where "the related row" is unambiguous — the field-level
/// diff for that row. <see cref="ImportNodeKind.Array"/> children only get the existence check in v1: no real
/// <c>ImportDefinition</c> writes to a child table yet (import-definitions.md §1), and matching individual
/// array items to individual existing rows needs a per-item identity key the data model doesn't have —
/// deferred to whichever v2+ change actually needs it, rather than guessed at here.</summary>
public sealed record ImportChildResult(
    string SourceKey,
    string RelatedTable,
    bool Matched,
    string? RejectReason,
    IReadOnlyList<ImportFieldDiff> Fields,
    IReadOnlyList<ImportChildResult> Children
);

/// <summary>One inbound record's full walk result: whether its root row was matched, the field-level diff if
/// so, and the resolution of every object/array child under it. <see cref="CorrelationValue"/> is kept even for
/// a rejected/quarantined row (null only when the record had no readable correlation value at all) so a review
/// UI can show which vendor record failed to match, not just a bare count.</summary>
public sealed record ImportRowResult(
    string? CorrelationValue,
    ImportRowStatus Status,
    string? RejectReason,
    IReadOnlyList<ImportFieldDiff> Fields,
    IReadOnlyList<ImportChildResult> Children
);

/// <summary>Full result of walking one inbound JSON file against one <see cref="ImportNode"/> tree — the
/// <c>DiffJson</c> shape (import-definitions.md §4): persisted verbatim by Slice 3 so the eventual commit step
/// and this preview can never disagree about what a row means.</summary>
public sealed record ImportWalkResult(
    int RecordCount,
    int AcceptedCount,
    int RejectedCount,
    IReadOnlyList<ImportRowResult> Rows
);

/// <summary>Thrown for a structural problem with the saved <c>ImportDefinition</c> itself (a <c>TargetColumn</c>
/// outside <c>AllowedWritableColumns</c>, a missing root match field, malformed inbound JSON) — never for an
/// individual record's data, which is reported via <see cref="ImportRowResult"/> instead. Distinguishing the
/// two matters to the caller (Slice 5's API): a thrown exception means "this run cannot proceed at all," while
/// a rejected/quarantined row is a normal, expected part of a successful walk.</summary>
public sealed class ImportValidationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
