using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Connector.Api;
using Connector.Core.Domain;
using Connector.Core.Interfaces;
using Connector.Core.Schema;
using Connector.Erp.DemoErp;
using Connector.Export;
using Connector.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ── Auth ──────────────────────────────────────────────────────────────────────

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

var jwtSecret =
    builder.Configuration["Auth:JwtSecret"] ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        }
    );

builder.Services.AddAuthorization();

// ── Pipeline implementations ──────────────────────────────────────────────────

builder.Services.AddSingleton<IExportFilter, ExportFilter>();
builder.Services.AddSingleton<IDataMinimizer, DataMinimizer>();
builder.Services.AddSingleton<ISchemaMapper, SchemaMapper>();
builder.Services.AddSingleton<IPackager, ExcelPackager>();

// ── Infrastructure ────────────────────────────────────────────────────────────

builder.Services.Configure<ExportSinkOptions>(builder.Configuration.GetSection("ExportSink"));
builder.Services.Configure<ExportWorkerOptions>(builder.Configuration.GetSection("ExportWorker"));
builder.Services.AddSingleton<IExportSink, FileSystemExportSink>();

builder.Services.AddDbContext<ExportLogDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("ExportLog"))
);

builder.Services.AddHostedService<ExportWorker>();

// Demo-ERP-Datenbank (SQLite). Für Produktion: DemoErpReader durch echten PostgreSQL-Reader ersetzen.
builder.Services.AddDbContext<DemoErpDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("DemoErp"))
);
builder.Services.AddScoped<IErpReader, DemoErpReader>();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Demo-ERP beim Start initialisieren (idempotent).
using (var scope = app.Services.CreateScope())
{
    var erpDb = scope.ServiceProvider.GetRequiredService<DemoErpDbContext>();
    await erpDb.Database.EnsureCreatedAsync();
    DemoErpSeed.Seed(erpDb);

    var exportLogDb = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
    await exportLogDb.Database.EnsureCreatedAsync();

    // Additive schema migrations — safe to run on every start; no-op if column/table already exists.
    // ExecuteSqlRawAsync processes one statement at a time, so each DDL is a separate call.
    var existingColumns = await exportLogDb.Database
        .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('ExportRun')")
        .ToHashSetAsync();
    foreach (var (col, def) in new[]
    {
        ("DeliveredAt",          "ALTER TABLE ExportRun ADD COLUMN DeliveredAt TEXT"),
        ("DeliveredBy",          "ALTER TABLE ExportRun ADD COLUMN DeliveredBy TEXT"),
        ("ImportedRecordCount",  "ALTER TABLE ExportRun ADD COLUMN ImportedRecordCount INTEGER"),
        ("DeliveryNotes",        "ALTER TABLE ExportRun ADD COLUMN DeliveryNotes TEXT"),
    })
    {
        if (!existingColumns.Contains(col))
            await exportLogDb.Database.ExecuteSqlRawAsync(def);
    }
    await exportLogDb.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE IF NOT EXISTS AppSetting (
            Key TEXT PRIMARY KEY NOT NULL,
            Value TEXT NOT NULL
        )
        """);
}

// ── User store ────────────────────────────────────────────────────────────────
// Development: hard-coded seed (alice/alice123, bob/bob123).
// Production: BCrypt hashes from Auth:Users in appsettings.json.

Dictionary<string, string> userStore;
if (app.Environment.IsDevelopment())
{
    userStore = DevAuthSeed.CreateUsers();
    app.Logger.LogInformation("Dev auth: users alice/alice123 and bob/bob123 are active.");
}
else
{
    var authUsers = app.Configuration.GetSection("Auth:Users").Get<List<AuthUser>>() ?? [];
    userStore = authUsers.ToDictionary(u => u.Username, u => u.PasswordHash, StringComparer.OrdinalIgnoreCase);
}

// ── Health check (no auth — used by monitoring and Step 1 connection test) ────

app.MapGet(
    "/api/health",
    async (DemoErpDbContext erpDb, ExportLogDbContext logDb, IOptions<ExportSinkOptions> sinkOpts) =>
    {
        var staging = sinkOpts.Value.StagingPath;
        var stagingOk = Directory.Exists(staging) && IsStagingWritable(staging);
        var erpOk = false;
        var logOk = false;

        try
        {
            erpOk = await erpDb.Database.CanConnectAsync();
        }
        catch
        { /* degraded */
        }
        try
        {
            logOk = await logDb.Database.CanConnectAsync();
        }
        catch
        { /* degraded */
        }

        var checks = new
        {
            erp_db = erpOk,
            log_db = logOk,
            staging = stagingOk,
        };
        var healthy = erpOk && logOk && stagingOk;
        var result = new { status = healthy ? "healthy" : "degraded", checks };
        return healthy ? Results.Ok(result) : Results.Json(result, statusCode: 503);
    }
);

// Write a temp file to confirm the staging directory is writable, not just readable.
bool IsStagingWritable(string path)
{
    try
    {
        var probe = Path.Combine(path, ".health_probe");
        File.WriteAllText(probe, "");
        File.Delete(probe);
        return true;
    }
    catch
    {
        return false;
    }
}

// ── Auth endpoints ────────────────────────────────────────────────────────────

app.MapPost(
    "/api/auth/login",
    (LoginRequest req) =>
    {
        if (
            string.IsNullOrWhiteSpace(req.Username)
            || !userStore.TryGetValue(req.Username, out var hash)
            || !BCrypt.Net.BCrypt.Verify(req.Password ?? "", hash)
        )
            return Results.Unauthorized();

        var expiry = app.Configuration.GetValue<int>("Auth:JwtExpiryHours", defaultValue: 8);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var token = new JwtSecurityToken(
            claims: [new Claim(ClaimTypes.Name, req.Username)],
            expires: DateTime.UtcNow.AddHours(expiry),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );
        return Results.Ok(new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), req.Username));
    }
);

// Dev-only: returns a BCrypt hash for a plaintext password (to seed appsettings for production users).
if (app.Environment.IsDevelopment())
{
    app.MapPost(
        "/api/auth/hash",
        (HashRequest req) => Results.Ok(new { Hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 11) })
    );
}

// ── Export API (all routes require a valid JWT) ───────────────────────────────

// Returns the run list with an IsStale flag so the UI can warn about long-pending runs.
app.MapGet(
        "/api/exports",
        async (ExportLogDbContext db) =>
        {
            var now = DateTimeOffset.UtcNow;
            var runs = await db.ExportRuns.OrderByDescending(r => r.SequenceNo).ToListAsync();

            return runs.Select(r =>
                {
                    // Pending runs stale after 24 h — operator should investigate or release.
                    var isStale =
                        r.Status == ExportRunStatus.Pending
                        && DateTimeOffset.TryParse(
                            r.ExtractedAt,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.RoundtripKind,
                            out var ts
                        )
                        && (now - ts).TotalHours > 24;

                    var sha256Short = r.Sha256.Length >= 12 ? r.Sha256[..12] : r.Sha256;
                    return new ExportRunSummary(
                        r.SequenceNo,
                        r.ExtractedAt,
                        r.RecordCount,
                        sha256Short,
                        r.Status,
                        r.DataFileName,
                        isStale
                    );
                })
                .ToList();
        }
    )
    .RequireAuthorization();

// Returns full detail including a gap warning when the preceding run has not been released.
app.MapGet(
        "/api/exports/{seqNo:int}",
        async (int seqNo, ExportLogDbContext db) =>
        {
            var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
            if (run is null)
                return Results.NotFound();

            // Gap check: only meaningful for Pending runs that are candidates for release.
            string? gapWarning = null;
            if (run.Status == ExportRunStatus.Pending)
            {
                var lastReleasedSeq = await db
                    .ExportRuns.Where(r => r.Status == ExportRunStatus.Released)
                    .OrderByDescending(r => r.SequenceNo)
                    .Select(r => (int?)r.SequenceNo)
                    .FirstOrDefaultAsync();

                // Gap = a released run exists and it is not the immediate predecessor.
                if (lastReleasedSeq.HasValue && lastReleasedSeq.Value != seqNo - 1)
                    gapWarning =
                        $"Sequence gap detected: last released run is #{lastReleasedSeq.Value}, "
                        + $"but #{seqNo} is next in line. "
                        + $"Investigate run #{lastReleasedSeq.Value + 1} before releasing.";
            }

            return Results.Ok(
                new ExportDetailDto(
                    run.Id,
                    run.SequenceNo,
                    run.ExtractedAt,
                    run.RecordCount,
                    run.Sha256,
                    run.Status,
                    run.ReleasedAt,
                    run.OperatedBy,
                    run.ApprovedBy,
                    run.DataFileName,
                    run.DeliveredAt,
                    run.DeliveredBy,
                    run.ImportedRecordCount,
                    run.DeliveryNotes,
                    gapWarning
                )
            );
        }
    )
    .RequireAuthorization();

/// <summary>
/// Four-eyes release. Operator is read from the JWT (cannot be spoofed).
/// Approver must be a different registered user supplied in the request body.
/// </summary>
app.MapPost(
        "/api/exports/{seqNo:int}/release",
        async (int seqNo, ReleaseRequest request, HttpContext httpContext, ExportLogDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.Approver))
                return Results.BadRequest("Approver ist ein Pflichtfeld.");

            var operatorName = httpContext.User.Identity!.Name!;

            if (string.Equals(operatorName, request.Approver, StringComparison.OrdinalIgnoreCase))
                return Results.BadRequest(
                    "Operator und Approver müssen verschiedene Personen sein (Vier-Augen-Prinzip)."
                );

            if (!userStore.ContainsKey(request.Approver))
                return Results.BadRequest(
                    $"Unbekannter Approver: '{request.Approver}'. Nur registrierte Benutzer können freigeben."
                );

            var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
            if (run is null)
                return Results.NotFound();
            if (run.Status != ExportRunStatus.Pending)
                return Results.Conflict($"Run #{seqNo} ist bereits {run.Status}.");

            run.Status = ExportRunStatus.Released;
            run.OperatedBy = operatorName;
            run.ApprovedBy = request.Approver;
            run.ReleasedAt = DateTimeOffset.UtcNow.ToString("O");
            await db.SaveChangesAsync();

            return Results.Ok();
        }
    )
    .RequireAuthorization();

/// <summary>
/// Delivery acknowledgement. Closes the custody chain after the export file has been
/// physically transferred to the vendor. Only valid for Released runs; idempotent per run.
/// </summary>
app.MapPost(
        "/api/exports/{seqNo:int}/deliver",
        async (int seqNo, DeliverRequest request, HttpContext httpContext, ExportLogDbContext db) =>
        {
            var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
            if (run is null)
                return Results.NotFound();
            if (run.Status != ExportRunStatus.Released)
                return Results.BadRequest("Only released runs can be marked as delivered.");
            if (run.DeliveredAt is not null)
                return Results.Conflict($"Run #{seqNo} has already been recorded as delivered.");

            run.DeliveredAt = DateTimeOffset.UtcNow.ToString("O");
            run.DeliveredBy = httpContext.User.Identity!.Name!;
            run.ImportedRecordCount = request.ImportedRecordCount;
            run.DeliveryNotes = request.Notes;
            await db.SaveChangesAsync();

            return Results.Ok();
        }
    )
    .RequireAuthorization();

// ── On-demand pipeline trigger ────────────────────────────────────────────────

app.MapPost(
        "/api/pipeline/run",
        async (
            string? format,
            ExportLogDbContext db,
            IErpReader erpReader,
            IExportFilter filter,
            IDataMinimizer minimizer,
            ISchemaMapper mapper,
            IPackager packager,
            IExportSink sink,
            ILogger<Program> logger,
            CancellationToken ct
        ) =>
        {
            var fmt = format?.ToLowerInvariant() switch
            {
                "csv" => "csv",
                "json" => "json",
                _ => "xlsx",
            };

            var sequenceNo = (await db.ExportRuns.MaxAsync(r => (int?)r.SequenceNo, ct) ?? 0) + 1;
            var run = new ExportRunEntity
            {
                SequenceNo = sequenceNo,
                ExtractedAt = DateTimeOffset.UtcNow.ToString("O"),
                Status = ExportRunStatus.Pending,
            };
            db.ExportRuns.Add(run);
            await db.SaveChangesAsync(ct);

            var mappingSetting = await db.AppSettings.FindAsync("column_mappings");
            var nameMap = mappingSetting is null
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string>>(mappingSetting.Value);

            try
            {
                var rawItems = await erpReader.ReadMaintainableCIsAsync(ct);
                var filtered = filter.Filter(rawItems);
                var minimized = filtered.Select(minimizer.Minimize).ToList();
                var mapped = minimized.Select(mapper.Map).ToList();

                ExportPackage package;
                var extractedAt = DateTimeOffset.UtcNow;
                if (fmt == "csv")
                {
                    var bytes = BuildCsvBytes(mapped, ExportSchema.Version, extractedAt, nameMap);
                    var checksum = Convert
                        .ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
                        .ToLowerInvariant();
                    var fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt, "csv");
                    package = new ExportPackage(
                        new ExportManifest(sequenceNo, ExportSchema.Version, extractedAt, mapped.Count, checksum),
                        bytes,
                        fileName
                    );
                }
                else if (fmt == "json")
                {
                    var bytes = BuildJsonBytes(mapped, ExportSchema.Version, extractedAt, nameMap);
                    var checksum = Convert
                        .ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
                        .ToLowerInvariant();
                    var fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt, "json");
                    package = new ExportPackage(
                        new ExportManifest(sequenceNo, ExportSchema.Version, extractedAt, mapped.Count, checksum),
                        bytes,
                        fileName
                    );
                }
                else
                {
                    package = await packager.PackageAsync(mapped, sequenceNo, ct, nameMap);
                }

                await sink.WriteAsync(package, ct);

                run.RecordCount = package.Manifest.RecordCount;
                run.Sha256 = package.Manifest.Sha256Checksum;
                run.DataFileName = package.DataFileName;
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Run Now: Export #{Seq} ({Fmt}) abgeschlossen, {Count} Records",
                    sequenceNo,
                    fmt,
                    run.RecordCount
                );

                var sha256Short = run.Sha256.Length >= 12 ? run.Sha256[..12] : run.Sha256;
                return Results.Ok(new RunNowResult(sequenceNo, run.RecordCount, sha256Short));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Run Now: Export #{Seq} fehlgeschlagen", sequenceNo);
                run.Status = ExportRunStatus.Failed;
                await db.SaveChangesAsync(CancellationToken.None);
                return Results.Problem(ex.Message, statusCode: 500);
            }
        }
    )
    .RequireAuthorization();

byte[] BuildCsvBytes(IReadOnlyList<MappedExportRecord> records, string schemaVersion, DateTimeOffset extractedAt, IReadOnlyDictionary<string, string>? nameMap = null)
{
    string N(string n) => nameMap?.GetValueOrDefault(n) ?? n;
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"# schema_version={schemaVersion},extracted_at={extractedAt:O}");
    sb.AppendLine(
        $"{N("guid")},{N("serial_number")},{N("part_number")},{N("parent_serial_number")},{N("model_reference")},{N("commissioning_date")},{N("maintenance_state")}"
    );
    foreach (var r in records)
    {
        sb.Append(CsvEscape(r.Guid)).Append(',');
        sb.Append(CsvEscape(r.SerialNumber)).Append(',');
        sb.Append(CsvEscape(r.PartNumber)).Append(',');
        sb.Append(CsvEscape(r.ParentSerialNumber ?? "")).Append(',');
        sb.Append(CsvEscape(r.ModelReference)).Append(',');
        sb.Append(CsvEscape(r.CommissioningDateIso8601)).Append(',');
        sb.AppendLine(CsvEscape(r.MaintenanceState));
    }
    return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
}

byte[] BuildJsonBytes(IReadOnlyList<MappedExportRecord> records, string schemaVersion, DateTimeOffset extractedAt, IReadOnlyDictionary<string, string>? nameMap = null)
{
    string N(string n) => nameMap?.GetValueOrDefault(n) ?? n;
    var obj = new
    {
        schema_version = schemaVersion,
        extracted_at = extractedAt.ToString("O"),
        records = records
            .Select(r => (object)new Dictionary<string, object?>
            {
                [N("guid")] = r.Guid,
                [N("serial_number")] = r.SerialNumber,
                [N("part_number")] = r.PartNumber,
                [N("parent_serial_number")] = r.ParentSerialNumber,
                [N("model_reference")] = r.ModelReference,
                [N("commissioning_date")] = r.CommissioningDateIso8601,
                [N("maintenance_state")] = r.MaintenanceState,
            })
            .ToList(),
    };
    return JsonSerializer.SerializeToUtf8Bytes(obj, new JsonSerializerOptions { WriteIndented = true });
}

string CsvEscape(string? value)
{
    if (string.IsNullOrEmpty(value))
        return "";
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        return '"' + value.Replace("\"", "\"\"") + '"';
    return value;
}

// ── Export preview (read-only, no side effects) ────────────────────────────────

app.MapGet(
        "/api/pipeline/preview",
        async (
            IErpReader erpReader,
            IExportFilter filter,
            IDataMinimizer minimizer,
            ISchemaMapper mapper,
            CancellationToken ct
        ) =>
        {
            var rawItems = await erpReader.ReadMaintainableCIsAsync(ct);
            var filtered = filter.Filter(rawItems);
            var minimized = filtered.Select(minimizer.Minimize).ToList();
            var records = minimized
                .Select(item => mapper.Map(item))
                .Select(r => new PreviewRecord(
                    r.Guid,
                    r.SerialNumber,
                    r.PartNumber,
                    r.ParentSerialNumber,
                    r.ModelReference,
                    r.CommissioningDateIso8601,
                    r.MaintenanceState
                ))
                .ToList();
            return Results.Ok(new PreviewResult(records.Count, ExportSchema.Version, records));
        }
    )
    .RequireAuthorization();

// ── ERP demo database ─────────────────────────────────────────────────────────

app.MapGet(
        "/api/erp/records",
        async (DemoErpDbContext erpDb) =>
        {
            var configs = await erpDb
                .SystemConfigurations.AsNoTracking()
                .Include(sc => sc.Article)
                .Include(sc => sc.MaintenancePlans)
                .Include(sc => sc.ParentLinks)
                    .ThenInclude(l => l.Parent)
                .OrderBy(sc => sc.Id)
                .ToListAsync();

            var records = configs
                .Select(sc =>
                {
                    var activePlan = sc.MaintenancePlans.FirstOrDefault(mp => mp.Status == "Active");
                    var anyPlan = sc.MaintenancePlans.FirstOrDefault();
                    bool inScope = activePlan != null;
                    string? exclusionReason = null;
                    if (!inScope)
                        exclusionReason = sc.MaintenancePlans.Any()
                            ? "Inactive maintenance plan"
                            : "No maintenance plan";

                    return new ErpCiRecord(
                        Id: sc.Id,
                        Serial: sc.Serial,
                        Status: sc.Status,
                        CommissionDate: sc.CommissionDate?.ToString("yyyy-MM-dd"),
                        ArticleName: sc.Article?.ArticleName,
                        PartNumber: sc.Article?.PartNumber,
                        Manufacturer: sc.Article?.Manufacturer,
                        MaintenancePlanStatus: activePlan?.Status ?? anyPlan?.Status,
                        AllocationChartRef: activePlan?.AllocationChartRef ?? anyPlan?.AllocationChartRef,
                        ParentId: sc.ParentLinks.FirstOrDefault()?.ParentId,
                        ParentSerial: sc.ParentLinks.FirstOrDefault()?.Parent?.Serial,
                        InScope: inScope,
                        ExclusionReason: exclusionReason,
                        TechnicianName: sc.TechnicianName,
                        StorageLocation: sc.StorageLocation
                    );
                })
                .ToList();

            return Results.Ok(records);
        }
    )
    .RequireAuthorization();

// ── Source database schema ────────────────────────────────────────────────────

// Returns schema from the persisted Postgres connection when one is configured,
// falling back to the hardcoded demo schema when none is stored or the connection fails.
app.MapGet(
        "/api/source-schema",
        async (ExportLogDbContext db) =>
        {
            var connSetting = await db.AppSettings.FindAsync("erp_connection");
            if (connSetting is not null)
            {
                var cfg = JsonSerializer.Deserialize<ConnectionConfigRequest>(connSetting.Value);
                if (cfg is not null)
                {
                    try
                    {
                        var connStr = BuildNpgsqlConnectionString(cfg);
                        await using var conn = new NpgsqlConnection(connStr);
                        await conn.OpenAsync();
                        var tables = await IntrospectSchemaAsync(conn);
                        return Results.Ok(new SourceSchemaDto($"{cfg.Host}:{cfg.Port}/{cfg.Database}", tables));
                    }
                    catch
                    {
                        // Fall through to demo schema if stored config is unreachable.
                    }
                }
            }

            return Results.Ok(DemoSourceSchema());
        }
    )
    .RequireAuthorization();

// ── Connection config ─────────────────────────────────────────────────────────

// Returns the stored connection (host/port/db/user only — password never returned).
app.MapGet(
        "/api/connection",
        async (ExportLogDbContext db) =>
        {
            var setting = await db.AppSettings.FindAsync("erp_connection");
            if (setting is null)
                return Results.NotFound();

            var cfg = JsonSerializer.Deserialize<ConnectionConfigRequest>(setting.Value);
            if (cfg is null)
                return Results.NotFound();

            return Results.Ok(new ErpConnectionInfo(cfg.Host, cfg.Port, cfg.Database, cfg.Username));
        }
    )
    .RequireAuthorization();

// Tests the connection, persists it on success, and returns the live source schema.
app.MapPost(
        "/api/connection",
        async (ConnectionConfigRequest request, ExportLogDbContext db) =>
        {
            if (
                string.IsNullOrWhiteSpace(request.Host)
                || string.IsNullOrWhiteSpace(request.Database)
                || string.IsNullOrWhiteSpace(request.Username)
            )
                return Results.BadRequest("Host, Database, and Username are required.");

            try
            {
                var connStr = BuildNpgsqlConnectionString(request);
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                var tables = await IntrospectSchemaAsync(conn);

                // Persist config (including password — stored server-side only, never in localStorage).
                var serialized = JsonSerializer.Serialize(request);
                var setting = await db.AppSettings.FindAsync("erp_connection");
                if (setting is null)
                    db.AppSettings.Add(new AppSettingEntity { Key = "erp_connection", Value = serialized });
                else
                    setting.Value = serialized;
                await db.SaveChangesAsync();

                return Results.Ok(new SourceSchemaDto($"{request.Host}:{request.Port}/{request.Database}", tables));
            }
            catch (Exception ex)
            {
                return Results.BadRequest($"Connection failed: {ex.Message}");
            }
        }
    )
    .RequireAuthorization();

// ── Export schema definition ───────────────────────────────────────────────────

// Returns schema columns with the active flag read from persisted preferences (AppSetting key "active_columns").
// Defaults to all columns active when no preference is stored.
app.MapGet(
        "/api/schema",
        async (ExportLogDbContext db) =>
        {
            var activeSetting = await db.AppSettings.FindAsync("active_columns");
            var activeSet = activeSetting is null
                ? new HashSet<string>(ExportSchema.Columns)
                : new HashSet<string>(JsonSerializer.Deserialize<string[]>(activeSetting.Value) ?? []);

            var mappingSetting = await db.AppSettings.FindAsync("column_mappings");
            var mapping = mappingSetting is null
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(mappingSetting.Value) ?? new();

            string? ExportName(string n) => mapping.GetValueOrDefault(n);

            var columns = new SchemaColumnDto[]
            {
                new(
                    "guid",
                    "systemconfiguration.id",
                    "UUID text",
                    "Coalesce key — stable PostgreSQL PK; never changes for the entity lifetime",
                    activeSet.Contains("guid"),
                    ExportName("guid")
                ),
                new(
                    "serial_number",
                    "systemconfiguration.serial",
                    "Text (explicit)",
                    "Physical unit identity; warranty lookups by humans. Not the coalesce key.",
                    activeSet.Contains("serial_number"),
                    ExportName("serial_number")
                ),
                new(
                    "part_number",
                    "masterdata.part_number",
                    "Text (explicit)",
                    "Model/part reference — explicit text to prevent numeric coercion",
                    activeSet.Contains("part_number"),
                    ExportName("part_number")
                ),
                new(
                    "parent_serial_number",
                    "articlestructure → systemconfiguration.serial",
                    "Text (explicit)",
                    "BOM parent reference; drives cmdb_rel_ci hierarchy on the vendor side",
                    activeSet.Contains("parent_serial_number"),
                    ExportName("parent_serial_number")
                ),
                new(
                    "model_reference",
                    "masterdata.article_name",
                    "Text",
                    "Human-readable model name; links to cmdb_model on the vendor side",
                    activeSet.Contains("model_reference"),
                    ExportName("model_reference")
                ),
                new(
                    "commissioning_date",
                    "systemconfiguration.commission_date",
                    "ISO 8601 date",
                    "Warranty start date (YYYY-MM-DD)",
                    activeSet.Contains("commissioning_date"),
                    ExportName("commissioning_date")
                ),
                new(
                    "maintenance_state",
                    "systemconfiguration.status",
                    "Text (mapped enum)",
                    "CI lifecycle state mapped to ServiceNow install_status values",
                    activeSet.Contains("maintenance_state"),
                    ExportName("maintenance_state")
                ),
            };
            return Results.Ok(new SchemaDto(ExportSchema.Version, columns));
        }
    )
    .RequireAuthorization();

// Persists the active column set. Rejects unknown column names; allows partial sets.
app.MapPatch(
        "/api/schema/columns",
        async (ColumnPatchRequest request, ExportLogDbContext db) =>
        {
            // Only accept column names that exist in the canonical schema.
            var valid = request.Columns.Where(c => ExportSchema.Columns.Contains(c)).Distinct().ToArray();

            var serialized = JsonSerializer.Serialize(valid);
            var setting = await db.AppSettings.FindAsync("active_columns");
            if (setting is null)
                db.AppSettings.Add(new AppSettingEntity { Key = "active_columns", Value = serialized });
            else
                setting.Value = serialized;

            await db.SaveChangesAsync();
            return Results.Ok(valid);
        }
    )
    .RequireAuthorization();

// Persists per-column export name overrides. Keys not in the canonical schema are silently dropped.
// An empty or whitespace value removes the override for that column (falls back to the source name).
app.MapPatch(
        "/api/schema/mappings",
        async (MappingPatchRequest request, ExportLogDbContext db) =>
        {
            var valid = request.Mappings
                .Where(kvp => ExportSchema.Columns.Contains(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Trim());

            var serialized = JsonSerializer.Serialize(valid);
            var setting = await db.AppSettings.FindAsync("column_mappings");
            if (setting is null)
                db.AppSettings.Add(new AppSettingEntity { Key = "column_mappings", Value = serialized });
            else
                setting.Value = serialized;

            await db.SaveChangesAsync();
            return Results.Ok(valid);
        }
    )
    .RequireAuthorization();

// ─────────────────────────────────────────────────────────────────────────────

await app.RunAsync();

// Builds the Npgsql connection string from a config request, enforcing SSL prefer and read-only intent.
string BuildNpgsqlConnectionString(ConnectionConfigRequest cfg) =>
    $"Host={cfg.Host};Port={cfg.Port};Database={cfg.Database};Username={cfg.Username};Password={cfg.Password};SSL Mode=Prefer;Trust Server Certificate=true;Timeout=5;Command Timeout=10";

// Introspects the public schema of an open Npgsql connection using information_schema views.
async Task<SourceTableDto[]> IntrospectSchemaAsync(NpgsqlConnection conn)
{
    // Single query: columns with PK flag via a correlated EXISTS.
    var sql = """
        SELECT
            c.table_name,
            c.column_name,
            c.data_type,
            c.is_nullable,
            EXISTS (
                SELECT 1
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                    ON kcu.constraint_name = tc.constraint_name
                    AND kcu.table_schema  = tc.table_schema
                    AND kcu.table_name    = tc.table_name
                    AND kcu.column_name   = c.column_name
                WHERE tc.constraint_type = 'PRIMARY KEY'
                  AND tc.table_schema    = 'public'
                  AND tc.table_name      = c.table_name
            ) AS is_pk
        FROM information_schema.columns c
        WHERE c.table_schema = 'public'
        ORDER BY c.table_name, c.ordinal_position
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var reader = await cmd.ExecuteReaderAsync();

    var byTable = new Dictionary<string, List<SourceColumnDto>>();
    while (await reader.ReadAsync())
    {
        var table = reader.GetString(0);
        if (!byTable.ContainsKey(table))
            byTable[table] = [];
        byTable[table]
            .Add(
                new SourceColumnDto(
                    Name: reader.GetString(1),
                    Type: reader.GetString(2),
                    Nullable: reader.GetString(3) == "YES",
                    PrimaryKey: reader.GetBoolean(4)
                )
            );
    }

    return byTable.Select(kv => new SourceTableDto(kv.Key, "", kv.Value.ToArray())).OrderBy(t => t.Name).ToArray();
}

