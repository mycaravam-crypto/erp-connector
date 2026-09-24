namespace Connector.Core.DataSources;

/// <summary>
/// Database-neutral description of one read query: a root table, the columns to project,
/// equi-joins to further tables, and AND-combined filter conditions. Carries no SQL at all — every table and
/// column is a plain name that <see cref="SourceQueryValidator"/> checks against a known
/// <see cref="SourceSchema"/>, and every filter value stays a value (never text spliced into a query), so a
/// provider's compiler can always bind it as a parameter. Only providers know a dialect; the PostgreSQL one is
/// <c>Connector.Infrastructure.DataSources.PostgreSql.PostgreSqlQueryCompiler</c>. See knowledge/architecture/source-query-model.md.
/// </summary>
public sealed record SourceQuery
{
    /// <summary>The table every <see cref="QueryColumn.Table"/>/<see cref="QueryCondition.Table"/>/
    /// <see cref="QueryJoin.ParentTable"/> left null refers to.</summary>
    public required string RootTable { get; init; }

    /// <summary>The projection, in output order. Empty means every column of <see cref="RootTable"/>, in
    /// schema order.</summary>
    public IReadOnlyList<QueryColumn> Columns { get; init; } = [];

    /// <summary>Applied in list order — a join's <see cref="QueryJoin.ParentTable"/> must be the root table or
    /// a table joined earlier in this list. Each table may appear at most once in the whole query.</summary>
    public IReadOnlyList<QueryJoin> Joins { get; init; } = [];

    /// <summary>Combined with AND. Empty means no filter.</summary>
    public IReadOnlyList<QueryCondition> Conditions { get; init; } = [];

    /// <summary>Maximum number of rows to return; null means unlimited. Must not be negative.</summary>
    public int? Limit { get; init; }
}

/// <summary>One projected column of a <see cref="SourceQuery"/>.</summary>
public sealed record QueryColumn
{
    /// <summary>Null means <see cref="SourceQuery.RootTable"/>; otherwise a table named by one of the query's
    /// <see cref="SourceQuery.Joins"/>.</summary>
    public string? Table { get; init; }

    public required string Column { get; init; }

    /// <summary>The name this column carries in <see cref="QueryResult.Columns"/>; defaults to
    /// <see cref="Column"/>. Output names must be unique within one query.</summary>
    public string? Alias { get; init; }

    /// <summary>The name this column carries in <see cref="QueryResult.Columns"/>.</summary>
    public string OutputName => Alias ?? Column;
}

/// <summary>How a <see cref="QueryJoin"/> treats a parent row with no matching row in the joined table.</summary>
public enum QueryJoinType
{
    /// <summary>Drops the parent row.</summary>
    Inner = 0,

    /// <summary>Keeps the parent row; the joined table's columns come back null.</summary>
    Left = 1,
}

/// <summary>
/// An equi-join <c>Table.Column = ParentTable.ParentColumn</c>. Deliberately no free-form ON expression —
/// the only thing a caller chooses is which known columns to match.
/// </summary>
public sealed record QueryJoin
{
    /// <summary>The table being joined in.</summary>
    public required string Table { get; init; }

    /// <summary>The column of <see cref="Table"/> matched against <see cref="ParentColumn"/>.</summary>
    public required string Column { get; init; }

    /// <summary>Null means <see cref="SourceQuery.RootTable"/>; otherwise a table joined earlier.</summary>
    public string? ParentTable { get; init; }

    public required string ParentColumn { get; init; }

    public QueryJoinType Type { get; init; } = QueryJoinType.Inner;
}

/// <summary>
/// One filter predicate of a <see cref="SourceQuery"/>. The operand is always a value, never an expression:
/// <see cref="Value"/> for the binary operators, <see cref="Values"/> for <see cref="QueryOperator.In"/>, and
/// neither for <see cref="QueryOperator.IsNull"/>/<see cref="QueryOperator.IsNotNull"/>.
/// </summary>
public sealed record QueryCondition
{
    /// <summary>Null means <see cref="SourceQuery.RootTable"/>; otherwise a joined table.</summary>
    public string? Table { get; init; }

    public required string Column { get; init; }

    public required QueryOperator Operator { get; init; }

    /// <summary>A scalar (string, number, bool, <see cref="Guid"/>, date/time). Must be non-null for every
    /// operator except <see cref="QueryOperator.IsNull"/>/<see cref="QueryOperator.IsNotNull"/>/
    /// <see cref="QueryOperator.In"/> (where it must be null), and a string for
    /// <see cref="QueryOperator.Contains"/>/<see cref="QueryOperator.StartsWith"/>/
    /// <see cref="QueryOperator.EndsWith"/> (matched literally and case-sensitively — no wildcard syntax).
    /// Use <see cref="QueryOperator.IsNull"/> rather than <see cref="QueryOperator.Equal"/> with null.</summary>
    public object? Value { get; init; }

    /// <summary><see cref="QueryOperator.In"/> only: the non-empty, non-null candidate values.</summary>
    public IReadOnlyList<object> Values { get; init; } = [];
}

/// <summary>The comparison a <see cref="QueryCondition"/> applies.</summary>
public enum QueryOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    Contains,
    StartsWith,
    EndsWith,
    IsNull,
    IsNotNull,
    In,
}
