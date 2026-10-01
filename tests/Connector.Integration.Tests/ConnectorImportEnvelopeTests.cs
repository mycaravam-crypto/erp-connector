using System.Text;
using System.Text.Json.Nodes;
using Connector.Api;
using Connector.Api.Endpoints;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Connector-to-connector JSON mode: an export definition with a <c>TargetImportDefinition</c> writes an
/// <c>ImportEnvelope</c> that a receiving instance's <see cref="ImportWorker"/> can route, and the setting is
/// validated at save time. No Postgres testdb required.
/// </summary>
public sealed class ConnectorImportEnvelopeTests
{
    [Fact]
    public void BuildImportEnvelopeBytes_ProducesARoutableImportEnvelope()
    {
        var records = new List<JsonObject> { new() { ["ciId"] = "abc", ["status"] = "active" } };

        var bytes = DynamicExportService.BuildImportEnvelopeBytes(
            records,
            "CI Alignment",
            new DateTimeOffset(2026, 10, 1, 6, 0, 0, TimeSpan.Zero),
            new ExportProvenance("ci-alignment", 1, 3)
        );
        var json = Encoding.UTF8.GetString(bytes);
        var envelope = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("CI Alignment", ImportWorker.ExtractDefinitionName(json));
        Assert.Equal(ImportNodeWalker.SupportedSchemaVersion, envelope["schemaVersion"]!.GetValue<string>());
        Assert.Equal("2026-10-01T06:00:00.0000000+00:00", envelope["generatedAt"]!.GetValue<string>());
        Assert.Equal("ci-alignment", envelope["provenance"]!["integrationKey"]!.GetValue<string>());
        Assert.Equal("abc", envelope["records"]![0]!["ciId"]!.GetValue<string>());
        Assert.False(envelope.ContainsKey("schema_version"));
    }

    [Fact]
    public void BuildImportEnvelopeBytes_WithoutProvenance_OmitsTheKey()
    {
        var bytes = DynamicExportService.BuildImportEnvelopeBytes([], "CI Alignment", DateTimeOffset.UtcNow, null);

        Assert.False(JsonNode.Parse(bytes)!.AsObject().ContainsKey("provenance"));
    }

    private static ExportDefinitionRequest Request(string outputFormat, string? targetImportDefinition) =>
        new(
            Name: "Alignment Export",
            Description: null,
            RootTable: "systemconfiguration",
            RootNode: new ExportNode(
                "root",
                ExportNodeKind.Root,
                null,
                null,
                null,
                null,
                null,
                null,
                [new ExportNode("id", ExportNodeKind.ScalarField, "id", null, null, null, null, null, [], true)],
                true
            ),
            OutputFormat: outputFormat,
            IsEnabled: false,
            Schedule: null,
            TargetImportDefinition: targetImportDefinition
        );

    [Theory]
    [InlineData("json", "CI Alignment", null)]
    [InlineData("json", null, null)]
    [InlineData("csv", null, null)]
    [InlineData("csv", "CI Alignment", "TargetImportDefinition requires OutputFormat json.")]
    [InlineData("json", " ", "TargetImportDefinition must be non-empty and free of control characters.")]
    [InlineData("json", "bad\nname", "TargetImportDefinition must be non-empty and free of control characters.")]
    public async Task ValidateRequestAsync_TargetImportDefinition(
        string outputFormat,
        string? targetImportDefinition,
        string? expectedError
    )
    {
        await using var local = await LocalDb.NewAsync();

        var (_, error) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            Request(outputFormat, targetImportDefinition),
            local.Db,
            CancellationToken.None
        );

        Assert.Equal(expectedError, error);
    }
}
