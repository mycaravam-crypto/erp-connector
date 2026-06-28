using Connector.Core.Domain;
using Connector.Export;

namespace Connector.Core.Tests;

public sealed class SchemaMapperTests
{
    private readonly SchemaMapper _sut = new();

    [Fact]
    public void Map_FormatsDateAsIso8601()
    {
        var item = MakeItem(date: new DateOnly(2024, 3, 7));

        var result = _sut.Map(item);

        Assert.Equal("2024-03-07", result.CommissioningDateIso8601);
    }

    [Fact]
    public void Map_NullDate_ReturnsEmptyString()
    {
        // Leerer String statt null verhindert Excel-Formelfehler bei leeren Zellen.
        var item = MakeItem(date: null);

        var result = _sut.Map(item);

        Assert.Equal(string.Empty, result.CommissioningDateIso8601);
    }

    [Fact]
    public void Map_GuidPreservedAsString()
    {
        // Kritisch: GUID ist der Coalesce-Schlüssel — muss exakt als String erhalten bleiben.
        var item = MakeItem(guid: "sc-rack-0001");

        var result = _sut.Map(item);

        Assert.Equal("sc-rack-0001", result.Guid);
    }

    [Fact]
    public void Map_SerialNumberPreservedAsString()
    {
        // Seriennummern mit führenden Nullen dürfen nicht zu int konvertiert werden.
        var item = MakeItem(serial: "0042-A");

        var result = _sut.Map(item);

        Assert.Equal("0042-A", result.SerialNumber);
    }

    [Fact]
    public void Map_NullSerial_ReturnsEmptyString()
    {
        // Missing serial is allowed — it maps to empty string (not null) to keep Excel cells consistent.
        var item = MakeItem(serial: null);

        var result = _sut.Map(item);

        Assert.Equal(string.Empty, result.SerialNumber);
    }

    [Fact]
    public void Map_EmptyGuid_ThrowsInvalidCorrelationKeyException()
    {
        var item = MakeItem(guid: "");

        Assert.Throws<InvalidCorrelationKeyException>(() => _sut.Map(item));
    }

    private static ExportItem MakeItem(string guid = "sc-001", string? serial = "SN-001", DateOnly? date = null) =>
        new(
            Guid: guid,
            SerialNumber: serial,
            PartNumber: "P-001",
            ParentSerialNumber: null,
            ModelReference: "MODEL-A",
            CommissioningDate: date,
            MaintenanceState: "InBetrieb"
        );
}
