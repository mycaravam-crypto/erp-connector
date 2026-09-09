using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Connector.Core.Domain;
using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;
using Connector.Core.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Connector.Infrastructure;

/// <summary>
/// Slice 4 of Phase 17 (import-definitions.md §3 steps 1-2, §5): a sibling of <see cref="ExportWorker"/>,
/// polling an <c>inbound/</c> staging folder instead of writing to one. Every vendor-supplied file dropped
/// there is manifest-checked, idempotency-checked against <c>(ImportDefinitionId, Sha256Checksum)</c> (Open
/// Decision #13), walked by <see cref="ImportNodeWalker"/> (Slice 2) against the <c>ImportEnvelope</c>
/// (Open Decision #14) — the <c>definition</c> field says which saved <see cref="ImportDefinitionEntity"/> to
/// use — and staged as an <see cref="ImportRunEntity"/> at <see cref="ImportRunStatus.PendingReview"/>, with
/// <see cref="ImportDefinitionEntity"/> frozen onto it as <see cref="ImportRunEntity.DefinitionSnapshotJson"/>
/// (Open Decision #10). This worker never writes to the ERP itself — that's <see cref="ImportRunReleaser"/>
/// (Slice 3)'s job, once a human approves the staged plan.
/// </summary>
public sealed class ImportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ImportSinkOptions> sinkOptions,
    IOptions<ImportWorkerOptions> options,
    ILogger<ImportWorker> logger
) : BackgroundService
{
    /// <summary>Fixed <see cref="ImportRunEntity.TriggeredBy"/> marker for a run staged by this worker,
    /// distinguishing it from a username on a manually-triggered run.</summary>
    public const string TriggeredBy = "watcher";

    private const string ManifestSuffix = ".manifest.json";

    private readonly string _inboundPath = sinkOptions.Value.InboundPath;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "ImportWorker started. Polling {Path} every {Interval}",
            _inboundPath,
            options.Value.PollInterval
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            await PollOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(options.Value.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>One polling pass over <see cref="_inboundPath"/> — public so a test can drive it directly
    /// against a real inbound directory and testdb, without waiting out the timer loop. Never throws: a
    /// broken file or a scope/DB failure is logged and the rest of the pass continues, matching the
    /// <see cref="ExportWorker"/>/<see cref="ExportDefinitionWorker"/> resilience convention.</summary>
    public async Task PollOnceAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_inboundPath))
            return;

        var processedDir = Path.Combine(_inboundPath, "processed");
        var rejectedDir = Path.Combine(_inboundPath, "rejected");

        List<string> dataFiles;
        try
        {
            Directory.CreateDirectory(processedDir);
            Directory.CreateDirectory(rejectedDir);

            dataFiles =
            [
                .. Directory
                    .EnumerateFiles(_inboundPath)
                    .Where(f => !f.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.Ordinal),
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ImportWorker: failed to prepare/enumerate {Path}", _inboundPath);
            return;
        }

        foreach (var dataFile in dataFiles)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                await ProcessFileAsync(dataFile, processedDir, rejectedDir, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ImportWorker: unhandled error processing {File}", dataFile);
            }
        }
    }

    private async Task ProcessFileAsync(
        string dataFilePath,
        string processedDir,
        string rejectedDir,
        CancellationToken ct
    )
    {
        var fileName = Path.GetFileName(dataFilePath);
        var manifestPath = Path.Combine(_inboundPath, ExportSchema.BuildManifestFileName(fileName));

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

        async Task RejectAsync(string reason)
        {
            logger.LogWarning("ImportWorker: quarantining {File}: {Reason}", fileName, reason);
            await audit.LogAsync(TriggeredBy, "import_file_rejected", $"{fileName}: {reason}");
            MoveFiles(dataFilePath, manifestPath, rejectedDir);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(dataFilePath, ct);
        }
        catch (IOException)
        {
            // Most likely still being written (e.g. mid-copy from removable media) — leave it for the next
            // poll instead of quarantining a file that will be complete a moment later.
            return;
        }

        if (!File.Exists(manifestPath))
        {
            await RejectAsync("no accompanying manifest file");
            return;
        }

        ImportManifest? manifest;
        try
        {
            var manifestJson = await File.ReadAllTextAsync(manifestPath, ct);
            manifest = JsonSerializer.Deserialize<ImportManifest>(
                manifestJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch (JsonException ex)
        {
            await RejectAsync($"manifest is not valid JSON: {ex.Message}");
            return;
        }

        if (manifest is null || string.IsNullOrWhiteSpace(manifest.Sha256Checksum))
        {
            await RejectAsync("manifest is missing Sha256Checksum");
            return;
        }

        var actualChecksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actualChecksum, manifest.Sha256Checksum, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(
                $"checksum mismatch: file={actualChecksum} manifest={manifest.Sha256Checksum.ToLowerInvariant()}"
            );
            return;
        }

        var inboundJson = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');

        JsonNode? envelopeNode;
        try
        {
            envelopeNode = JsonNode.Parse(inboundJson);
        }
        catch (JsonException ex)
        {
            await RejectAsync($"file is not valid JSON: {ex.Message}");
            return;
        }

        var definitionName = ExtractDefinitionName(envelopeNode);
        if (definitionName is null)
        {
            await RejectAsync(
                "ImportEnvelope is missing a \"definition\" property naming which saved import definition "
                    + "this file targets (Open Decision #14)."
            );
            return;
        }

        var definition = await db.ImportDefinitions.FirstOrDefaultAsync(
            d => d.Name == definitionName && d.IsEnabled,
            ct
        );
        if (definition is null)
        {
            await RejectAsync($"no enabled ImportDefinition named '{definitionName}'");
            return;
        }

        var existing = await db.ImportRuns.FirstOrDefaultAsync(
            r => r.ImportDefinitionId == definition.Id && r.Sha256Checksum == actualChecksum,
            ct
        );
        if (existing is not null)
        {
            var classification = ClassifyDuplicate(existing.Status);
            logger.LogInformation(
                "ImportWorker: {File} is a {Classification} of run #{RunId} — not staged again",
                fileName,
                classification,
                existing.Id
            );
            await audit.LogAsync(
                TriggeredBy,
                "import_duplicate_detected",
                $"{fileName}: {classification} (run #{existing.Id})"
            );
            // Not a new run, but still a handled file — move it aside so it isn't rescanned every poll.
            MoveFiles(dataFilePath, manifestPath, processedDir);
            return;
        }

        var root = ImportNodeJson.Deserialize(definition.RootNode);
        if (root is null)
        {
            await RejectAsync($"ImportDefinition '{definition.Name}' has an unreadable RootNode");
            return;
        }

        var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
        if (connRaw is null)
        {
            await RejectAsync("no ERP connection configured");
            return;
        }
        var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw);
        if (connCfg is null)
        {
            await RejectAsync("stored ERP connection config could not be read");
            return;
        }

        ImportWalkResult walkResult;
        try
        {
            await using var conn = new NpgsqlConnection(DynamicExportService.BuildConnectionString(connCfg));
            await conn.OpenAsync(ct);
            walkResult = await ImportNodeWalker.WalkAsync(conn, definition, root, inboundJson, ct);
        }
        catch (ImportValidationException ex)
        {
            await RejectAsync(ex.Message);
            return;
        }

        var plan = ImportPlanBuilder.Build(walkResult, definition.RootTable, definition.RootMatchColumn);

        var run = new ImportRunEntity
        {
            ImportDefinitionId = definition.Id,
            ConfigVersion = definition.ConfigVersion,
            DefinitionSnapshotJson = JsonSerializer.Serialize(definition),
            SourceFileName = fileName,
            Sha256Checksum = actualChecksum,
            StartedAt = DateTimeOffset.UtcNow.ToString("O"),
            // FinishedAt stays null: the run's lifecycle only ends when ImportRunReleaser moves it out of
            // PendingReview (Released/Rejected/Failed), not when staging itself completes.
            Status = ImportRunStatus.PendingReview,
            TriggeredBy = TriggeredBy,
            RecordCount = plan.RecordCount,
            MatchedCount = plan.MatchedCount,
            ChangedCount = plan.ChangedCount,
            UnchangedCount = plan.UnchangedCount,
            RejectedCount = plan.RejectedCount,
            InvalidCount = plan.InvalidCount,
            PlanJson = ImportPlanJson.Serialize(plan),
            StagedConnectionFingerprint = DynamicExportService.ConnectionFingerprint(connCfg),
        };

        try
        {
            db.ImportRuns.Add(run);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // A concurrent staging of this exact (ImportDefinitionId, Sha256Checksum) pair between the
            // pre-check above and this insert — the unique constraint (Open Decision #13) catches it.
            // Treated the same as the pre-check duplicate case rather than crashing the poll.
            logger.LogInformation(
                ex,
                "ImportWorker: {File} lost a race to stage the same (definition, checksum) pair — treating as duplicate",
                fileName
            );
            await audit.LogAsync(TriggeredBy, "import_duplicate_detected", $"{fileName}: duplicate (race)");
            MoveFiles(dataFilePath, manifestPath, processedDir);
            return;
        }

        await audit.LogAsync(
            TriggeredBy,
            "import_run_staged",
            $"id={run.Id} definition={definition.Name} matched={run.MatchedCount} changed={run.ChangedCount} "
                + $"unchanged={run.UnchangedCount} rejected={run.RejectedCount} invalid={run.InvalidCount}"
        );
        logger.LogInformation(
            "ImportWorker: staged run #{RunId} from {File} ({Matched} matched, {Changed} changed, {Rejected} rejected)",
            run.Id,
            fileName,
            run.MatchedCount,
            run.ChangedCount,
            run.RejectedCount
        );

        MoveFiles(dataFilePath, manifestPath, processedDir);
    }

    /// <summary>Reads the <c>definition</c> property off the top-level <c>ImportEnvelope</c> object (Open
    /// Decision #14) — the only envelope field this worker needs beyond what <see cref="ImportNodeWalker"/>
    /// already reads (<c>schemaVersion</c>/<c>records</c>) — without otherwise validating the envelope's
    /// shape; <see cref="ImportNodeWalker.WalkAsync"/> is what actually enforces <c>schemaVersion</c> and
    /// <c>records</c>. Pure and DB-free so it's unit-testable without a database or filesystem. Returns null
    /// for anything that isn't a well-formed envelope with a non-blank string <c>definition</c>.</summary>
    public static string? ExtractDefinitionName(JsonNode? envelopeNode)
    {
        if (
            envelopeNode is not JsonObject envelope
            || !envelope.TryGetPropertyValue("definition", out var definitionNode)
            || definitionNode is not JsonValue value
            || !value.TryGetValue<string>(out var name)
            || string.IsNullOrWhiteSpace(name)
        )
            return null;

        return name;
    }

    /// <summary>Convenience overload for callers that haven't already parsed the file — returns null for
    /// invalid JSON the same as for a well-formed-but-definition-less envelope, since both are "can't route
    /// this file" from the caller's point of view.</summary>
    public static string? ExtractDefinitionName(string inboundJson)
    {
        try
        {
            return ExtractDefinitionName(JsonNode.Parse(inboundJson));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Maps an existing <see cref="ImportRunEntity"/>'s <see cref="ImportRunStatus"/> to the
    /// human-readable duplicate classification Open Decision #13 calls for. Pure so it's unit-testable
    /// without a database.</summary>
    public static string ClassifyDuplicate(string existingRunStatus) =>
        existingRunStatus switch
        {
            ImportRunStatus.PendingReview => "already-staged duplicate",
            ImportRunStatus.Released => "already-released duplicate",
            ImportRunStatus.Rejected => "rejected duplicate",
            _ => "duplicate",
        };

    private void MoveFiles(string dataFilePath, string manifestPath, string destDir)
    {
        try
        {
            File.Move(
                dataFilePath,
                UniquePath(Path.Combine(destDir, Path.GetFileName(dataFilePath))),
                overwrite: false
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ImportWorker: failed to move {File} to {Dest}", dataFilePath, destDir);
        }

        if (!File.Exists(manifestPath))
            return;

        try
        {
            File.Move(
                manifestPath,
                UniquePath(Path.Combine(destDir, Path.GetFileName(manifestPath))),
                overwrite: false
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ImportWorker: failed to move {File} to {Dest}", manifestPath, destDir);
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var i = 1;
        while (true)
        {
            var candidate = Path.Combine(dir, $"{stem}.{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
            i++;
        }
    }
}

public sealed class ImportSinkOptions
{
    /// <summary>Absolute or relative path to the inbound staging directory <see cref="ImportWorker"/>
    /// polls. <c>processed/</c> and <c>rejected/</c> subfolders are created under it on demand.</summary>
    public string InboundPath { get; set; } = string.Empty;
}

public sealed class ImportWorkerOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
}
