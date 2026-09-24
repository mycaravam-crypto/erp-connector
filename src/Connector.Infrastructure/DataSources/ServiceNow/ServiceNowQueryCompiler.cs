using System.Globalization;
using System.Text.RegularExpressions;
using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources.ServiceNow;

/// <summary>One Table API read: the table, its <c>sysparm_query</c> (ServiceNow encoded query) and the
/// <c>sysparm_fields</c> projection.</summary>
public sealed record ServiceNowTableRequest(string Table, string EncodedQuery, IReadOnlyList<string> Fields);

/// <summary>A <see cref="QueryJoin"/> resolved in C#: <see cref="Request"/> reads the joined table (filtered by its
/// own conditions, if any), and its rows are matched on <c>Join.Column = ParentTable.ParentColumn</c>.</summary>
public sealed record ServiceNowJoinStep(QueryJoin Join, string ParentTable, ServiceNowTableRequest Request);

/// <summary>An output column of the result: which table's field, under which name.</summary>
public sealed record ServiceNowOutputColumn(string Table, string Column, string Name);

/// <summary>A compiled <see cref="SourceQuery"/>: a root read, joins resolved in C#, the output columns, and the
/// limit (applied to the root read directly when no inner join can drop root rows).</summary>
public sealed record ServiceNowQueryPlan(
    ServiceNowTableRequest Root,
    IReadOnlyList<ServiceNowJoinStep> Joins,
    IReadOnlyList<ServiceNowOutputColumn> Columns,
    int? Limit
)
{
    public int? RootLimit => Joins.Any(j => j.Join.Type == QueryJoinType.Inner) ? null : Limit;
}

/// <summary>
/// Compiles the database-neutral <see cref="SourceQuery"/> into Table API reads. ServiceNow's
/// encoded-query syntax exists only here — never in <c>Connector.Core</c> or stored configuration. Validates
/// against the schema first (<see cref="SourceQueryValidator"/>), so every field name is one ServiceNow reported;
/// every value is checked for the encoded-query separators (<c>^</c>, and <c>,</c> inside <c>IN</c>) and rejected
/// rather than escaped, so a value can never add a condition of its own. Conditions are sent server-side as the
/// filter of the table they belong to; the Table API can't join, so joins become a second read keyed by the parent
/// rows' values (see <see cref="ServiceNowTableApiProvider"/>).
/// </summary>
public static partial class ServiceNowQueryCompiler
{
    public static ServiceNowQueryPlan Compile(SourceQuery query, SourceSchema schema)
    {
        SourceQueryValidator.Validate(query, schema);

        var columns = (
            query.Columns.Count > 0
                ? query.Columns
                : schema
                    .Tables.First(t => t.Name == query.RootTable)
                    .Columns.Select(c => new QueryColumn { Column = c.Name })
                    .ToList()
        )
            .Select(c => new ServiceNowOutputColumn(c.Table ?? query.RootTable, c.Column, c.OutputName))
            .ToList();

        // Every table reads exactly the fields the result or a join needs.
        var fields = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        void Need(string table, string column)
        {
            if (!fields.TryGetValue(table, out var set))
                fields[table] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(column);
        }
        foreach (var c in columns)
            Need(c.Table, c.Column);
        foreach (var j in query.Joins)
        {
            Need(j.Table, j.Column);
            Need(j.ParentTable ?? query.RootTable, j.ParentColumn);
        }

        var conditionsByTable = query.Conditions.ToLookup(c => c.Table ?? query.RootTable, StringComparer.Ordinal);
        string Filter(string table) =>
            string.Join("^", conditionsByTable[table].Select(CompileCondition).Append("ORDERBYsys_id"));
        ServiceNowTableRequest Request(string table) => new(table, Filter(table), fields[table].ToList());

        var joins = query
            .Joins.Select(j =>
                j.Type == QueryJoinType.Left && conditionsByTable[j.Table].Any()
                    ? throw new InvalidSourceQueryException(
                        $"ServiceNow: conditions on the left-joined table '{j.Table}' are not supported."
                    )
                    : new ServiceNowJoinStep(j, j.ParentTable ?? query.RootTable, Request(j.Table))
            )
            .ToList();

        return new ServiceNowQueryPlan(Request(query.RootTable), joins, columns, query.Limit);
    }

    /// <summary><c>fieldINa,b,c</c> — the batched key match for joins and metadata lookups.</summary>
    public static string In(string field, IEnumerable<string> values) =>
        $"{field}IN{string.Join(",", values.Select(v => SafeValue(v, forList: true)))}";

    /// <summary>True for a plain ServiceNow table/field name (letters, digits, underscore).</summary>
    public static bool IsIdentifier(string name) => IdentifierRegex().IsMatch(name);

    private static string CompileCondition(QueryCondition c)
    {
        string V() => SafeValue(Format(c.Value!), forList: false);
        return c.Operator switch
        {
            QueryOperator.Equal => $"{c.Column}={V()}",
            QueryOperator.NotEqual => $"{c.Column}!={V()}",
            QueryOperator.GreaterThan => $"{c.Column}>{V()}",
            QueryOperator.GreaterThanOrEqual => $"{c.Column}>={V()}",
            QueryOperator.LessThan => $"{c.Column}<{V()}",
            QueryOperator.LessThanOrEqual => $"{c.Column}<={V()}",
            QueryOperator.Contains => $"{c.Column}LIKE{V()}",
            QueryOperator.StartsWith => $"{c.Column}STARTSWITH{V()}",
            QueryOperator.EndsWith => $"{c.Column}ENDSWITH{V()}",
            QueryOperator.IsNull => $"{c.Column}ISEMPTY",
            QueryOperator.IsNotNull => $"{c.Column}ISNOTEMPTY",
            QueryOperator.In => In(c.Column, c.Values.Select(Format)),
            _ => throw new InvalidSourceQueryException($"Unsupported operator '{c.Operator}'."),
        };
    }

    // ServiceNow stores dates as "yyyy-MM-dd" / "yyyy-MM-dd HH:mm:ss" (UTC) and sys_ids as 32 hex digits.
    private static string Format(object value) =>
        value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            Guid g => g.ToString("N"),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };

    private static string SafeValue(string value, bool forList)
    {
        if (value.Contains('^') || value.Contains('\n') || value.Contains('\r'))
            throw new InvalidSourceQueryException("ServiceNow filter values cannot contain '^' or line breaks.");
        if (forList && value.Contains(','))
            throw new InvalidSourceQueryException("ServiceNow IN values cannot contain ','.");
        return value;
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex IdentifierRegex();
}
