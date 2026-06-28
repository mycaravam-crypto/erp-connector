using Connector.Core.Schema;

namespace Connector.Core.Tests;

/// <summary>
/// Verhindert unabsichtliche Schema-Änderungen ohne Version-Bump.
/// Wer eine Spalte hinzufügt oder umbenennt, muss auch <see cref="ExportSchema.Version"/> anpassen.
/// </summary>
public sealed class ExportSchemaTests
{
    [Fact]
    public void Schema_HasExpectedColumns()
    {
        // Snapshot der erwarteten Spalten für Schema-Version 1.0.
        // Änderungen hier = ICD-Änderung = Abstimmung mit Hersteller erforderlich.
        var expected = new[]
        {
            "serial_number",
            "part_number",
            "parent_serial_number",
            "model_reference",
            "commissioning_date",
            "maintenance_state",
        };

        Assert.Equal(expected, ExportSchema.Columns);
    }

    [Fact]
    public void Schema_Version_Is_1_0()
    {
        Assert.Equal("1.0", ExportSchema.Version);
    }

    [Fact]
    public void BuildFileName_FormatsCorrectly()
    {
        var at = new DateTimeOffset(2026, 6, 28, 6, 0, 0, TimeSpan.Zero);

        var name = ExportSchema.BuildFileName(42, at);

        Assert.Equal("export_0042_20260628T060000Z.xlsx", name);
    }

    [Fact]
    public void BuildManifestFileName_ReplacesExtension()
    {
        var manifest = ExportSchema.BuildManifestFileName("export_0042_20260628T060000Z.xlsx");

        Assert.Equal("export_0042_20260628T060000Z.manifest.json", manifest);
    }
}
