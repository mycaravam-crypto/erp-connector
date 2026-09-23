using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Connector.Infrastructure.DataSources.PostgreSql;

/// <summary>
/// Turns a PostgreSQL value in its text output format into the JSON value PostgreSQL's own <c>to_json</c> would
/// produce for it — so the export tree engine can assemble JSON in C# that is identical to what the database
/// used to build with <c>json_build_object</c>. Follows <c>to_json</c>'s type categories:
/// <list type="bullet">
/// <item><c>smallint</c>/<c>integer</c>/<c>bigint</c>/<c>real</c>/<c>double precision</c>/<c>numeric</c> →
/// a JSON number with the exact same digits (<c>NaN</c>/<c>Infinity</c> stay strings, as in Postgres);</item>
/// <item><c>boolean</c> → <c>true</c>/<c>false</c>; <c>json</c>/<c>jsonb</c> → embedded as JSON;</item>
/// <item><c>date</c> → unchanged; <c>timestamp</c>/<c>timestamptz</c> → ISO 8601 with a <c>T</c> separator
/// and a <c>±HH:MM</c> offset;</item>
/// <item>arrays → JSON arrays, element by element (nested arrays nest);</item>
/// <item>everything else (text, uuid, time, interval, enums, …) → a JSON string of the text as-is.</item>
/// </list>
/// Assumes the server's default <c>DateStyle</c> (ISO), which is what makes date/timestamp text ISO-shaped.
/// Composite (row) types and types with a custom cast to json aren't special-cased — they come out as
/// strings; no exported column in this codebase uses either.
/// </summary>
internal static partial class PostgreSqlJsonValues
{
    public static JsonNode? FromNativeText(string text, string dataType)
    {
        var type = NormalizeType(dataType);
        if (type.EndsWith("[]", StringComparison.Ordinal))
            return ParseArray(text, type[..^2]);

        switch (type)
        {
            case "smallint" or "integer" or "bigint" or "real" or "double precision" or "numeric":
                return JsonNumberRegex().IsMatch(text) ? JsonNode.Parse(text) : JsonValue.Create(text);
            case "boolean":
                return JsonValue.Create(text == "t");
            case "json" or "jsonb":
                return JsonNode.Parse(text);
            case "timestamp without time zone":
                return JsonValue.Create(IsInfinity(text) ? text : text.Replace(' ', 'T'));
            case "timestamp with time zone":
                return JsonValue.Create(IsInfinity(text) ? text : WithFullOffset(text.Replace(' ', 'T')));
            default:
                return JsonValue.Create(text);
        }
    }

    // Npgsql reports type modifiers inline — "numeric(10, 2)", "timestamp(3) without time zone",
    // "character varying(200)[]" — which don't change how a value is encoded.
    private static string NormalizeType(string dataType) => TypeModifierRegex().Replace(dataType, "");

    private static bool IsInfinity(string text) => text is "infinity" or "-infinity";

    // Postgres's text format abbreviates a whole-hour offset ("+00"); its JSON encoding always writes "+00:00".
    private static string WithFullOffset(string timestamp) =>
        HourOnlyOffsetRegex().IsMatch(timestamp) ? timestamp + ":00" : timestamp;

    // Array text format: {a,b,"quoted, with \"escapes\"",NULL,{nested}}, optionally prefixed by explicit
    // bounds ("[0:1]={...}") when the lower bound isn't 1.
    private static JsonArray ParseArray(string text, string elementType)
    {
        var start = text.IndexOf('{');
        var pos = start;
        return ParseArrayLevel(text, ref pos, elementType);
    }

    private static JsonArray ParseArrayLevel(string text, ref int pos, string elementType)
    {
        var result = new JsonArray();
        pos++; // '{'
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '}')
            {
                pos++;
                return result;
            }
            if (c == ',')
            {
                pos++;
                continue;
            }
            if (c == '{')
            {
                result.Add(ParseArrayLevel(text, ref pos, elementType));
                continue;
            }

            if (c == '"')
            {
                var value = new StringBuilder();
                pos++;
                while (text[pos] != '"')
                {
                    if (text[pos] == '\\')
                        pos++;
                    value.Append(text[pos++]);
                }
                pos++; // closing quote
                result.Add(FromNativeText(value.ToString(), elementType));
                continue;
            }

            var end = pos;
            while (text[end] != ',' && text[end] != '}')
                end++;
            var raw = text[pos..end];
            pos = end;
            result.Add(
                raw.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : FromNativeText(raw, elementType)
            );
        }
        return result;
    }

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$")]
    private static partial Regex JsonNumberRegex();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex TypeModifierRegex();

    [GeneratedRegex(@"[+-][0-9]{2}$")]
    private static partial Regex HourOnlyOffsetRegex();
}
