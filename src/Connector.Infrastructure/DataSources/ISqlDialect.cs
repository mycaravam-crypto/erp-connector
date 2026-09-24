using System.Text.Json.Nodes;

namespace Connector.Infrastructure.DataSources;

/// <summary>
/// The SQL syntax that differs between relational backends, as far as this codebase actually uses it — not a
/// general-purpose SQL abstraction (no SQL framework, no AST, no ORM). Generic query
/// builders (<see cref="DynamicExportService"/>, <see cref="ImportNodeWalker"/>, <see cref="ImportRunReleaser"/>)
/// assemble statements from plain ANSI keywords (<c>SELECT</c>/<c>FROM</c>/<c>WHERE</c>/<c>AND</c>/<c>AS</c>)
/// plus these members; everything else is a dialect's job. Implementations:
/// <see cref="PostgreSql.PostgreSqlDialect"/> and <see cref="MariaDb.MariaDbDialect"/>. See
/// knowledge/architecture/sql-dialect.md.
/// </summary>
/// <remarks>
/// Members past the first three exist because a concrete builder needs them today, not speculatively: string
/// literals and a string aggregate for the legacy flat export, text casts and a batched key match for the export
/// tree engine's per-level child queries, rendering a native text value as JSON for that engine's C#-side tree
/// assembly, and a null-safe comparison for the import releaser's optimistic-concurrency guard.
/// Every member returns a SQL fragment; callers must only pass it identifiers from a known schema/config,
/// expressions built by this dialect, or values they escape through <see cref="QuoteStringLiteral"/>.
/// </remarks>
public interface ISqlDialect
{
    /// <summary>Quotes a table/column/alias name so it is always read as an identifier, never as syntax.</summary>
    string QuoteIdentifier(string identifier);

    /// <summary>The placeholder for the <paramref name="index"/>-th bound parameter, as it appears in SQL
    /// text. Also a valid parameter name for the matching driver's parameter collection.</summary>
    string BuildParameterName(int index);

    /// <summary>The clause appended to a SELECT to cap its row count at <paramref name="limit"/>.</summary>
    string BuildLimit(int limit);

    /// <summary>A string literal holding <paramref name="value"/> verbatim — for constant text that has to be
    /// part of the statement itself (a JSON object key, an aggregation delimiter) rather than a parameter.</summary>
    string QuoteStringLiteral(string value);

    /// <summary>Converts <paramref name="expression"/> to text, so it can be compared with a text value or
    /// emitted as text regardless of the column's own type.</summary>
    string CastToText(string expression);

    /// <summary>A null-safe equality test: true when both sides are equal <i>or</i> both are null.</summary>
    string BuildNullSafeEquals(string left, string right);

    /// <summary>True when <paramref name="expression"/> equals any of <paramref name="values"/> — how the export
    /// tree engine fetches every child row of a whole level in one query instead of one query per parent. Binds
    /// the values into <paramref name="parameters"/> (named from <see cref="BuildParameterName"/>, continuing at
    /// its current count) in whatever shape the backend needs: one array parameter, or one parameter per value.
    /// </summary>
    string BuildMatchesAny(string expression, IReadOnlyList<string> values, IDictionary<string, object?> parameters);

    /// <summary>
    /// Renders <paramref name="nativeText"/> — a value in the backend's own text format, as returned for a
    /// <see cref="Connector.Core.DataSources.NativeSqlQuery.ReturnNativeText"/> query — as the JSON value the
    /// backend's own JSON encoding would give a column of type <paramref name="dataType"/>
    /// (<see cref="Connector.Core.DataSources.QueryResultColumn.DataType"/>): numbers as JSON numbers, booleans
    /// as JSON booleans, JSON columns embedded as JSON, arrays as JSON arrays, anything else as a string.
    /// </summary>
    JsonNode? ConvertNativeTextToJson(string nativeText, string dataType);

    /// <summary>The connector's string form of a non-null column <paramref name="value"/> as the driver returned
    /// it, for a column the backend calls <paramref name="dataType"/>: date/timestamp columns as <c>yyyy-MM-dd</c>,
    /// everything else the value's own <c>ToString()</c>. Shared by the provider's row materialization and the
    /// import walker, so old values read for a diff match exported values exactly.</summary>
    string FormatValue(object value, string dataType);

    /// <summary>Aggregates <paramref name="expression"/> (as text) over the rows of the enclosing SELECT into
    /// one string joined by <paramref name="delimiter"/>; null values are skipped.</summary>
    string BuildStringAggregate(string expression, string delimiter);
}

/// <summary>
/// An <see cref="Connector.Core.DataSources.IDataSourceProvider"/> whose backend speaks SQL, and so can run
/// the SQL a generic builder assembles with its <see cref="Dialect"/>. Lives here rather than in
/// <c>Connector.Core</c> because SQL dialects are an Infrastructure concern — a non-SQL provider (e.g.
/// ServiceNow's Table API) simply doesn't implement it.
/// </summary>
public interface ISqlDataSourceProvider : Connector.Core.DataSources.IDataSourceProvider
{
    ISqlDialect Dialect { get; }

    /// <summary>An open ADO.NET connection for callers that need more than one query per call — the import path's
    /// multi-statement transaction. The caller disposes it.</summary>
    Task<System.Data.Common.DbConnection> OpenConnectionAsync(
        Connector.Core.DataSources.DataSourceConfig config,
        CancellationToken cancellationToken
    );
}