// Hardcoded demo schema that mirrors what a real production PostgreSQL ERP database would expose.
SourceSchemaDto DemoSourceSchema() =>
    new(
        "demo-erp (SQLite in dev · PostgreSQL in prod)",
        new SourceTableDto[]
        {
            new(
                "systemconfiguration",
                "Installed CI instances — one row per physical unit",
                new SourceColumnDto[]
                {
                    new("id", "uuid", Nullable: false, PrimaryKey: true),
                    new("serial", "character varying(100)", Nullable: true, PrimaryKey: false),
                    new("article_id", "uuid", Nullable: true, PrimaryKey: false),
                    new("status", "character varying(50)", Nullable: true, PrimaryKey: false),
                    new("commission_date", "date", Nullable: true, PrimaryKey: false),
                    new("technician_name", "character varying(100)", Nullable: true, PrimaryKey: false),
                    new("storage_location", "character varying(200)", Nullable: true, PrimaryKey: false),
                }
            ),
            new(
                "masterdata",
                "Article/model master records — one row per model type",
                new SourceColumnDto[]
                {
                    new("id", "uuid", Nullable: false, PrimaryKey: true),
                    new("article_name", "character varying(200)", Nullable: true, PrimaryKey: false),
                    new("part_number", "character varying(100)", Nullable: true, PrimaryKey: false),
                    new("manufacturer", "character varying(100)", Nullable: true, PrimaryKey: false),
                }
            ),
            new(
                "maintenance_plan",
                "Maintenance plan assignments — drives scope filter",
                new SourceColumnDto[]
                {
                    new("id", "uuid", Nullable: false, PrimaryKey: true),
                    new("system_configuration_id", "uuid", Nullable: false, PrimaryKey: false),
                    new("status", "character varying(50)", Nullable: false, PrimaryKey: false),
                    new("allocation_chart_ref", "character varying(100)", Nullable: true, PrimaryKey: false),
                }
            ),
            new(
                "articlestructure",
                "BOM parent–child relationships",
                new SourceColumnDto[]
                {
                    new("id", "uuid", Nullable: false, PrimaryKey: true),
                    new("parent_id", "uuid", Nullable: true, PrimaryKey: false),
                    new("child_id", "uuid", Nullable: true, PrimaryKey: false),
                }
            ),
        }
    );

