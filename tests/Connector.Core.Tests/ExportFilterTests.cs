using Connector.Core.Domain;
using Connector.Export;
using Microsoft.Extensions.Logging.Abstractions;

namespace Connector.Core.Tests;

public sealed class ExportFilterTests
{
    private readonly ExportFilter _sut = new(NullLogger<ExportFilter>.Instance);

    [Fact]
    public void Filter_ExcludesItemsWithoutSerialNumber()
    {
        var items = new[]
        {
            MakeItem(serial: null),
            MakeItem(serial: ""),
            MakeItem(serial: "  "),
        };

        var result = _sut.Filter(items);

        Assert.Empty(result);
    }

    [Fact]
    public void Filter_KeepsItemsWithSerialNumber()
    {
        var items = new[]
        {
            MakeItem(serial: "SN-001"),
            MakeItem(serial: "SN-002"),
        };

        var result = _sut.Filter(items);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Filter_MixedItems_ReturnsOnlyValid()
    {
        var items = new[]
        {
            MakeItem(serial: "SN-001"),
            MakeItem(serial: null),
            MakeItem(serial: "SN-003"),
        };

        var result = _sut.Filter(items);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.NotNull(r.SerialNumber));
    }

    private static ErpConfigurationItem MakeItem(string? serial) => new(
        SerialNumber: serial,
        PartNumber: "P-001",
        ParentSerialNumber: null,
        ModelReference: "MODEL-A",
        CommissioningDate: null,
        MaintenanceState: null,
        TechnicianName: "Hans Mustermann",
        StorageLocation: "Halle 1");
}
