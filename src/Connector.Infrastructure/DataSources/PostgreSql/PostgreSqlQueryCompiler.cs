using System.Text;
using Connector.Core.DataSources;
using Npgsql;
using NpgsqlTypes;

namespace Connector.Infrastructure.DataSources.PostgreSql;

/// <summary>A <see cref="SourceQuery"/> compiled to PostgreSQL: SQL text plus the positional-named
/// (<c>@p0</c>, <c>@p1</c>, …) parameters it references.</summary>
public sealed record CompiledPostgreSqlQuery(string Sql, IReadOnlyList<NpgsqlParameter> Parameters);

/// <summary>
/// Compiles the database-neutral <see cref="SourceQuery"/> into PostgreSQL — the only place
/// that model meets a SQL dialect. Validates against the given <see cref="SourceSchema"/> first
/// (<see cref="SourceQueryValidator"/>), so every identifier emitted is one the schema reported, double-quoted
/// by <see cref="PostgreSqlDialect"/>, and table aliases are synthetic (<c>t0</c>, <c>t1</c>, …). No
/// caller-supplied text other than those identifiers and output aliases ever becomes SQL: every filter value is
/// a bound parameter, and <see cref="SourceQuery.Limit"/> is an <see cref="int"/> rendered by the dialect.
/// </summary>
public static class PostgreSqlQueryCompiler
{
    private static readonly PostgreSqlDialect Dialect = PostgreSqlDialect.Instance;

    public static CompiledPostgreSqlQuery Compile(SourceQuery query, SourceSchema schema)
    {
        SourceQueryValidator.Validate(query, schema);

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal) { [query.RootTable] = "t0" };
        foreach (var join in query.Joins)
            aliases[join.Table] = $"t{aliases.Count}";
        string Ref(string? table, string column) => $"{aliases[table ?? query.RootTable]}.{QI(column)}";

        var columns =
            query.Columns.Count > 0
                ? query.Columns
                : schema
                    .Tables.First(t => t.Name == query.RootTable)
                    .Columns.Select(c => new QueryColumn { Column = c.Name })
                    .ToList();

        var sql = new StringBuilder("SELECT ");
        sql.AppendJoin(", ", columns.Select(c => $"{Ref(c.Table, c.Column)} AS {QI(c.OutputName)}"));
        sql.Append($" FROM {QI(query.RootTable)} AS t0");

        foreach (var join in query.Joins)
        {
            var keyword = join.Type == QueryJoinType.Left ? "LEFT JOIN" : "INNER JOIN";
            sql.Append(
                $" {keyword} {QI(join.Table)} AS {aliases[join.Table]}"
                    + $" ON {Ref(join.Table, join.Column)} = {Ref(join.ParentTable, join.ParentColumn)}"
            );
        }

        var parameters = new List<NpgsqlParameter>();
        if (query.Conditions.Count > 0)
        {
            sql.Append(" WHERE ");
            sql.AppendJoin(
                " AND ",
                query.Conditions.Select(c => CompileCondition(Ref(c.Table, c.Column), c, parameters))
            );
        }

        if (query.Limit.HasValue)
            sql.Append(' ').Append(Dialect.BuildLimit(query.Limit.Value));

        return new CompiledPostgreSqlQuery(sql.ToString(), parameters);
    }

    private static string CompileCondition(string column, QueryCondition condition, List<NpgsqlParameter> parameters)
    {
        string Bind(object value)
        {
            var name = Dialect.BuildParameterName(parameters.Count);
            parameters.Add(CreateParameter(name, value));
            return name;
        }

        // LIKE patterns match literally: the value's own %, _ and \ are escaped (backslash is Postgres's
        // default LIKE escape character), and the ::text cast lets a non-text column be matched too.
        string Like(string prefix, string suffix) =>
            $"{Dialect.CastToText(column)} LIKE {Bind(prefix + EscapeLike((string)condition.Value!) + suffix)}";

        return condition.Operator switch
        {
            QueryOperator.Equal => $"{column} = {Bind(condition.Value!)}",
            QueryOperator.NotEqual => $"{column} <> {Bind(condition.Value!)}",
            QueryOperator.GreaterThan => $"{column} > {Bind(condition.Value!)}",
            QueryOperator.GreaterThanOrEqual => $"{column} >= {Bind(condition.Value!)}",
            QueryOperator.LessThan => $"{column} < {Bind(condition.Value!)}",
            QueryOperator.LessThanOrEqual => $"{column} <= {Bind(condition.Value!)}",
            QueryOperator.Contains => Like("%", "%"),
            QueryOperator.StartsWith => Like("", "%"),
            QueryOperator.EndsWith => Like("%", ""),
            QueryOperator.IsNull => $"{column} IS NULL",
            QueryOperator.IsNotNull => $"{column} IS NOT NULL",
            QueryOperator.In => $"{column} IN ({string.Join(", ", condition.Values.Select(Bind))})",
            _ => throw new InvalidSourceQueryException($"Unsupported operator '{condition.Operator}'."),
        };
    }

    // A string value is sent with PostgreSQL's "unknown" type rather than Npgsql's default text, so Postgres
    // infers it from the column it's compared with — exactly as it would an untyped literal. That lets a caller
    // filter a uuid/date/numeric column with a string value without the query needing a dialect-specific cast.
    // Every other scalar keeps Npgsql's own CLR-type mapping.
    private static NpgsqlParameter CreateParameter(string name, object value) =>
        value is string
            ? new NpgsqlParameter(name, NpgsqlDbType.Unknown) { Value = value }
            : new NpgsqlParameter(name, value);

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static string QI(string identifier) => Dialect.QuoteIdentifier(identifier);
}
