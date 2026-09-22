using System.Text;
using System.Text.Json;
using Connector.Core.Domain;
using Connector.Core.Schema;
using Connector.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-filesystem coverage for <see cref="FileSystemExportSink"/>: the atomic write (.tmp then rename,
/// manifest only after the data file is complete) and the cleanup-on-failure path documented on the class.
/// </summary>
public sealed class FileSystemExportSinkTests
{
    private static ExportPackage MakePackage(string dataFileName = "export_0001_20260101T000000Z.json") =>
        new(
            Manifest: new ExportManifest(
                SequenceNumber: 1,
                SchemaVersion: ExportSchema.Version,
                ExtractedAt: DateTimeOffset.UtcNow,
                RecordCount: 2,
                Sha256Checksum: "abc123"
            ),
            DataFileBytes: Encoding.UTF8.GetBytes("""{"records":[]}"""),
            DataFileName: dataFileName
        );

    private static FileSystemExportSink NewSink(string stagingPath) =>
        new(
            Options.Create(new ExportSinkOptions { StagingPath = stagingPath }),
            NullLogger<FileSystemExportSink>.Instance
        );

    [Fact]
    public async Task WriteAsync_ValidPackage_WritesDataFileAndManifestToStagingPath()
    {
        var stagingDir = Directory.CreateTempSubdirectory("export-sink-test-");
        try
        {
            var package = MakePackage();
            var sink = NewSink(stagingDir.FullName);

            await sink.WriteAsync(package, CancellationToken.None);

            var dataPath = Path.Combine(stagingDir.FullName, package.DataFileName);
            var manifestPath = Path.Combine(
                stagingDir.FullName,
                ExportSchema.BuildManifestFileName(package.DataFileName)
            );

            Assert.True(File.Exists(dataPath));
            Assert.Equal(package.DataFileBytes, await File.ReadAllBytesAsync(dataPath));

            Assert.True(File.Exists(manifestPath));
            using var manifestDoc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            Assert.Equal(1, manifestDoc.RootElement.GetProperty("SequenceNumber").GetInt32());
            Assert.Equal("abc123", manifestDoc.RootElement.GetProperty("Sha256Checksum").GetString());

            // No leftover .tmp artifact once the write has completed.
            Assert.False(File.Exists(dataPath + ".tmp"));
        }
        finally
        {
            stagingDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_StagingPathMissing_ThrowsExportSinkExceptionWithoutTouchingDisk()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "export-sink-does-not-exist-" + Guid.NewGuid());
        var sink = NewSink(missingPath);

        var ex = await Assert.ThrowsAsync<ExportSinkException>(() =>
            sink.WriteAsync(MakePackage(), CancellationToken.None)
        );

        Assert.Contains(missingPath, ex.Message);
        Assert.False(Directory.Exists(missingPath));
    }

    [Fact]
    public async Task WriteAsync_DataFileNameAlreadyExists_ThrowsAndCleansUpTheCollidingPath()
    {
        var stagingDir = Directory.CreateTempSubdirectory("export-sink-test-");
        try
        {
            var package = MakePackage();
            var dataPath = Path.Combine(stagingDir.FullName, package.DataFileName);
            await File.WriteAllTextAsync(dataPath, "pre-existing content");

            var sink = NewSink(stagingDir.FullName);

            // Move refuses to overwrite an existing file, so this must surface as a sink failure. The
            // catch block's cleanup isn't scoped to only what this call wrote — it unconditionally removes
            // whatever now sits at dataFilePath, so the colliding file is swept away too, not preserved.
            await Assert.ThrowsAsync<ExportSinkException>(() => sink.WriteAsync(package, CancellationToken.None));

            Assert.False(File.Exists(dataPath));
            Assert.False(File.Exists(dataPath + ".tmp"));
            Assert.False(
                File.Exists(Path.Combine(stagingDir.FullName, ExportSchema.BuildManifestFileName(package.DataFileName)))
            );
        }
        finally
        {
            stagingDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_ManifestWriteFails_RemovesTheDataFileItAlreadyWrote()
    {
        var stagingDir = Directory.CreateTempSubdirectory("export-sink-test-");
        try
        {
            var package = MakePackage();
            var manifestPath = Path.Combine(
                stagingDir.FullName,
                ExportSchema.BuildManifestFileName(package.DataFileName)
            );
            // Force the manifest write to fail after the data file has already been moved into place, by
            // occupying its path with a directory.
            Directory.CreateDirectory(manifestPath);

            var sink = NewSink(stagingDir.FullName);

            await Assert.ThrowsAsync<ExportSinkException>(() => sink.WriteAsync(package, CancellationToken.None));

            var dataPath = Path.Combine(stagingDir.FullName, package.DataFileName);
            Assert.False(File.Exists(dataPath));
            Assert.False(File.Exists(dataPath + ".tmp"));
        }
        finally
        {
            stagingDir.Delete(recursive: true);
        }
    }
}