// ── DTOs ──────────────────────────────────────────────────────────────────────

namespace Connector.Api
{
    record ExportRunSummary(
        int SequenceNo,
        string ExtractedAt,
        int RecordCount,
        string Sha256Short,
        string Status,
        string DataFileName,
        bool IsStale
    );

    /// <summary>
    /// Full export run detail. SequenceGapWarning is non-null when a Pending run has a gap
    /// relative to the last released run — operators should investigate before releasing.
    /// Delivery fields are null until the physical handover is recorded via POST …/deliver.
    /// </summary>
    record ExportDetailDto(
        int Id,
        int SequenceNo,
        string ExtractedAt,
        int RecordCount,
        string Sha256,
        string Status,
        string? ReleasedAt,
        string? OperatedBy,
        string? ApprovedBy,
        string DataFileName,
        string? DeliveredAt,
        string? DeliveredBy,
        int? ImportedRecordCount,
        string? DeliveryNotes,
        string? SequenceGapWarning
    );

    /// <summary>Operator is taken from the JWT; only the approver name is supplied in the body.</summary>
    record ReleaseRequest(string Approver);

    /// <summary>Body for POST …/deliver. ImportedRecordCount and Notes are optional confirmation data.</summary>
    record DeliverRequest(int? ImportedRecordCount, string? Notes);

