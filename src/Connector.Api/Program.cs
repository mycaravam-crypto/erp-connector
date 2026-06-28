using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Connector.Api;
using Connector.Core.Domain;
using Connector.Core.Interfaces;
using Connector.Core.Schema;
using Connector.Erp.DemoErp;
using Connector.Export;
using Connector.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Auth ──────────────────────────────────────────────────────────────────────

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

var jwtSecret =
    builder.Configuration["Auth:JwtSecret"]
    ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
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
    userStore = authUsers.ToDictionary(
        u => u.Username,
        u => u.PasswordHash,
        StringComparer.OrdinalIgnoreCase
    );
}

// ── Auth endpoints ────────────────────────────────────────────────────────────

app.MapPost("/api/auth/login", (LoginRequest req) =>
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
});

// Dev-only: returns a BCrypt hash for a plaintext password (to seed appsettings for production users).
if (app.Environment.IsDevelopment())
{
    app.MapPost("/api/auth/hash", (HashRequest req) =>
        Results.Ok(new { Hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 11) })
    );
}

// ── Export API (all routes require a valid JWT) ───────────────────────────────

app.MapGet(
    "/api/exports",
    async (ExportLogDbContext db) =>
        await db
            .ExportRuns.OrderByDescending(r => r.SequenceNo)
            .Select(r => new ExportRunSummary(
                r.SequenceNo,
                r.ExtractedAt,
                r.RecordCount,
                r.Sha256.Substring(0, 12),
                r.Status,
                r.DataFileName
            ))
            .ToListAsync()
).RequireAuthorization();

app.MapGet(
    "/api/exports/{seqNo:int}",
    async (int seqNo, ExportLogDbContext db) =>
    {
        var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
        return run is null ? Results.NotFound() : Results.Ok(run);
    }
).RequireAuthorization();

/// <summary>
/// Vier-Augen-Freigabe. Operator comes from the JWT; approver must be a different registered user.
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
).RequireAuthorization();

// ── On-demand pipeline trigger ────────────────────────────────────────────────

app.MapPost("/api/pipeline/run", async (
    string? format,
    ExportLogDbContext db,
    IErpReader erpReader,
    IExportFilter filter,
    IDataMinimizer minimizer,
    ISchemaMapper mapper,
    IPackager packager,
    IExportSink sink,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var fmt = format?.ToLowerInvariant() switch { "csv" => "csv", "json" => "json", _ => "xlsx" };

    var sequenceNo = (await db.ExportRuns.MaxAsync(r => (int?)r.SequenceNo, ct) ?? 0) + 1;
    var run = new ExportRunEntity
    {
        SequenceNo = sequenceNo,
        ExtractedAt = DateTimeOffset.UtcNow.ToString("O"),
        Status = ExportRunStatus.Pending,
    };
    db.ExportRuns.Add(run);
    await db.SaveChangesAsync(ct);

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
            var bytes = BuildCsvBytes(mapped, ExportSchema.Version, extractedAt);
            var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            var fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt, "csv");
            package = new ExportPackage(
                new ExportManifest(sequenceNo, ExportSchema.Version, extractedAt, mapped.Count, checksum),
                bytes, fileName);
        }
        else if (fmt == "json")
        {
            var bytes = BuildJsonBytes(mapped, ExportSchema.Version, extractedAt);
            var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            var fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt, "json");
            package = new ExportPackage(
                new ExportManifest(sequenceNo, ExportSchema.Version, extractedAt, mapped.Count, checksum),
                bytes, fileName);
        }
        else
        {
            package = await packager.PackageAsync(mapped, sequenceNo, ct);
        }

        await sink.WriteAsync(package, ct);

        run.RecordCount = package.Manifest.RecordCount;
        run.Sha256 = package.Manifest.Sha256Checksum;
        run.DataFileName = package.DataFileName;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Run Now: Export #{Seq} ({Fmt}) abgeschlossen, {Count} Records", sequenceNo, fmt, run.RecordCount);

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
}).RequireAuthorization();

