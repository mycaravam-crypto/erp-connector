using Connector.Core.DataSources;
using Connector.Infrastructure;
using NpgsqlTypes;

namespace Connector.Integration.Tests;

/// <summary>
/// Pure unit tests (no DB) for <see cref="PostgreSqlQueryCompiler"/> — Arbeitsauftrag 4's required cases
/// (simple SELECT, projection, multiple conditions, null check, IN, limit, unknown field, unknown table) plus
/// the join/LIKE/parameter-typing rules the compiler adds on top. Live execution of compiled queries is
/// covered by <see cref="PostgreSqlDataSourceProviderTests"/>.
/// </summary>
public sealed class PostgreSqlQueryCompilerTests
{
    private static readonly SourceSchema Schema = new(
        "test",
        [
            new SourceTable(
                "orders",
                "",
                [
                    new SourceColumn("id", "integer", Nullable: false, PrimaryKey: true),
                    new SourceColumn("customer_id", "uuid", Nullable: true, PrimaryKey: false, "customers", "id"),
                    new SourceColumn("status", "character varying", Nullable: false, PrimaryKey: false),
                    new SourceColumn("total", "numeric", Nullable: true, PrimaryKey: false),
                    new SourceColumn("shipped_at", "date", Nullable: true, PrimaryKey: false),
                ]
            ),
            new SourceTable(
                "customers",
                "",
                [
                    new SourceColumn("id", "uuid", Nullable: false, PrimaryKey: true),
                    new SourceColumn("name", "character varying", Nullable: true, PrimaryKey: false),
                ]
            ),
        ]
    );

    private static CompiledPostgreSqlQuery Compile(SourceQuery query) => PostgreSqlQueryCompiler.Compile(query, Schema);

    private static QueryCondition Condition(string column, QueryOperator op, object? value = null) =>
        new()
        {
            Column = column,
            Operator = op,
            Value = value,
        };

    [Fact]
    public void Compile_SimpleSelect_ProjectsEveryRootColumnInSchemaOrder()
    {
        var compiled = Compile(new SourceQuery { RootTable = "orders" });

        Assert.Equal(
            """SELECT t0."id" AS "id", t0."customer_id" AS "customer_id", t0."status" AS "status", """
                + """t0."total" AS "total", t0."shipped_at" AS "shipped_at" FROM "orders" AS t0""",
            compiled.Sql
        );
        Assert.Empty(compiled.Parameters);
    }

    [Fact]
    public void Compile_FieldProjection_EmitsOnlyRequestedColumnsInOrderWithAliases()
    {
        var compiled = Compile(
            new SourceQuery
            {
                RootTable = "orders",
                Columns =
                [
                    new QueryColumn { Column = "status" },
                    new QueryColumn { Column = "id", Alias = "order_no" },
                ],
            }
        );

        Assert.Equal("""SELECT t0."status" AS "status", t0."id" AS "order_no" FROM "orders" AS t0""", compiled.Sql);
    }

    [Fact]
    public void Compile_MultipleConditions_AreAndCombinedWithEveryValueBoundAsParameter()
    {
        var compiled = Compile(
            new SourceQuery
            {
                RootTable = "orders",
                Columns = [new QueryColumn { Column = "id" }],
                Conditions =
                [
                    Condition("status", QueryOperator.Equal, "open"),
                    Condition("total", QueryOperator.GreaterThanOrEqual, 100m),
                    Condition("id", QueryOperator.NotEqual, 7),
                    Condition("total", QueryOperator.LessThan, 500m),
                ],
            }
        );

        Assert.Equal(
            """SELECT t0."id" AS "id" FROM "orders" AS t0 WHERE t0."status" = @p0 AND t0."total" >= @p1 """
                + """AND t0."id" <> @p2 AND t0."total" < @p3""",
            compiled.Sql
        );
        Assert.Equal(["p0", "p1", "p2", "p3"], compiled.Parameters.Select(p => p.ParameterName));
        Assert.Equal(["open", 100m, 7, 500m], compiled.Parameters.Select(p => p.Value));
    }

    [Fact]
    public void Compile_NullChecks_EmitIsNullAndIsNotNullWithoutParameters()
    {
        var compiled = Compile(
            new SourceQuery
            {
                RootTable = "orders",
                Columns = [new QueryColumn { Column = "id" }],
                Conditions =
                [
                    Condition("shipped_at", QueryOperator.IsNull),
                    Condition("customer_id", QueryOperator.IsNotNull),
                ],
            }
        );

        Assert.EndsWith("""WHERE t0."shipped_at" IS NULL AND t0."customer_id" IS NOT NULL""", compiled.Sql);
        Assert.Empty(compiled.Parameters);
    }

    [Fact]
    public void Compile_In_BindsOneParameterPerValue()
    {
        var compiled = Compile(
            new SourceQuery
            {
                RootTable = "orders",
                Columns = [new QueryColumn { Column = "id" }],
                Conditions =
                [
                    new QueryCondition
                    {
                        Column = "status",
                        Operator = QueryOperator.In,
                        Values = ["open", "shipped", "x'); DROP TABLE orders; --"],
                    },
                ],
            }
        );

        Assert.EndsWith("""WHERE t0."status" IN (@p0, @p1, @p2)""", compiled.Sql);
        Assert.Equal(["open", "shipped", "x'); DROP TABLE orders; --"], compiled.Parameters.Select(p => p.Value));
        Assert.DoesNotContain("DROP", compiled.Sql);
    }

