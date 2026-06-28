using Connector.Core.Domain;
using Connector.Export;

namespace Connector.Core.Tests;

public sealed class DataMinimizerTests
{
    private readonly DataMinimizer _sut = new();

    [Fact]
    public void Minimize_StripsTechnicianName()
    {
        var item = MakeItem(technicianName: "Hans Mustermann");

        var result = _sut.Minimize(item);

        // ExportItem hat kein TechnicianName-Feld — der Typ selbst ist der Beweis der Minimierung.
        Assert.IsType<ExportItem>(result);
    }

    [Fact]
    public void Minimize_PreservesGuid()
    {
        var item = MakeItem(guid: "sc-rack-0001");

        var result = _sut.Minimize(item);

        Assert.Equal("sc-rack-0001", result.Guid);
    }

    [Fact]
    public void Minimize_PreservesSerialNumber()
    {
        var item = MakeItem(serial: "SN-12345");

        var result = _sut.Minimize(item);

        Assert.Equal("SN-12345", result.SerialNumber);
    }

    [Fact]
    public void Minimize_NullSerial_RemainsNull()
    {
        // Missing serial is allowed — it does not block the export.
        var item = MakeItem(serial: null);

        var result = _sut.Minimize(item);

        Assert.Null(result.SerialNumber);
    }

    [Fact]
    public void Minimize_PreservesParentSerialNumber()
    {
        var item = MakeItem(serial: "SN-CHILD", parent: "SN-PARENT");

        var result = _sut.Minimize(item);

        Assert.Equal("SN-PARENT", result.ParentSerialNumber);
    }

    [Fact]
    public void Minimize_NullParent_RemainsNull()
    {
        var item = MakeItem(serial: "SN-ROOT", parent: null);

        var result = _sut.Minimize(item);

        Assert.Null(result.ParentSerialNumber);
    }

    private static ErpConfigurationItem MakeItem(
        string guid = "sc-001",
        string? serial = "SN-001",
        string? parent = null,
        string technicianName = "Erika Mustermann"
    ) =>
        new(
            Guid: guid,
            SerialNumber: serial,
            PartNumber: "P-001",
            ParentSerialNumber: parent,
            ModelReference: "MODEL-A",
            CommissioningDate: new DateOnly(2024, 1, 15),
            MaintenanceState: "InBetrieb",
            TechnicianName: technicianName,
            StorageLocation: "Halle 2"
        );
}
