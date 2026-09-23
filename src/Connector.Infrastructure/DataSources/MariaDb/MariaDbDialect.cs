using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Connector.Infrastructure.DataSources.MariaDb;

/// <summary>
/// <see cref="ISqlDialect"/> for MariaDB (Arbeitsauftrag 7) — every MariaDB-specific fragment the generic query
/// builders emit: backtick identifier quoting, <c>@pN</c> parameter placeholders (MySqlConnector's named-parameter
/// syntax), <c>LIMIT n</c>, <c>CAST(… AS CHAR)</c>, <c>&lt;=&gt;</c>, <c>GROUP_CONCAT</c>, and how a value in
/// <see cref="MariaDbDataSourceProvider"/>'s native text format maps to JSON. Stateless; use <see cref="Instance"/>.
/// </summary>
public sealed partial class MariaDbDialect : ISqlDialect
{
    public static readonly MariaDbDialect Instance = new();

    private MariaDbDialect() { }

    // Wraps in backticks and doubles any embedded backtick — works regardless of the server's ANSI_QUOTES mode.
    public string QuoteIdentifier(string identifier) => "`" + identifier.Replace("`", "``") + "`";

    public string BuildParameterName(int index) => "@p" + index.ToString(CultureInfo.InvariantCulture);

    public string BuildLimit(int limit) => "LIMIT " + limit.ToString(CultureInfo.InvariantCulture);

    // MariaDB treats a backslash inside '...' as an escape character (unless NO_BACKSLASH_ESCAPES is set, where
    // doubling it would add a second one — so this is only ever used for constant delimiters, never for data).
    public string QuoteStringLiteral(string value) => "'" + value.Replace(@"\", @"\\").Replace("'", "''") + "'";

    public string CastToText(string expression) => $"CAST({expression} AS CHAR)";

    public string BuildNullSafeEquals(string left, string right) => $"{left} <=> {right}";

    // No array parameters in MariaDB: one parameter per value. MySqlConnector substitutes them client-side, so a
    // full 10 000-key batch stays well within the protocol's limits.
    public string BuildMatchesAny(
        string expression,
        IReadOnlyList<string> values,
        IDictionary<string, object?> parameters
    )
    {
        var names = new List<string>(values.Count);
        foreach (var value in values)
        {
            var name = BuildParameterName(parameters.Count);
            parameters[name] = value;
            names.Add(name);
        }
        return $"{expression} IN ({string.Join(", ", names)})";
    }

    /// <summary>
    /// Maps a value in <see cref="MariaDbDataSourceProvider"/>'s native text format (see its
    /// <c>FormatNative</c>) to the JSON value PostgreSQL's <c>to_json</c> gives the equivalent column, so a
    /// nested export is the same document against either backend: numeric types → JSON numbers with the same
    /// digits, <c>BOOL</c> (<c>TINYINT(1)</c>) → <c>true</c>/<c>false</c>, <c>JSON</c> → embedded,
    /// <c>DATETIME</c>/<c>TIMESTAMP</c> → ISO 8601 with a <c>T</c>, everything else → a string.
    /// </summary>
    public JsonNode? ConvertNativeTextToJson(string nativeText, string dataType)
    {
        var type = dataType.ToUpperInvariant().Replace(" UNSIGNED", "", StringComparison.Ordinal);
        return type switch
        {
            "TINYINT" or "SMALLINT" or "MEDIUMINT" or "INT" or "BIGINT" or "DECIMAL" or "FLOAT" or "DOUBLE" or "YEAR" =>
                JsonNumberRegex().IsMatch(nativeText) ? JsonNode.Parse(nativeText) : JsonValue.Create(nativeText),
            "BOOL" => JsonValue.Create(nativeText != "0"),
            "JSON" => JsonNode.Parse(nativeText),
            "DATETIME" or "TIMESTAMP" => JsonValue.Create(nativeText.Replace(' ', 'T')),
            _ => JsonValue.Create(nativeText),
        };
    }

    public string FormatValue(object value, string dataType) =>
        value is DateTime dt && dataType is "DATE" or "DATETIME" or "TIMESTAMP"
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.ToString() ?? "";

    // GROUP_CONCAT truncates at group_concat_max_len (1 MiB by default on MariaDB) — far beyond a flat export
    // cell, and the same order of magnitude as a spreadsheet cell's own limit.
    public string BuildStringAggregate(string expression, string delimiter) =>
        $"GROUP_CONCAT({CastToText(expression)} SEPARATOR {QuoteStringLiteral(delimiter)})";

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$")]
    private static partial Regex JsonNumberRegex();
}
