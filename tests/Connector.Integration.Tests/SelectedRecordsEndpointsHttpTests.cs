using System.Net;
using System.Net.Http.Json;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Connector.Integration.Tests;

/// <summary>Selected-record HTTP input validation without requiring a live ERP database.</summary>
[Collection(ApiCollection.Name)]
public sealed class SelectedRecordsEndpointsHttpTests(ApiFactory factory)
{
    private async Task<int> CreateExportDefinitionAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        var root = new ExportNode(
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
        );
        var definition = new ExportDefinitionEntity
        {
            Name = "Selected records HTTP " + Guid.NewGuid().ToString("N"),
            RootTable = "systemconfiguration",
            RootNode = ExportNodeJson.Serialize(root),
            OutputFormat = "json",
            IsEnabled = true,
            CorrelationKeySourceField = "id",
            ConfigVersion = 1,
            CreatedBy = "alice",
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
        db.ExportDefinitions.Add(definition);
        await db.SaveChangesAsync();
        return definition.Id;
    }

    [Fact]
    public async Task SelectedExport_RejectsEmptyDuplicateAndOverlongIds()
    {
        var id = await CreateExportDefinitionAsync();
        using var client = await factory.CreateAuthenticatedClientAsync();

        foreach (var invalid in new[]
        {
            Array.Empty<string>(),
            new[] { "a", "a" },
            new[] { "" },
            new[] { new string('x', 257) },
        })
        {
            var response = await client.PostAsJsonAsync(
                $"/api/export-definitions/{id}/run-selected", new { recordIds = invalid }
            );
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task SelectedImport_RejectsEmptyAndNonObjectRecords()
    {
        using var client = await factory.CreateAuthenticatedClientAsync();
        var missingId = int.MaxValue;
        var empty = await client.PostAsJsonAsync(
            $"/api/import-definitions/{missingId}/records", new { records = Array.Empty<object>() }
        );
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var notObject = await client.PostAsJsonAsync(
            $"/api/import-definitions/{missingId}/records", new { records = new object[] { "not-an-object" } }
        );
        Assert.Equal(HttpStatusCode.BadRequest, notObject.StatusCode);

        var unknownDefinition = await client.PostAsJsonAsync(
            $"/api/import-definitions/{missingId}/records",
            new { records = new[] { new { guid = Guid.NewGuid().ToString() } } }
        );
        Assert.Equal(HttpStatusCode.NotFound, unknownDefinition.StatusCode);
    }
}