    [Fact]
    public void Compile_Limit_AppendsLimitClause()
    {
        var compiled = Compile(
            new SourceQuery
            {
                RootTable = "orders",
                Columns = [new QueryColumn { Column = "id" }],
                Conditions = [Condition("status", QueryOperator.Equal, "open")],
                Limit = 25,
            }
        );

        Assert.Equal("""SELECT t0."id" AS "id" FROM "orders" AS t0 WHERE t0."status" = @p0 LIMIT 25""", compiled.Sql);
    }

    [Fact]
    public void Compile_LimitZero_IsKept()
    {
        var compiled = Compile(new SourceQuery { RootTable = "customers", Limit = 0 });

        Assert.EndsWith(" LIMIT 0", compiled.Sql);
    }

    [Fact]
    public void Compile_UnknownProjectedField_Throws()
    {
        var ex = Assert.Throws<InvalidSourceQueryException>(() =>
            Compile(new SourceQuery { RootTable = "orders", Columns = [new QueryColumn { Column = "password" }] })
        );

        Assert.Contains("'password'", ex.Message);
        Assert.Contains("'orders'", ex.Message);
    }

    [Fact]
    public void Compile_UnknownConditionField_Throws()
    {
        Assert.Throws<InvalidSourceQueryException>(() =>
            Compile(
                new SourceQuery
                {
                    RootTable = "orders",
                    Conditions = [Condition("\"id\" = 1 OR 1=1 --", QueryOperator.Equal, 1)],
                }
            )
        );
    }

    [Fact]
    public void Compile_UnknownTable_Throws()
    {
        var ex = Assert.Throws<InvalidSourceQueryException>(() => Compile(new SourceQuery { RootTable = "pg_shadow" }));

        Assert.Contains("'pg_shadow'", ex.Message);
    }

    [Fact]
    public void Compile_TableNameIsMatchedCaseSensitively()
    {
        Assert.Throws<InvalidSourceQueryException>(() => Compile(new SourceQuery { RootTable = "Orders" }));
    }

    [Fact]
    public void Compile_UnknownJoinTable_Throws()
    {
        Assert.Throws<InvalidSourceQueryException>(() =>
            Compile(
                new SourceQuery
                {
                    RootTable = "orders",
                    Joins =
                    [
                        new QueryJoin
                        {
                            Table = "invoices",
                            Column = "order_id",
                            ParentColumn = "id",
                        },
                    ],
                }
            )
        );
    }

    [Fact]
    public void Compile_Join_EmitsSyntheticAliasesAndEquiJoin()
    {
        var compiled = Compile(
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
                        Table = "customers",
                        Column = "name",
                        Operator = QueryOperator.IsNotNull,
                    },
                ],
            }
        );

        Assert.Equal(
            """SELECT t0."id" AS "id", t1."name" AS "customer_name" FROM "orders" AS t0 """
                + """LEFT JOIN "customers" AS t1 ON t1."id" = t0."customer_id" WHERE t1."name" IS NOT NULL""",
            compiled.Sql
        );
    }

    [Theory]
    [InlineData(QueryOperator.Contains, "%50\\%\\_off\\\\%")]
    [InlineData(QueryOperator.StartsWith, "50\\%\\_off\\\\%")]
    [InlineData(QueryOperator.EndsWith, "%50\\%\\_off\\\\")]
    public void Compile_LikeOperators_EscapeWildcardsInTheBoundPattern(QueryOperator op, string expectedPattern)
    {
        var compiled = Compile(
            new SourceQuery
            {
                RootTable = "customers",
                Columns = [new QueryColumn { Column = "id" }],
                Conditions = [Condition("name", op, "50%_off\\")],
            }
        );

        Assert.EndsWith("""WHERE t0."name"::text LIKE @p0""", compiled.Sql);
        Assert.Equal(expectedPattern, Assert.Single(compiled.Parameters).Value);
    }

    [Fact]
    public void Compile_StringValues_AreBoundUntypedSoPostgresInfersTheColumnType()
    {
        var compiled = Compile(
            new SourceQuery
            {
                RootTable = "orders",
                Conditions =
                [
                    Condition("customer_id", QueryOperator.Equal, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    Condition("id", QueryOperator.Equal, 7),
                ],
            }
        );

        // Only the string parameter's type is set explicitly; a non-string value keeps its CLR value for
        // Npgsql's own (lazy, bind-time) inference, so its NpgsqlDbType isn't asserted here. The effect of the
        // untyped binding is exercised end-to-end by PostgreSqlDataSourceProviderTests (string vs. uuid column).
        Assert.Equal(NpgsqlDbType.Unknown, compiled.Parameters[0].NpgsqlDbType);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", compiled.Parameters[0].Value);
        Assert.Equal(7, compiled.Parameters[1].Value);
    }

    [Fact]
    public void Compile_QuotesAliasSoItCannotBreakOutOfTheIdentifier()
    {
        var compiled = Compile(
            new SourceQuery
            {
                RootTable = "orders",
                Columns = [new QueryColumn { Column = "id", Alias = "x\" FROM pg_shadow --" }],
            }
        );

        Assert.Equal("""SELECT t0."id" AS "x"" FROM pg_shadow --" FROM "orders" AS t0""", compiled.Sql);
    }
}
