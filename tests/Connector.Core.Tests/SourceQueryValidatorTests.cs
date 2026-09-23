using Connector.Core.DataSources;

namespace Connector.Core.Tests;

/// <summary>
/// Dialect-free validation of <see cref="SourceQuery"/> against a known <see cref="SourceSchema"/> — the checks
/// every provider's compiler relies on before emitting native syntax (Arbeitsauftrag 4).
/// </summary>
public sealed class SourceQueryValidatorTests
{
    private static readonly SourceSchema Schema = new(
        "test",
        [
            new SourceTable(
                "orders",
                "",
                [
                    new SourceColumn("id", "integer", false, true),
                    new SourceColumn("customer_id", "uuid", true, false),
                    new SourceColumn("status", "text", false, false),
                ]
            ),
            new SourceTable(
                "customers",
                "",
                [new SourceColumn("id", "uuid", false, true), new SourceColumn("region_id", "integer", true, false)]
            ),
            new SourceTable("regions", "", [new SourceColumn("id", "integer", false, true)]),
        ]
    );

    private static readonly QueryJoin CustomersJoin = new()
    {
        Table = "customers",
        Column = "id",
        ParentColumn = "customer_id",
    };

    private static void Validate(SourceQuery query) => SourceQueryValidator.Validate(query, Schema);

    private static InvalidSourceQueryException Invalid(SourceQuery query) =>
        Assert.Throws<InvalidSourceQueryException>(() => Validate(query));

    private static SourceQuery WithCondition(QueryCondition condition) =>
        new() { RootTable = "orders", Conditions = [condition] };

    [Fact]
    public void Validate_WellFormedQuery_DoesNotThrow()
    {
        Validate(
            new SourceQuery
            {
                RootTable = "orders",
                Columns =
                [
                    new QueryColumn { Column = "id" },
                    new QueryColumn
                    {
                        Table = "regions",
                        Column = "id",
                        Alias = "region",
                    },
                ],
                Joins =
                [
                    CustomersJoin,
                    new QueryJoin
                    {
                        Table = "regions",
                        Column = "id",
                        ParentTable = "customers",
                        ParentColumn = "region_id",
                        Type = QueryJoinType.Left,
                    },
                ],
                Conditions =
                [
                    new QueryCondition
                    {
                        Column = "status",
                        Operator = QueryOperator.In,
                        Values = ["open", "closed"],
                    },
                    new QueryCondition
                    {
                        Table = "customers",
                        Column = "region_id",
                        Operator = QueryOperator.IsNotNull,
                    },
                ],
                Limit = 10,
            }
        );
    }

    [Fact]
    public void Validate_UnknownRootTable_Throws() =>
        Assert.Contains("'invoices'", Invalid(new SourceQuery { RootTable = "invoices" }).Message);

    [Fact]
    public void Validate_UnknownColumn_Throws() =>
        Assert.Contains(
            "'total'",
            Invalid(new SourceQuery { RootTable = "orders", Columns = [new QueryColumn { Column = "total" }] }).Message
        );

    [Fact]
    public void Validate_ColumnOfTableThatIsNotJoined_Throws() =>
        Invalid(
            new SourceQuery { RootTable = "orders", Columns = [new QueryColumn { Table = "customers", Column = "id" }] }
        );

    [Fact]
    public void Validate_JoinReferencingLaterJoin_Throws() =>
        Invalid(
            new SourceQuery
            {
                RootTable = "orders",
                Joins =
                [
                    new QueryJoin
                    {
                        Table = "regions",
                        Column = "id",
                        ParentTable = "customers",
                        ParentColumn = "region_id",
                    },
                    CustomersJoin,
                ],
            }
        );

    [Fact]
    public void Validate_UnknownJoinColumn_Throws() =>
        Invalid(new SourceQuery { RootTable = "orders", Joins = [CustomersJoin with { ParentColumn = "nope" }] });

    [Fact]
    public void Validate_TableJoinedTwice_Throws() =>
        Invalid(new SourceQuery { RootTable = "orders", Joins = [CustomersJoin, CustomersJoin] });

    [Fact]
    public void Validate_DuplicateOutputName_Throws() =>
        Invalid(
            new SourceQuery
            {
                RootTable = "orders",
                Columns = [new QueryColumn { Column = "id" }, new QueryColumn { Column = "status", Alias = "id" }],
            }
        );

    [Fact]
    public void Validate_NegativeLimit_Throws() => Invalid(new SourceQuery { RootTable = "orders", Limit = -1 });

    [Theory]
    [InlineData(QueryOperator.Equal)]
    [InlineData(QueryOperator.GreaterThan)]
    [InlineData(QueryOperator.Contains)]
    public void Validate_BinaryOperatorWithoutValue_Throws(QueryOperator op) =>
        Invalid(WithCondition(new QueryCondition { Column = "status", Operator = op }));

    [Fact]
    public void Validate_IsNullWithValue_Throws() =>
        Invalid(
            WithCondition(
                new QueryCondition
                {
                    Column = "status",
                    Operator = QueryOperator.IsNull,
                    Value = "x",
                }
            )
        );

    [Fact]
    public void Validate_InWithoutValues_Throws() =>
        Invalid(WithCondition(new QueryCondition { Column = "status", Operator = QueryOperator.In }));

    [Fact]
    public void Validate_StartsWithNonString_Throws() =>
        Invalid(
            WithCondition(
                new QueryCondition
                {
                    Column = "id",
                    Operator = QueryOperator.StartsWith,
                    Value = 1,
                }
            )
        );

    [Fact]
    public void Validate_NonScalarValue_Throws() =>
        Invalid(
            WithCondition(
                new QueryCondition
                {
                    Column = "id",
                    Operator = QueryOperator.Equal,
                    Value = new[] { 1, 2 },
                }
            )
        );
}
