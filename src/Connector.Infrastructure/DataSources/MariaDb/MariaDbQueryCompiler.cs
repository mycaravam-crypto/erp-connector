using System.Text;
using Connector.Core.DataSources;
using MySqlConnector;

namespace Connector.Infrastructure.DataSources.MariaDb;

/// <summary>A <see cref="SourceQuery"/> compiled to MariaDB: SQL text plus the <c>@p0</c>, <c>@p1</c>, … parameters
/// it references.</summary>
public sealed record CompiledMariaDbQuery(string Sql, IReadOnlyList<MySqlParameter> Parameters);

/// <summary>
/// Compiles the database-neutral <see cref="SourceQuery"/> into MariaDB SQL — the MariaDB counterpart of
/// <c>PostgreSqlQueryCompiler</c>, with the same guarantees: validated against the given <see cref="SourceSchema"/>
/// first (<see cref="SourceQueryValidator"/>), so every emitted identifier is one the schema reported
/// (backtick-quoted by <see cref="MariaDbDialect"/>); synthetic table aliases (<c>t0</c>, <c>t1</c>, …); every
/// filter value a bound parameter; <see cref="SourceQuery.Limit"/> an <see cref="int"/> rendered by the dialect.
/// </summary>
public static class MariaDbQueryCompiler
{
    private static readonly MariaDbDialect Dialect = MariaDbDialect.Instance;

    public static CompiledMariaDbQuery Compile(SourceQuery query, SourceSchema schema)
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

        var parameters = new List<MySqlParameter>();
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

        return new CompiledMariaDbQuery(sql.ToString(), parameters);
    }

    private static string CompileCondition(string column, QueryCondition condition, List<MySqlParameter> parameters)
    {
        string Bind(object value)
        {
            var name = Dialect.BuildParameterName(parameters.Count);
            parameters.Add(new MySqlParameter(name, value));
            return name;
        }

        // The model's LIKE operators match literally and case-sensitively. MariaDB's default collations compare
        // case-insensitively, so the column is cast to text and compared under the binary utf8mb4 collation
        // (MySqlConnector's connection charset); backslash is MariaDB's default LIKE escape character.
        string Like(string prefix, string suffix) =>
            $"{Dialect.CastToText(column)} COLLATE utf8mb4_bin LIKE "
            + Bind(prefix + EscapeLike((string)condition.Value!) + suffix);

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

    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static string QI(string identifier) => Dialect.QuoteIdentifier(identifier);
}
