namespace Connector.Core.DataSources;

/// <summary>
/// Checks a <see cref="SourceQuery"/> against a known <see cref="SourceSchema"/> before any provider compiles
/// it: every table and column must exist in the schema (matched exactly, case-sensitively), joins must build on
/// the root table or an earlier join, every table appears at most once, output names are unique, and each
/// condition's operand fits its operator. Dialect-free on purpose, so every provider's compiler shares it — a
/// compiler may assume any query that passes is safe to turn into native syntax (every name it quotes is one
/// the schema itself reported).
/// </summary>
public static class SourceQueryValidator
{
    /// <summary>Throws <see cref="InvalidSourceQueryException"/> describing the first problem found.</summary>
    public static void Validate(SourceQuery query, SourceSchema schema)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(schema);

        var tables = new Dictionary<string, SourceTable>(StringComparer.Ordinal)
        {
            [query.RootTable] = FindTable(schema, query.RootTable),
        };

        foreach (var join in query.Joins)
        {
            var parent = ResolveInScope(tables, join.ParentTable ?? query.RootTable, "join parent");
            RequireColumn(parent, join.ParentColumn);

            var joined = FindTable(schema, join.Table);
            RequireColumn(joined, join.Column);
            if (!tables.TryAdd(join.Table, joined))
                throw new InvalidSourceQueryException(
                    $"Table '{join.Table}' appears more than once in the query; each table may be used only once."
                );
        }

        var outputNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in query.Columns)
        {
            RequireColumn(ResolveInScope(tables, column.Table ?? query.RootTable, "column"), column.Column);
            if (string.IsNullOrEmpty(column.OutputName))
                throw new InvalidSourceQueryException($"Column '{column.Column}' has an empty alias.");
            if (!outputNames.Add(column.OutputName))
                throw new InvalidSourceQueryException(
                    $"Output column name '{column.OutputName}' is used more than once."
                );
        }

        foreach (var condition in query.Conditions)
        {
            RequireColumn(ResolveInScope(tables, condition.Table ?? query.RootTable, "condition"), condition.Column);
            ValidateOperand(condition);
        }

        if (query.Limit < 0)
            throw new InvalidSourceQueryException($"Limit must not be negative (was {query.Limit}).");
    }

    private static SourceTable FindTable(SourceSchema schema, string name) =>
        schema.Tables.FirstOrDefault(t => t.Name == name)
        ?? throw new InvalidSourceQueryException($"Unknown table '{name}'.");

    private static SourceTable ResolveInScope(Dictionary<string, SourceTable> tables, string name, string usage) =>
        tables.TryGetValue(name, out var table)
            ? table
            : throw new InvalidSourceQueryException(
                $"The {usage} references table '{name}', which is neither the root table nor joined before it."
            );

    private static void RequireColumn(SourceTable table, string column)
    {
        if (!table.Columns.Any(c => c.Name == column))
            throw new InvalidSourceQueryException($"Unknown column '{column}' on table '{table.Name}'.");
    }

    private static void ValidateOperand(QueryCondition condition)
    {
        var target = $"Condition on '{condition.Column}' ({condition.Operator})";
        switch (condition.Operator)
        {
            case QueryOperator.IsNull or QueryOperator.IsNotNull:
                if (condition.Value is not null || condition.Values.Count > 0)
                    throw new InvalidSourceQueryException($"{target} takes no value.");
                break;

            case QueryOperator.In:
                if (condition.Value is not null)
                    throw new InvalidSourceQueryException($"{target} takes its candidates in Values, not Value.");
                if (condition.Values.Count == 0)
                    throw new InvalidSourceQueryException($"{target} needs at least one value.");
                foreach (var value in condition.Values)
                    RequireScalar(target, value);
                break;

            case QueryOperator.Contains
            or QueryOperator.StartsWith
            or QueryOperator.EndsWith:
                if (condition.Value is not string)
                    throw new InvalidSourceQueryException($"{target} needs a string value.");
                RequireNoValues(target, condition);
                break;

            case QueryOperator.Equal
            or QueryOperator.NotEqual
            or QueryOperator.GreaterThan
            or QueryOperator.GreaterThanOrEqual
            or QueryOperator.LessThan
            or QueryOperator.LessThanOrEqual:
                if (condition.Value is null)
                    throw new InvalidSourceQueryException(
                        $"{target} needs a value; use IsNull/IsNotNull to test for null."
                    );
                RequireScalar(target, condition.Value);
                RequireNoValues(target, condition);
                break;

            default:
                throw new InvalidSourceQueryException($"{target}: unsupported operator.");
        }
    }

    private static void RequireNoValues(string target, QueryCondition condition)
    {
        if (condition.Values.Count > 0)
            throw new InvalidSourceQueryException($"{target} takes a single Value, not Values.");
    }

    // Only plain scalars reach a provider as parameter values — nothing a driver might serialize into
    // something other than a single bound value (arrays, dictionaries, arbitrary objects).
    private static void RequireScalar(string target, object? value)
    {
        if (
            value
            is not (
                string
                or bool
                or byte
                or short
                or int
                or long
                or float
                or double
                or decimal
                or Guid
                or DateTime
                or DateTimeOffset
                or DateOnly
                or TimeOnly
            )
        )
            throw new InvalidSourceQueryException(
                $"{target} has an unsupported value type '{value?.GetType().Name ?? "null"}'."
            );
    }
}
