using System.Text.Json;
using Connector.Core.Domain;
using Connector.Core.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Connector.Infrastructure;

/// <summary>
/// Writes an export package (data file + manifest JSON) atomically to the configured staging path.
/// </summary>
/// <remarks>
/// "Atomically" here means: the data file is first written to a .tmp file, then renamed — so the
/// gateway never sees a partially written file. The manifest is only written once the data file is
/// complete.
/// </remarks>
public sealed class FileSystemExportSink(IOptions<ExportSinkOptions> options, ILogger<FileSystemExportSink> logger)
{
    private readonly string _stagingPath = options.Value.StagingPath;

    public async Task WriteAsync(ExportPackage package, CancellationToken ct)
    {
        if (!Directory.Exists(_stagingPath))
            throw new ExportSinkException($"Staging path does not exist: {_stagingPath}");

        var dataFilePath = Path.Combine(_stagingPath, package.DataFileName);
        var tmpFilePath = dataFilePath + ".tmp";
        var manifestPath = Path.Combine(_stagingPath, ExportSchema.BuildManifestFileName(package.DataFileName));

        try
        {
            // Write to .tmp first, then rename — the gateway never sees a partially written file.
            await File.WriteAllBytesAsync(tmpFilePath, package.DataFileBytes, ct);
            File.Move(tmpFilePath, dataFilePath, overwrite: false);

            var manifestJson = JsonSerializer.Serialize(package.Manifest, ManifestJsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

            logger.LogInformation(
                "Export #{Seq} written: {File} ({Bytes} bytes, {Count} records)",
                package.Manifest.SequenceNumber,
                package.DataFileName,
                package.DataFileBytes.Length,
                package.Manifest.RecordCount
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Clean up partial artifacts — the gateway may only ever see complete packages.
            TryDelete(tmpFilePath);
            TryDelete(dataFilePath);
            TryDelete(manifestPath);
            throw new ExportSinkException($"Writing to staging path failed: {ex.Message}", ex);
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cleanup failed: {Path}", path);
        }
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };
}

public sealed class ExportSinkOptions
{
    /// <summary>
    /// Absolute or relative path to the staging directory.
    /// The gateway system needs read access to this path; the service needs write access.
    /// </summary>
    public string StagingPath { get; set; } = string.Empty;
}
