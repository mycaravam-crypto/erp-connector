using System.Security.Cryptography;
using System.Text.Json;
using Connector.Core.Domain;
using Connector.Erp.DemoErp;
using Connector.Export;
using Connector.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Connector.Integration.Tests;

/// <summary>
/// Vollständige Pipeline-Integration gegen Demo-ERP-Datenbank und echtes Dateisystem-Staging.
/// Testet den Pfad: DemoErpReader → Filter → Minimizer → Mapper → Packager → FileSystemSink.
/// </summary>
public sealed class PipelineIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DemoErpDbContext _erpDb;
    private readonly string _stagingDir;

    private readonly DemoErpReader _reader;
    private readonly ExportFilter _filter;
    private readonly DataMinimizer _minimizer;
    private readonly SchemaMapper _mapper;
    private readonly ExcelPackager _packager;
    private readonly FileSystemExportSink _sink;

    public PipelineIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<DemoErpDbContext>()
            .UseSqlite(_connection)
            .Options;
        _erpDb = new DemoErpDbContext(dbOptions);
        _erpDb.Database.EnsureCreated();
        DemoErpSeed.Seed(_erpDb);

        _stagingDir = Path.Combine(Path.GetTempPath(), $"connector-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_stagingDir);

        _reader   = new DemoErpReader(_erpDb, NullLogger<DemoErpReader>.Instance);
        _filter   = new ExportFilter(NullLogger<ExportFilter>.Instance);
        _minimizer = new DataMinimizer();
        _mapper   = new SchemaMapper();
        _packager = new ExcelPackager();
        _sink     = new FileSystemExportSink(
            Options.Create(new ExportSinkOptions { StagingPath = _stagingDir }),
            NullLogger<FileSystemExportSink>.Instance);
    }

    [Fact]
    public async Task FullPipeline_ProducesCorrectRecordCount()
    {
        var package = await RunPipelineAsync(sequenceNumber: 1);

        Assert.Equal(5, package.Manifest.RecordCount);
    }

    [Fact]
    public async Task FullPipeline_ManifestChecksumMatchesFileBytes()
    {
        var package = await RunPipelineAsync(sequenceNumber: 1);

        var computed = Convert.ToHexString(SHA256.HashData(package.DataFileBytes))
            .ToLowerInvariant();

        Assert.Equal(computed, package.Manifest.Sha256Checksum);
    }

    [Fact]
    public async Task FullPipeline_ManifestSchemaVersionIs1_0()
    {
        var package = await RunPipelineAsync(sequenceNumber: 1);

        Assert.Equal("1.0", package.Manifest.SchemaVersion);
    }

    [Fact]
    public async Task FullPipeline_SequenceNumberAppearsInFileName()
    {
        var package = await RunPipelineAsync(sequenceNumber: 42);

        Assert.StartsWith("export_0042_", package.DataFileName);
        Assert.EndsWith(".xlsx", package.DataFileName);
    }

    [Fact]
    public async Task FullPipeline_SinkWritesBothFiles()
    {
        var package = await RunPipelineAsync(sequenceNumber: 3);
        await _sink.WriteAsync(package, CancellationToken.None);

        var dataFile     = Path.Combine(_stagingDir, package.DataFileName);
        var manifestFile = Path.Combine(_stagingDir, package.DataFileName.Replace(".xlsx", ".manifest.json"));

        Assert.True(File.Exists(dataFile),     $"Datendatei fehlt: {dataFile}");
        Assert.True(File.Exists(manifestFile), $"Manifest fehlt:   {manifestFile}");
    }

    [Fact]
    public async Task FullPipeline_ManifestJson_IsValidJson()
    {
        var package = await RunPipelineAsync(sequenceNumber: 4);
        await _sink.WriteAsync(package, CancellationToken.None);

        var manifestFile = Path.Combine(_stagingDir, package.DataFileName.Replace(".xlsx", ".manifest.json"));
        var json = await File.ReadAllTextAsync(manifestFile);

        // Wirft bei ungültigem JSON
        var doc = JsonDocument.Parse(json);
        Assert.Equal(5, doc.RootElement.GetProperty("RecordCount").GetInt32());
    }

    [Fact]
    public async Task FullPipeline_ExportedRecords_ExcludePersonalData()
    {
        // DataMinimizer entfernt TechnicianName — kein personenbezogenes Feld darf in MappedExportRecord erscheinen.
        var mapped = await RunPipelineToMappedAsync();

        // MappedExportRecord hat kein TechnicianName-Feld — das ist die statische Garantie.
        // Hier prüfen wir, dass alle fünf in-scope CIs im Ergebnis erscheinen.
        Assert.Equal(5, mapped.Count);
        Assert.All(mapped, r => Assert.False(string.IsNullOrEmpty(r.SerialNumber)));
    }

    [Fact]
    public async Task FullPipeline_ParentChildRelationship_IsPreserved()
    {
        var mapped = await RunPipelineToMappedAsync();

        var rack  = mapped.Single(r => r.SerialNumber == DemoErpSeed.Ids.SnRack1);
        var blade = mapped.Single(r => r.SerialNumber == DemoErpSeed.Ids.SnBlade1);

        Assert.Null(rack.ParentSerialNumber);
        Assert.Equal(DemoErpSeed.Ids.SnRack1, blade.ParentSerialNumber);
    }

    [Fact]
    public async Task FullPipeline_CommissioningDate_IsIso8601()
    {
        var mapped = await RunPipelineToMappedAsync();

        var rack = mapped.Single(r => r.SerialNumber == DemoErpSeed.Ids.SnRack1);

        Assert.Equal("2023-03-01", rack.CommissioningDateIso8601);
    }

    [Fact]
    public async Task FullPipeline_SerialNumber_PreservedExactly()
    {
        // Kritisch: Seriennummern dürfen nicht durch Excel-Autokonvertierung korrumpiert werden.
        // Dieser Test prüft den Wert vor dem Schreiben — der Excel-Formatter schreibt ihn als Text.
        var mapped = await RunPipelineToMappedAsync();

        Assert.Contains(mapped, r => r.SerialNumber == "SN-RACK-0001");
        Assert.Contains(mapped, r => r.SerialNumber == "SN-BLD-0001");
        Assert.Contains(mapped, r => r.SerialNumber == "SN-SW-0001");
    }

    [Fact]
    public async Task Sink_Throws_WhenStagingDirectoryMissing()
    {
        var missingSink = new FileSystemExportSink(
            Options.Create(new ExportSinkOptions { StagingPath = "/nonexistent/path/xyz" }),
            NullLogger<FileSystemExportSink>.Instance);

        var package = await RunPipelineAsync(sequenceNumber: 99);

        await Assert.ThrowsAsync<ExportSinkException>(
            () => missingSink.WriteAsync(package, CancellationToken.None));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<ExportPackage> RunPipelineAsync(int sequenceNumber)
    {
        var mapped = await RunPipelineToMappedAsync();
        return await _packager.PackageAsync(mapped, sequenceNumber, CancellationToken.None);
    }

    private async Task<IReadOnlyList<MappedExportRecord>> RunPipelineToMappedAsync()
    {
        var rawItems  = await _reader.ReadMaintainableCIsAsync(CancellationToken.None);
        var filtered  = _filter.Filter(rawItems);
        var minimized = filtered.Select(_minimizer.Minimize).ToList();
        return minimized.Select(_mapper.Map).ToList();
    }

    public void Dispose()
    {
        _erpDb.Dispose();
        _connection.Dispose();
        try { Directory.Delete(_stagingDir, recursive: true); } catch { /* best-effort cleanup */ }
    }
}
