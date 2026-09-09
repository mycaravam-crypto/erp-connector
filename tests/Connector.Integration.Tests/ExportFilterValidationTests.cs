using Connector.Api;
using Connector.Api.Endpoints;
using Connector.Core.DynamicExport;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for the Filter validation added to <see cref="ExportDefinitionEndpoints.ValidateRequestAsync"/>
/// (security audit finding: Filter is concatenated verbatim into the WHERE clause run against the ERP
/// Postgres source by <c>DynamicExportService.ExecuteExportNodeQueryAsync</c>/<c>BuildExportNodeExpr</c>, so
/// it must be screened for SQL-injection primitives at save time). Runs entirely against the in-memory
/// Sqlite <see cref="LocalDb"/> fixture — no Postgres testdb required.
/// </summary>
public sealed class ExportFilterValidationTests
{
    private static ExportNode Scalar(string targetKey, string sourceField) =>
        new(
            TargetKey: targetKey,
            Kind: ExportNodeKind.ScalarField,
            SourceField: sourceField,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: null,
            Mapping: null,
            Children: [],
            Enabled: true
        );

    private static ExportNode Root(string? filter, params ExportNode[] children) =>
        new(
            TargetKey: "root",
            Kind: ExportNodeKind.Root,
            SourceField: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: filter,
            Mapping: null,
            Children: children,
            Enabled: true
        );

    private static ExportNode ObjectNode(string? filter, params ExportNode[] children) =>
        new(
            TargetKey: "related",
            Kind: ExportNodeKind.Object,
            SourceField: null,
            RelatedTable: "relatedtable",
            JoinKey: "id",
            SourceJoinKey: "related_id",
            Filter: filter,
            Mapping: null,
            Children: children,
            Enabled: true
        );

    private static ExportDefinitionRequest Request(ExportNode root) =>
        new(
            Name: "Test Export Definition",
            Description: null,
            RootTable: "systemconfiguration",
            RootNode: root,
            OutputFormat: "json",
            IsEnabled: false,
            Schedule: null,
            IntegrationKey: null,
            ContractVersion: null
        );

    [Theory]
    [InlineData("status = 'active'")]
    [InlineData("amount > 100 AND category = 'x'")]
    [InlineData("created_at >= '2024-01-01' AND created_at < '2025-01-01'")]
    // SR-01 regression coverage: the general function-call rejection must not reject the parenthesized
    // boolean-grouping and IN-list syntax that legitimate filters (per SafeFilterCharsRegex's own
    // comment) actually need — only real function calls.
    [InlineData("status = 'active' AND (category = 'x' OR category = 'y')")]
    [InlineData("status IN ('active', 'pending')")]
    [InlineData(null)]
    public async Task Export_SafeRootFilter_Accepted(string? filter)
    {
        await using var local = await LocalDb.NewAsync();
        var request = Request(Root(filter, Scalar("id", "id")));

        var (root, error) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(error);
        Assert.NotNull(root);
    }

    [Theory]
    [InlineData("1=1); DROP TABLE systemconfiguration; --")]
    [InlineData("1=1 UNION SELECT password FROM users")]
    [InlineData("1=1 -- comment")]
    [InlineData("1=1 /* comment */")]
    [InlineData("pg_sleep(10) IS NULL")]
    [InlineData("1=1; SELECT 1")]
    [InlineData("current_setting('x') = 'y'")]
    // SR-01: pg_sleep_for/pg_sleep_until are real Postgres functions distinct from pg_sleep, but
    // \b in DangerousFilterKeywordRegex doesn't stop at '_' — these bypassed the name blacklist
    // entirely before the general function-call rejection was added.
    [InlineData("pg_sleep_for('5 seconds') IS NULL")]
    [InlineData("pg_sleep_until(now() + interval '5 seconds') IS NULL")]
    [InlineData("query_to_xml('select 1', false, false, '') IS NOT NULL")]
    // Not on any keyword list at all — the general function-call rejection must catch unknown/future
    // functions too, not just ones someone thought to enumerate.
    [InlineData("some_future_dangerous_function(1) = 1")]
    public async Task Export_UnsafeRootFilter_Rejected(string filter)
    {
        await using var local = await LocalDb.NewAsync();
        var request = Request(Root(filter, Scalar("id", "id")));

        var (root, error) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(root);
        Assert.NotNull(error);
        Assert.Contains("Filter", error);
    }

    [Fact]
    public async Task Export_UnsafeNestedNodeFilter_Rejected()
    {
        await using var local = await LocalDb.NewAsync();
        var nested = ObjectNode("1=1; DROP TABLE systemconfiguration; --", Scalar("name", "name"));
        var request = Request(Root(null, nested));

        var (root, error) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(root);
        Assert.NotNull(error);
        Assert.Contains("Filter", error);
    }
}
