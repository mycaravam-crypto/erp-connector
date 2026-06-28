using Connector.Core.Domain;
using Connector.Export;
using Microsoft.Extensions.Logging.Abstractions;

namespace Connector.Core.Tests;

public sealed class ExportFilterTests
{
    private readonly ExportFilter _sut = new(NullLogger<ExportFilter>.Instance);

    [Fact]
    public void Filter_ExcludesItemsWithoutGuid()
    {
        var items = new[] { MakeItem(guid: null), MakeItem(guid: ""), MakeItem(guid: "  ") };

        var result = _sut.Filter(items);

        Assert.Empty(result);
    }

    [Fact]
    public void Filter_KeepsItemsWithGuid()
    {
        var items = new[] { MakeItem(guid: "sc-rack-0001"), MakeItem(guid: "sc-blade-0001") };

        var result = _sut.Filter(items);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Filter_MixedItems_ReturnsOnlyValid()
    {
        var items = new[] { MakeItem(guid: "sc-rack-0001"), MakeItem(guid: null), MakeItem(guid: "sc-blade-0001") };

        var result = _sut.Filter(items);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.NotNull(r.Guid));
    }

    [Fact]
    public void Filter_AllowsItemsWithMissingSerialNumber()
    {
        // A missing serial does not block the export — only a missing GUID does.
        var items = new[] { MakeItem(guid: "sc-rack-0001", serial: null) };

        var result = _sut.Filter(items);

        Assert.Single(result);
    }

    private static ErpConfigurationItem MakeItem(string? guid = "sc-rack-0001", string? serial = "SN-001") =>
        new(
            Guid: guid,
            SerialNumber: serial,
            PartNumber: "P-001",
            ParentSerialNumber: null,
            ModelReference: "MODEL-A",
            CommissioningDate: null,
            MaintenanceState: null,
            TechnicianName: "Hans Mustermann",
            StorageLocation: "Halle 1"
        );
}
