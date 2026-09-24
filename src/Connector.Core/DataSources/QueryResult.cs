namespace Connector.Core.DataSources;

/// <summary>
/// Generic, provider-agnostic row set returned by <see cref="IDataSourceProvider.ExecuteAsync"/>/
/// <see cref="IDataSourceProvider.ExecuteNativeAsync"/>. Every value is already stringified by the provider
/// (e.g. a Postgres date/timestamp column is coerced to ISO-8601) so callers never need provider-specific type
/// information; a null value means the source column was NULL. Each row's
/// <see cref="QueryResultRow.Values"/> lines up index-for-index with <see cref="Columns"/>.
/// </summary>
public sealed record QueryResult
{
    public required IReadOnlyList<QueryResultColumn> Columns { get; init; }

    public required IReadOnlyList<QueryResultRow> Rows { get; init; }

    /// <summary>Each row keyed by column name. If two columns share a name the later one wins.</summary>
    public IEnumerable<Dictionary<string, string?>> ToDictionaries()
    {
        foreach (var row in Rows)
        {
            var dict = new Dictionary<string, string?>(Columns.Count);
            for (var i = 0; i < Columns.Count; i++)
                dict[Columns[i].Name] = row.Values[i];
            yield return dict;
        }
    }
}

/// <summary>One column of a <see cref="QueryResult"/>.</summary>
public sealed record QueryResultColumn
{
    public required string Name { get; init; }

    /// <summary>The backend's own name for the column's type (for PostgreSQL e.g. <c>integer</c>,
    /// <c>numeric(10, 2)</c>, <c>text[]</c>), or null if the provider doesn't report one. Opaque outside the
    /// provider's own layer.</summary>
    public string? DataType { get; init; }
}

/// <summary>One row of a <see cref="QueryResult"/>.</summary>
public sealed record QueryResultRow
{
    public required IReadOnlyList<string?> Values { get; init; }
}
