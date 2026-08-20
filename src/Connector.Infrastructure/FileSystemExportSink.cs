using System.Text.Json;
using Connector.Core.Domain;
using Connector.Core.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Connector.Infrastructure;

/// <summary>
/// Schreibt Export-Paket (Datei + Manifest-JSON) atomar auf den konfigurierten Staging-Pfad.
/// </summary>
/// <remarks>
/// "Atomar" bedeutet hier: Datendatei wird zuerst in eine .tmp-Datei geschrieben,
/// dann umbenannt — so sieht das Gateway nie eine halbfertige Datei.
/// Das Manifest wird erst geschrieben, nachdem die Datendatei vollständig ist.
/// </remarks>
public sealed class FileSystemExportSink(IOptions<ExportSinkOptions> options, ILogger<FileSystemExportSink> logger)
{
    private readonly string _stagingPath = options.Value.StagingPath;

    public async Task WriteAsync(ExportPackage package, CancellationToken ct)
    {
        if (!Directory.Exists(_stagingPath))
            throw new ExportSinkException($"Staging-Pfad existiert nicht: {_stagingPath}");

        var dataFilePath = Path.Combine(_stagingPath, package.DataFileName);
        var tmpFilePath = dataFilePath + ".tmp";
        var manifestPath = Path.Combine(_stagingPath, ExportSchema.BuildManifestFileName(package.DataFileName));

        try
        {
            // Erst .tmp schreiben, dann umbenennen — Gateway sieht keine halbfertige Datei.
            await File.WriteAllBytesAsync(tmpFilePath, package.DataFileBytes, ct);
            File.Move(tmpFilePath, dataFilePath, overwrite: false);

            var manifestJson = JsonSerializer.Serialize(package.Manifest, ManifestJsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, ct);

            logger.LogInformation(
                "Export #{Seq} geschrieben: {File} ({Bytes} Bytes, {Count} Records)",
                package.Manifest.SequenceNumber,
                package.DataFileName,
                package.DataFileBytes.Length,
                package.Manifest.RecordCount
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Halbfertige Artefakte aufräumen — Gateway darf nur vollständige Pakete sehen.
            TryDelete(tmpFilePath);
            TryDelete(dataFilePath);
            TryDelete(manifestPath);
            throw new ExportSinkException($"Schreiben auf Staging-Pfad fehlgeschlagen: {ex.Message}", ex);
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
            logger.LogWarning(ex, "Aufräumen fehlgeschlagen: {Path}", path);
        }
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };
}

public sealed class ExportSinkOptions
{
    /// <summary>
    /// Absoluter oder relativer Pfad zum Staging-Verzeichnis.
    /// Das Gateway-System muss Leserecht auf diesen Pfad haben; der Dienst benötigt Schreibrecht.
    /// </summary>
    public string StagingPath { get; set; } = string.Empty;
}
