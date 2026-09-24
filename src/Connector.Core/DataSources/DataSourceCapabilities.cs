namespace Connector.Core.DataSources;

/// <summary>
/// What a provider can and can't do, stated explicitly — so callers and the shared provider
/// contract tests branch on a named capability, never on <c>provider.Type == …</c>. Only differences that exist
/// between the implemented providers are modeled; everything else in <see cref="IDataSourceProvider"/> (connection
/// test, schema with relations, projection, filters, null checks, limit, joins, cancellation, sanitized errors)
/// is required of every provider.
/// </summary>
public sealed record DataSourceCapabilities
{
    /// <summary><see cref="IDataSourceProvider.ExecuteNativeAsync"/> runs SQL, so the SQL-rendering export builders
    /// (legacy mappings, <c>ExportNode</c> trees) work against this source.</summary>
    public required bool NativeSql { get; init; }

    /// <summary><see cref="QueryOperator.Contains"/>/<see cref="QueryOperator.StartsWith"/>/
    /// <see cref="QueryOperator.EndsWith"/> match case-sensitively, as the model asks; otherwise they follow the
    /// source's case-insensitive matching.</summary>
    public required bool CaseSensitiveTextMatch { get; init; }

    /// <summary>An empty string and NULL are different values; otherwise the source has no NULL and an empty
    /// field is reported as <c>null</c>.</summary>
    public required bool DistinguishesEmptyFromNull { get; init; }

    /// <summary>A <see cref="SourceQuery"/> may put conditions on a left-joined table; otherwise that is rejected
    /// with <see cref="InvalidSourceQueryException"/>.</summary>
    public required bool ConditionsOnLeftJoinedTables { get; init; }

    /// <summary>The four-eyes import path (<c>ImportNodeWalker</c> reads, <c>ImportRunReleaser</c>'s transactional
    /// conditional updates) runs against this source — through the provider's ADO.NET connection and SQL dialect.
    /// Implies <see cref="NativeSql"/>.</summary>
    public required bool Imports { get; init; }

    /// <summary>A relational database: everything the model defines.</summary>
    public static readonly DataSourceCapabilities Sql = new()
    {
        NativeSql = true,
        CaseSensitiveTextMatch = true,
        DistinguishesEmptyFromNull = true,
        ConditionsOnLeftJoinedTables = true,
        Imports = true,
    };
}
