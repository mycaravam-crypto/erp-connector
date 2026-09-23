using System.Net;
using System.Web;
using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources.ServiceNow;

namespace Connector.Integration.Tests;

/// <summary>
/// <see cref="ServiceNowTableApiProvider"/> (Arbeitsauftrag 9) against <see cref="FakeServiceNow"/>, an in-memory
/// Table API — no real instance or credentials. Covers authentication (success, 401, 403), schema (table metadata,
/// inherited fields, reference fields as relations), queries (projection, server-side filter, pagination, joins,
/// limit, empty-as-null), retry on 429/502/503 only, request timeout and cancellation.
/// </summary>
public sealed class ServiceNowTableApiProviderTests
{
    private static SourceQuery Incidents(params QueryCondition[] conditions) =>
        new()
        {
            RootTable = "incident",
            Columns = [new QueryColumn { Column = "number" }, new QueryColumn { Column = "category" }],
            Conditions = conditions,
        };

    private static string? QueryParam(Uri uri, string name) => HttpUtility.ParseQueryString(uri.Query)[name];

    // ── Authentication ───────────────────────────────────────────────────────

    [Fact]
    public async Task TestConnection_ValidCredentials_ReturnsSchema()
    {
        var sn = FakeServiceNow.WithIncidents();

        var result = await sn.CreateProvider().TestConnectionAsync(FakeServiceNow.Config, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal("fake.service-now.com", result.Schema!.ConnectionLabel);
        Assert.Contains(result.Schema.Tables, t => t.Name == "incident");
    }

    [Fact]
    public async Task TestConnection_WrongPassword_Fails401WithoutRetryOrLeakingIt()
    {
        var sn = FakeServiceNow.WithIncidents();

        var result = await sn.CreateProvider()
            .TestConnectionAsync(
                FakeServiceNow.Config with
                {
                    Password = "wrong-pw-do-not-echo",
                },
                CancellationToken.None
            );

        Assert.False(result.Success);
        Assert.Contains("401", result.Error);
        Assert.DoesNotContain("wrong-pw-do-not-echo", result.Error);
        Assert.Single(sn.Requests); // 401 is never retried
    }

    [Fact]
    public async Task ExecuteAsync_TableDeniedByAcl_Throws403WithoutRetry()
    {
        var sn = FakeServiceNow.WithIncidents();
        sn.ForbiddenTables.Add("incident");

        var ex = await Assert.ThrowsAsync<DataSourceQueryException>(() =>
            sn.CreateProvider().ExecuteAsync(FakeServiceNow.Config, Incidents(), CancellationToken.None)
        );

        Assert.Equal("403", ex.ErrorCode);
        Assert.Contains("ACL", ex.Message);
        Assert.Single(sn.TableRequests("incident"));
    }

    [Fact]
    public async Task PlainHttpInstanceUrl_IsRefusedBeforeAnyRequest()
    {
        var sn = FakeServiceNow.WithIncidents();

        var result = await sn.CreateProvider()
            .TestConnectionAsync(
                FakeServiceNow.Config with
                {
                    InstanceUrl = "http://fake.service-now.com",
                },
                CancellationToken.None
            );

        Assert.False(result.Success);
        Assert.Contains("https", result.Error);
        Assert.Empty(sn.Requests);
    }

    // ── Schema ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadSchema_TableMetadataIncludesInheritedFields()
    {
        var schema = await FakeServiceNow
            .WithIncidents()
            .CreateProvider()
            .ReadSchemaAsync(FakeServiceNow.Config, CancellationToken.None);

        var incident = schema.Tables.Single(t => t.Name == "incident");
        Assert.Equal(
            ["assignment_group", "category", "sys_id", "number", "short_description", "priority"],
            incident.Columns.Select(c => c.Name)
        );
        var sysId = incident.Columns.Single(c => c.Name == "sys_id");
        Assert.True(sysId.PrimaryKey);
        Assert.False(sysId.Nullable);
        Assert.False(incident.Columns.Single(c => c.Name == "number").Nullable); // mandatory
        Assert.Equal("integer", incident.Columns.Single(c => c.Name == "priority").Type);
    }

    [Fact]
    public async Task ReadSchema_ReferenceFieldIsARelation()
    {
        var schema = await FakeServiceNow
            .WithIncidents()
            .CreateProvider()
            .ReadSchemaAsync(FakeServiceNow.Config, CancellationToken.None);

        var group = schema.Tables.Single(t => t.Name == "incident").Columns.Single(c => c.Name == "assignment_group");
        Assert.Equal("reference", group.Type);
        Assert.Equal("sys_user_group", group.ForeignKeyTable);
        Assert.Equal("sys_id", group.ForeignKeyColumn);
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SimpleQuery_ReturnsEveryRecordWithEmptyAsNull()
    {
        var result = await FakeServiceNow
            .WithIncidents()
            .CreateProvider()
            .ExecuteAsync(FakeServiceNow.Config, Incidents(), CancellationToken.None);

        Assert.Equal(["number", "category"], result.Columns.Select(c => c.Name));
        var rows = result.ToDictionaries().OrderBy(r => r["number"]).ToList();
        Assert.Equal(["INC001", "INC002", "INC003"], rows.Select(r => r["number"]));
        Assert.Null(rows[1]["category"]);
    }

    [Fact]
    public async Task ExecuteAsync_Projection_RequestsOnlyTheSelectedFields()
    {
        var sn = FakeServiceNow.WithIncidents();

        await sn.CreateProvider().ExecuteAsync(FakeServiceNow.Config, Incidents(), CancellationToken.None);

        Assert.Equal("category,number", QueryParam(sn.TableRequests("incident").Single(), "sysparm_fields"));
    }

    [Fact]
    public async Task ExecuteAsync_Filter_IsSentServerSideAsEncodedQuery()
    {
        var sn = FakeServiceNow.WithIncidents();

        var result = await sn.CreateProvider()
            .ExecuteAsync(
                FakeServiceNow.Config,
                Incidents(
                    new QueryCondition
                    {
                        Column = "priority",
                        Operator = QueryOperator.LessThanOrEqual,
                        Value = 2,
                    },
                    new QueryCondition { Column = "category", Operator = QueryOperator.IsNotNull }
                ),
                CancellationToken.None
            );

        Assert.Equal(
            "priority<=2^categoryISNOTEMPTY^ORDERBYsys_id",
            QueryParam(sn.TableRequests("incident").Single(), "sysparm_query")
        );
        Assert.Equal(["INC001", "INC003"], result.ToDictionaries().Select(r => r["number"]).Order());
    }

    [Fact]
    public async Task ExecuteAsync_ValueWithEncodedQuerySeparator_IsRejectedBeforeAnyTableRead()
    {
        var sn = FakeServiceNow.WithIncidents();

        await Assert.ThrowsAsync<InvalidSourceQueryException>(() =>
            sn.CreateProvider()
                .ExecuteAsync(
                    FakeServiceNow.Config,
                    Incidents(
                        new QueryCondition
                        {
                            Column = "number",
                            Operator = QueryOperator.Equal,
                            Value = "x^ORpriority>0",
                        }
                    ),
                    CancellationToken.None
                )
        );
        Assert.Empty(sn.TableRequests("incident"));
    }

    [Fact]
    public async Task ExecuteAsync_Pagination_ReadsEveryPage()
    {
        var sn = FakeServiceNow.WithIncidents();
        var provider = sn.CreateProvider(new ServiceNowClientOptions { PageSize = 2 });

        var result = await provider.ExecuteAsync(FakeServiceNow.Config, Incidents(), CancellationToken.None);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal(["0", "2"], sn.TableRequests("incident").Select(u => QueryParam(u, "sysparm_offset")));
    }

    [Fact]
    public async Task ExecuteAsync_Limit_StopsReading()
    {
        var sn = FakeServiceNow.WithIncidents();

        var result = await sn.CreateProvider(new ServiceNowClientOptions { PageSize = 2 })
            .ExecuteAsync(FakeServiceNow.Config, Incidents() with { Limit = 1 }, CancellationToken.None);

        Assert.Single(result.Rows);
        Assert.Equal("1", QueryParam(sn.TableRequests("incident").Single(), "sysparm_limit"));
    }

    [Theory]
    [InlineData(QueryJoinType.Left, 3)]
    [InlineData(QueryJoinType.Inner, 2)]
    public async Task ExecuteAsync_JoinOverReferenceField_OneBatchedReadForTheJoinedTable(
        QueryJoinType type,
        int expectedRows
    )
    {
        var sn = FakeServiceNow.WithIncidents();

        var result = await sn.CreateProvider()
            .ExecuteAsync(
                FakeServiceNow.Config,
                new SourceQuery
                {
                    RootTable = "incident",
                    Columns =
                    [
                        new QueryColumn { Column = "number" },
                        new QueryColumn
                        {
                            Table = "sys_user_group",
                            Column = "name",
                            Alias = "group",
                        },
                    ],
                    Joins =
                    [
                        new QueryJoin
                        {
                            Table = "sys_user_group",
                            Column = "sys_id",
                            ParentColumn = "assignment_group",
                            Type = type,
                        },
                    ],
                },
                CancellationToken.None
            );

        var rows = result.ToDictionaries().ToDictionary(r => r["number"]!, r => r["group"]);
        Assert.Equal(expectedRows, rows.Count);
        Assert.Equal("Service Desk", rows["INC001"]);
        Assert.Equal("Network", rows["INC002"]);
        if (type == QueryJoinType.Left)
            Assert.Null(rows["INC003"]);
        Assert.Equal(
            "sys_idINg1,g2^ORDERBYsys_id",
            QueryParam(sn.TableRequests("sys_user_group").Single(), "sysparm_query")
        );
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTableOrColumn_FailsExplicitly()
    {
        var provider = FakeServiceNow.WithIncidents().CreateProvider();

        await Assert.ThrowsAsync<InvalidSourceQueryException>(() =>
            provider.ExecuteAsync(
                FakeServiceNow.Config,
                new SourceQuery { RootTable = "no_such_table" },
                CancellationToken.None
            )
        );
        await Assert.ThrowsAsync<InvalidSourceQueryException>(() =>
            provider.ExecuteAsync(
                FakeServiceNow.Config,
                new SourceQuery { RootTable = "incident", Columns = [new QueryColumn { Column = "nope" }] },
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ExecuteNativeAsync_IsNotSupported() =>
        await Assert.ThrowsAsync<UnsupportedDataSourceException>(() =>
            FakeServiceNow
                .WithIncidents()
                .CreateProvider()
                .ExecuteNativeAsync(FakeServiceNow.Config, new NativeSqlQuery("SELECT 1"), CancellationToken.None)
        );

    // ── Retry, timeout, cancellation ─────────────────────────────────────────

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientFailure_IsRetried(HttpStatusCode status)
    {
        var sn = FakeServiceNow.WithIncidents();
        sn.ScriptedFailures.Enqueue((status, TimeSpan.Zero));
        sn.ScriptedFailures.Enqueue((status, null));

        var result = await sn.CreateProvider().TestConnectionAsync(FakeServiceNow.Config, CancellationToken.None);

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public async Task RateLimit_GivesUpAfterMaxRetries()
    {
        var sn = FakeServiceNow.WithIncidents();
        for (var i = 0; i < 10; i++)
            sn.ScriptedFailures.Enqueue((HttpStatusCode.TooManyRequests, TimeSpan.Zero));
        var provider = sn.CreateProvider(
            new ServiceNowClientOptions { MaxRetries = 2, RetryBaseDelay = TimeSpan.FromMilliseconds(1) }
        );

        var ex = await Assert.ThrowsAsync<DataSourceQueryException>(() =>
            provider.ReadSchemaAsync(FakeServiceNow.Config, CancellationToken.None)
        );

        Assert.Equal("429", ex.ErrorCode);
        Assert.Equal(3, sn.Requests.Count); // first attempt + 2 retries, then stop
    }

    [Fact]
    public async Task BadRequest_IsNotRetried()
    {
        var sn = FakeServiceNow.WithIncidents();
        sn.ScriptedFailures.Enqueue((HttpStatusCode.BadRequest, null));

        var ex = await Assert.ThrowsAsync<DataSourceQueryException>(() =>
            sn.CreateProvider().ReadSchemaAsync(FakeServiceNow.Config, CancellationToken.None)
        );

        Assert.Equal("400", ex.ErrorCode);
        Assert.Single(sn.Requests);
    }

    [Fact]
    public async Task SlowResponse_TimesOut()
    {
        var sn = FakeServiceNow.WithIncidents();
        sn.Delay = TimeSpan.FromSeconds(5);
        var provider = sn.CreateProvider(
            new ServiceNowClientOptions { RequestTimeout = TimeSpan.FromMilliseconds(100) }
        );

        await Assert.ThrowsAsync<TimeoutException>(() =>
            provider.ReadSchemaAsync(FakeServiceNow.Config, CancellationToken.None)
        );
    }

    [Fact]
    public async Task Cancellation_AbortsTheRequest()
    {
        var sn = FakeServiceNow.WithIncidents();
        sn.Delay = TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sn.CreateProvider().ExecuteAsync(FakeServiceNow.Config, Incidents(), cts.Token)
        );
    }
}
