using System.Text.Json.Nodes;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="ImportWorker"/>'s pure, DB-free helpers — <see cref="ImportWorker.ExtractDefinitionName(string)"/>
/// (Open Decision #14's <c>definition</c> routing field) and <see cref="ImportWorker.ClassifyDuplicate"/> (Open
/// Decision #13's duplicate/already-staged/already-released/rejected-duplicate split) — without a database,
/// filesystem, or testdb fixture, matching <see cref="ExportDefinitionWorkerCandidateFilterTests"/>'s precedent
/// for isolating a worker's pure selection logic from its I/O.
/// </summary>
public sealed class ImportWorkerLogicTests
{
    [Fact]
    public void ExtractDefinitionName_WellFormedEnvelope_ReturnsDefinitionName()
    {
        var json = """{ "schemaVersion": "1", "definition": "Widget Import", "records": [] }""";

        Assert.Equal("Widget Import", ImportWorker.ExtractDefinitionName(json));
    }

    [Fact]
    public void ExtractDefinitionName_MissingDefinitionProperty_ReturnsNull()
    {
        var json = """{ "schemaVersion": "1", "records": [] }""";

        Assert.Null(ImportWorker.ExtractDefinitionName(json));
    }

    [Fact]
    public void ExtractDefinitionName_BlankDefinition_ReturnsNull()
    {
        var json = """{ "schemaVersion": "1", "definition": "   ", "records": [] }""";

        Assert.Null(ImportWorker.ExtractDefinitionName(json));
    }

    [Fact]
    public void ExtractDefinitionName_DefinitionIsNotAString_ReturnsNull()
    {
        var json = """{ "schemaVersion": "1", "definition": 42, "records": [] }""";

        Assert.Null(ImportWorker.ExtractDefinitionName(json));
    }

    [Fact]
    public void ExtractDefinitionName_BareJsonArray_ReturnsNull()
    {
        // Not an ImportEnvelope object at all (Open Decision #14) — the same "not a recognized envelope
        // shape" case ImportNodeWalker.ParseRecords rejects.
        Assert.Null(ImportWorker.ExtractDefinitionName("[]"));
    }

    [Fact]
    public void ExtractDefinitionName_MalformedJson_ReturnsNullInsteadOfThrowing()
    {
        Assert.Null(ImportWorker.ExtractDefinitionName("{ not valid json"));
    }

    [Fact]
    public void ExtractDefinitionName_JsonNodeOverload_MirrorsStringOverload()
    {
        var node = JsonNode.Parse("""{ "definition": "Widget Import" }""");

        Assert.Equal("Widget Import", ImportWorker.ExtractDefinitionName(node));
        Assert.Null(ImportWorker.ExtractDefinitionName((JsonNode?)null));
    }

    [Theory]
    [InlineData(ImportRunStatus.PendingReview, "already-staged duplicate")]
    [InlineData(ImportRunStatus.Released, "already-released duplicate")]
    [InlineData(ImportRunStatus.Rejected, "rejected duplicate")]
    [InlineData(ImportRunStatus.Failed, "duplicate")]
    public void ClassifyDuplicate_MapsEveryStatusToItsClassification(string status, string expected)
    {
        Assert.Equal(expected, ImportWorker.ClassifyDuplicate(status));
    }
}
