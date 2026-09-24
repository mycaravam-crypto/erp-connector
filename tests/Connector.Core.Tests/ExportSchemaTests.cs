using Connector.Core.Schema;

namespace Connector.Core.Tests;

/// <summary>
/// Guards against unintentional schema changes without a version bump.
/// Adding or renaming a column requires updating <see cref="ExportSchema.Version"/> too.
/// </summary>
public sealed class ExportSchemaTests
{
    [Fact]
    public void Schema_HasExpectedColumns()
    {
        // Snapshot of the expected columns for schema version 2.0.
        // A change here is an ICD change and must be coordinated with the vendor.
        var expected = new[]
        {
            "guid",
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
    public void Schema_Version_Is_2_0()
    {
        Assert.Equal("2.0", ExportSchema.Version);
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
