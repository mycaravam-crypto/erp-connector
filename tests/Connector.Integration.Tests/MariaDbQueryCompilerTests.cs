using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;

namespace Connector.Integration.Tests;

/// <summary>Pure unit tests (no DB) for <see cref="MariaDbQueryCompiler"/>: the SQL it emits and the parameters
/// it binds. <see cref="MariaDbDataSourceProviderTests"/> runs compiled queries against a real server.</summary>
public sealed class MariaDbQueryCompilerTests
{
    private static readonly SourceSchema Schema = new(
        "test",
        [
            new SourceTable(
                "orders",
                "",
                [
                    new SourceColumn("id", "int", false, true),
                    new SourceColumn("status", "varchar", true, false),
                    new SourceColumn("customer_id", "int", true, false),
                ]
            ),
            new SourceTable(
                "customers",
                "",
                [new SourceColumn("id", "int", false, true), new SourceColumn("name", "varchar", true, false)]
            ),
        ]
    );

    [Fact]
    public void Compile_SelectWithJoinConditionsAndLimit()
    {
        var compiled = MariaDbQueryCompiler.Compile(
            new SourceQuery
            {
                RootTable = "orders",
                Columns =
                [
                    new QueryColumn { Column = "id" },
                    new QueryColumn
                    {
                        Table = "customers",
                        Column = "name",
                        Alias = "customer_name",
                    },
                ],
                Joins =
                [
                    new QueryJoin
                    {
                        Table = "customers",
                        Column = "id",
                        ParentColumn = "customer_id",
                        Type = QueryJoinType.Left,
                    },
                ],
                Conditions =
                [
                    new QueryCondition
                    {
                        Column = "status",
                        Operator = QueryOperator.In,
                        Values = ["open", "paid"],
                    },
                    new QueryCondition
                    {
                        Table = "customers",
                        Column = "name",
                        Operator = QueryOperator.StartsWith,
                        Value = "A_c%",
                    },
                    new QueryCondition { Column = "customer_id", Operator = QueryOperator.IsNotNull },
                ],
                Limit = 25,
            },
            Schema
        );

        Assert.Equal(
            "SELECT t0.`id` AS `id`, t1.`name` AS `customer_name` FROM `orders` AS t0"
                + " LEFT JOIN `customers` AS t1 ON t1.`id` = t0.`customer_id`"
                + " WHERE t0.`status` IN (@p0, @p1) AND CAST(t1.`name` AS CHAR) COLLATE utf8mb4_bin LIKE @p2"
                + " AND t0.`customer_id` IS NOT NULL LIMIT 25",
            compiled.Sql
        );
        Assert.Equal(["@p0", "@p1", "@p2"], compiled.Parameters.Select(p => p.ParameterName));
        Assert.Equal(["open", "paid", @"A\_c\%%"], compiled.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void Compile_NoColumns_SelectsEveryRootColumn() =>
        Assert.Equal(
            "SELECT t0.`id` AS `id`, t0.`status` AS `status`, t0.`customer_id` AS `customer_id` FROM `orders` AS t0",
            MariaDbQueryCompiler.Compile(new SourceQuery { RootTable = "orders" }, Schema).Sql
        );

    [Fact]
    public void Compile_UnknownTable_IsRejectedBeforeAnySql() =>
        Assert.Throws<InvalidSourceQueryException>(() =>
            MariaDbQueryCompiler.Compile(new SourceQuery { RootTable = "orders`; DROP TABLE x; --" }, Schema)
        );

    [Fact]
    public void Compile_UnknownColumn_IsRejectedBeforeAnySql() =>
        Assert.Throws<InvalidSourceQueryException>(() =>
            MariaDbQueryCompiler.Compile(
                new SourceQuery
                {
                    RootTable = "orders",
                    Conditions =
                    [
                        new QueryCondition
                        {
                            Column = "nope",
                            Operator = QueryOperator.Equal,
                            Value = 1,
                        },
                    ],
                },
                Schema
            )
        );
}