    /// <summary>Body for PATCH /api/schema/columns. Columns not in ExportSchema.Columns are silently ignored.</summary>
    record ColumnPatchRequest(string[] Columns);

    /// <summary>Body for PATCH /api/schema/mappings. Keys not in ExportSchema.Columns are silently ignored. Empty/whitespace values remove the override.</summary>
    record MappingPatchRequest(Dictionary<string, string> Mappings);

    record LoginRequest(string Username, string Password);

    record LoginResponse(string Token, string Username);

    record HashRequest(string Password);

    record ErpCiRecord(
        string Id,
        string? Serial,
        string? Status,
        string? CommissionDate,
        string? ArticleName,
        string? PartNumber,
        string? Manufacturer,
        string? MaintenancePlanStatus,
        string? AllocationChartRef,
        string? ParentId,
        string? ParentSerial,
        bool InScope,
        string? ExclusionReason,
        string? TechnicianName,
        string? StorageLocation
    );

    record SchemaColumnDto(string Name, string ErpSource, string Type, string Notes, bool Active, string? ExportName);

    record SchemaDto(string Version, SchemaColumnDto[] Columns);

    record SourceColumnDto(string Name, string Type, bool Nullable, bool PrimaryKey);

    record SourceTableDto(string Name, string Description, SourceColumnDto[] Columns);

    record SourceSchemaDto(string ConnectionLabel, SourceTableDto[] Tables);

    record RunNowResult(int SequenceNo, int RecordCount, string Sha256Short);

    record PreviewRecord(
        string Guid,
        string SerialNumber,
        string PartNumber,
        string? ParentSerialNumber,
        string ModelReference,
        string CommissioningDate,
        string MaintenanceState
    );

    record PreviewResult(int RecordCount, string SchemaVersion, IList<PreviewRecord> Records);

    /// <summary>
    /// Full connection config — stored server-side; password is never returned to the client.
    /// </summary>
    record ConnectionConfigRequest(string Host, int Port, string Database, string Username, string Password);

    /// <summary>Public view of the stored connection — no password field.</summary>
    record ErpConnectionInfo(string Host, int Port, string Database, string Username);
}