byte[] BuildCsvBytes(IReadOnlyList<MappedExportRecord> records, string schemaVersion, DateTimeOffset extractedAt)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine($"# schema_version={schemaVersion},extracted_at={extractedAt:O}");
    sb.AppendLine("guid,serial_number,part_number,parent_serial_number,model_reference,commissioning_date,maintenance_state");
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

byte[] BuildJsonBytes(IReadOnlyList<MappedExportRecord> records, string schemaVersion, DateTimeOffset extractedAt)
{
    var obj = new
    {
        schema_version = schemaVersion,
        extracted_at = extractedAt.ToString("O"),
        records = records.Select(r => new
        {
            guid = r.Guid,
            serial_number = r.SerialNumber,
            part_number = r.PartNumber,
            parent_serial_number = r.ParentSerialNumber,
            model_reference = r.ModelReference,
            commissioning_date = r.CommissioningDateIso8601,
            maintenance_state = r.MaintenanceState,
        }).ToList()
    };
    return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}

string CsvEscape(string? value)
{
    if (string.IsNullOrEmpty(value)) return "";
    if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        return '"' + value.Replace("\"", "\"\"") + '"';
    return value;
}

// ── Export preview (read-only, no side effects) ────────────────────────────────

app.MapGet("/api/pipeline/preview", async (
    IErpReader erpReader,
    IExportFilter filter,
    IDataMinimizer minimizer,
    ISchemaMapper mapper,
    CancellationToken ct) =>
{
    var rawItems = await erpReader.ReadMaintainableCIsAsync(ct);
    var filtered = filter.Filter(rawItems);
    var minimized = filtered.Select(minimizer.Minimize).ToList();
    var records = minimized
        .Select(item => mapper.Map(item))
        .Select(r => new PreviewRecord(
            r.Guid, r.SerialNumber, r.PartNumber, r.ParentSerialNumber,
            r.ModelReference, r.CommissioningDateIso8601, r.MaintenanceState))
        .ToList();
    return Results.Ok(new PreviewResult(records.Count, ExportSchema.Version, records));
}).RequireAuthorization();

// ── ERP demo database ─────────────────────────────────────────────────────────

app.MapGet("/api/erp/records", async (DemoErpDbContext erpDb) =>
{
    var configs = await erpDb.SystemConfigurations
        .AsNoTracking()
        .Include(sc => sc.Article)
        .Include(sc => sc.MaintenancePlans)
        .Include(sc => sc.ParentLinks).ThenInclude(l => l.Parent)
        .OrderBy(sc => sc.Id)
        .ToListAsync();

    var records = configs.Select(sc =>
    {
        var activePlan = sc.MaintenancePlans.FirstOrDefault(mp => mp.Status == "Active");
        var anyPlan = sc.MaintenancePlans.FirstOrDefault();
        bool inScope = activePlan != null;
        string? exclusionReason = null;
        if (!inScope)
            exclusionReason = sc.MaintenancePlans.Any() ? "Inactive maintenance plan" : "No maintenance plan";

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
    }).ToList();

    return Results.Ok(records);
}).RequireAuthorization();

// ── Source database schema ────────────────────────────────────────────────────

app.MapGet("/api/source-schema", () =>
{
    var tables = new SourceTableDto[]
    {
        new("systemconfiguration", "Installed CI instances — one row per physical unit",
            new SourceColumnDto[]
            {
                new("id", "uuid", Nullable: false, PrimaryKey: true),
                new("serial", "character varying(100)", Nullable: true, PrimaryKey: false),
                new("article_id", "uuid", Nullable: true, PrimaryKey: false),
                new("status", "character varying(50)", Nullable: true, PrimaryKey: false),
                new("commission_date", "date", Nullable: true, PrimaryKey: false),
                new("technician_name", "character varying(100)", Nullable: true, PrimaryKey: false),
                new("storage_location", "character varying(200)", Nullable: true, PrimaryKey: false),
            }),
        new("masterdata", "Article/model master records — one row per model type",
            new SourceColumnDto[]
            {
                new("id", "uuid", Nullable: false, PrimaryKey: true),
                new("article_name", "character varying(200)", Nullable: true, PrimaryKey: false),
                new("part_number", "character varying(100)", Nullable: true, PrimaryKey: false),
                new("manufacturer", "character varying(100)", Nullable: true, PrimaryKey: false),
            }),
        new("maintenance_plan", "Maintenance plan assignments — drives scope filter",
            new SourceColumnDto[]
            {
                new("id", "uuid", Nullable: false, PrimaryKey: true),
                new("system_configuration_id", "uuid", Nullable: false, PrimaryKey: false),
                new("status", "character varying(50)", Nullable: false, PrimaryKey: false),
                new("allocation_chart_ref", "character varying(100)", Nullable: true, PrimaryKey: false),
            }),
        new("articlestructure", "BOM parent–child relationships",
            new SourceColumnDto[]
            {
                new("id", "uuid", Nullable: false, PrimaryKey: true),
                new("parent_id", "uuid", Nullable: true, PrimaryKey: false),
                new("child_id", "uuid", Nullable: true, PrimaryKey: false),
            }),
    };
    return Results.Ok(new SourceSchemaDto("demo-erp (SQLite in dev · PostgreSQL in prod)", tables));
}).RequireAuthorization();

