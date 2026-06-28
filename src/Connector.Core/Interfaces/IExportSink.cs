using Connector.Core.Domain;

namespace Connector.Core.Interfaces;

/// <summary>
/// Übergibt Datei und Manifest an den Staging-Pfad für die Vier-Augen-Freigabe.
/// </summary>
/// <remarks>
/// Die Schreib-Operation muss atomar erscheinen: erst wenn Daten- und Manifest-Datei
/// vollständig geschrieben sind, gilt der Export als bereit. Halbfertige Zustände dürfen
/// nicht freigegeben werden können.
/// </remarks>
public interface IExportSink
{
    /// <summary>
    /// Schreibt <see cref="ExportPackage"/> auf den Staging-Pfad.
    /// </summary>
    /// <exception cref="ExportSinkException">Wenn der Pfad nicht beschreibbar ist.</exception>
    Task WriteAsync(ExportPackage package, CancellationToken ct);
}
