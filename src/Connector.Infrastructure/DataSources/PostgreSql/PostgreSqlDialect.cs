using System.Globalization;
using System.Text.Json.Nodes;

namespace Connector.Infrastructure.DataSources.PostgreSql;

/// <summary>
/// <see cref="ISqlDialect"/> for PostgreSQL — the single home of every PostgreSQL-specific fragment the
/// generic query builders emit: double-quote identifier quoting, <c>@pN</c> parameter placeholders (Npgsql's
/// named-parameter syntax), <c>LIMIT n</c>, <c>::text</c> casts, <c>= ANY(@array)</c>,
/// <c>IS NOT DISTINCT FROM</c>, <c>string_agg</c>, and how a PostgreSQL text value maps to JSON
/// (<see cref="PostgreSqlJsonValues"/>). Stateless; use <see cref="Instance"/>.
/// </summary>
public sealed class PostgreSqlDialect : ISqlDialect
{
    public static readonly PostgreSqlDialect Instance = new();

    private PostgreSqlDialect() { }

    // Wraps in double quotes and doubles any embedded double quote, so no identifier can end the quoted
    // name early and inject syntax.
    public string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public string BuildParameterName(int index) => "@p" + index.ToString(CultureInfo.InvariantCulture);

    public string BuildLimit(int limit) => "LIMIT " + limit.ToString(CultureInfo.InvariantCulture);

    // Standard single-quote doubling — the same escaping the export builders always used (formerly
    // DynamicExportService.SqlLit). Relies on Postgres's default standard_conforming_strings = on, under which
    // a backslash inside '...' is an ordinary character.
    public string QuoteStringLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    public string CastToText(string expression) => expression + "::text";

    public string BuildNullSafeEquals(string left, string right) => $"{left} IS NOT DISTINCT FROM {right}";

    // One text[] parameter for the whole batch, however many keys it holds.
    public string BuildMatchesAny(
        string expression,
        IReadOnlyList<string> values,
        IDictionary<string, object?> parameters
    )
    {
        var name = BuildParameterName(parameters.Count);
        parameters[name] = values.ToArray();
        return $"{expression} = ANY({name})";
    }

    public JsonNode? ConvertNativeTextToJson(string nativeText, string dataType) =>
        PostgreSqlJsonValues.FromNativeText(nativeText, dataType);

    public string FormatValue(object value, string dataType) =>
        (value, dataType) switch
        {
            (DateTime dt, "date" or "timestamp" or "timestamptz") => dt.ToString("yyyy-MM-dd"),
            (DateOnly d, _) => d.ToString("yyyy-MM-dd"),
            _ => value.ToString() ?? "",
        };

    public string BuildStringAggregate(string expression, string delimiter) =>
        $"string_agg({CastToText(expression)}, {QuoteStringLiteral(delimiter)})";
}