// ── Export schema definition ───────────────────────────────────────────────────

app.MapGet("/api/schema", () =>
{
    var columns = new SchemaColumnDto[]
    {
        new("guid", "systemconfiguration.id", "UUID text",
            "Coalesce key — stable PostgreSQL PK; never changes for the entity lifetime", true),
        new("serial_number", "systemconfiguration.serial", "Text (explicit)",
            "Physical unit identity; warranty lookups by humans. Not the coalesce key.", true),
        new("part_number", "masterdata.part_number", "Text (explicit)",
            "Model/part reference — explicit text to prevent numeric coercion", true),
        new("parent_serial_number", "articlestructure → systemconfiguration.serial", "Text (explicit)",
            "BOM parent reference; drives cmdb_rel_ci hierarchy on the vendor side", true),
        new("model_reference", "masterdata.article_name", "Text",
            "Human-readable model name; links to cmdb_model on the vendor side", true),
        new("commissioning_date", "systemconfiguration.commission_date", "ISO 8601 date",
            "Warranty start date (YYYY-MM-DD)", true),
        new("maintenance_state", "systemconfiguration.status", "Text (mapped enum)",
            "CI lifecycle state mapped to ServiceNow install_status values", true),
    };
    return Results.Ok(new SchemaDto(ExportSchema.Version, columns));
}).RequireAuthorization();

// ─────────────────────────────────────────────────────────────────────────────

await app.RunAsync();

// ── DTOs ──────────────────────────────────────────────────────────────────────

namespace Connector.Api
{
    record ExportRunSummary(
        int SequenceNo,
        string ExtractedAt,
        int RecordCount,
        string Sha256Short,
        string Status,
        string DataFileName
    );

    /// <summary>Operator is taken from the JWT; only the approver name is supplied in the body.</summary>
    record ReleaseRequest(string Approver);

    record LoginRequest(string Username, string Password);

    record LoginResponse(string Token, string Username);

    record HashRequest(string Password);

    record ErpCiRecord(
        string Id, string? Serial, string? Status, string? CommissionDate,
        string? ArticleName, string? PartNumber, string? Manufacturer,
        string? MaintenancePlanStatus, string? AllocationChartRef,
        string? ParentId, string? ParentSerial,
        bool InScope, string? ExclusionReason,
        string? TechnicianName, string? StorageLocation);

    record SchemaColumnDto(string Name, string ErpSource, string Type, string Notes, bool Active);
    record SchemaDto(string Version, SchemaColumnDto[] Columns);

    record SourceColumnDto(string Name, string Type, bool Nullable, bool PrimaryKey);
    record SourceTableDto(string Name, string Description, SourceColumnDto[] Columns);
    record SourceSchemaDto(string ConnectionLabel, SourceTableDto[] Tables);

    record RunNowResult(int SequenceNo, int RecordCount, string Sha256Short);
    record PreviewRecord(string Guid, string SerialNumber, string PartNumber, string? ParentSerialNumber,
        string ModelReference, string CommissioningDate, string MaintenanceState);
    record PreviewResult(int RecordCount, string SchemaVersion, IList<PreviewRecord> Records);
}
