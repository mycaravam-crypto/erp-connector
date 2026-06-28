using Connector.Core.Interfaces;
using Connector.Erp.DemoErp;
using Connector.Export;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;

// Composition Root: alle Interfaces werden hier mit konkreten Implementierungen verdrahtet.
// Keine abstrakten Factories, keine Plugin-Registrierung — direkte Bindung ist YAGNI-konform.

var builder = WebApplication.CreateBuilder(args);

// Pipeline-Implementierungen
builder.Services.AddSingleton<IExportFilter, ExportFilter>();
builder.Services.AddSingleton<IDataMinimizer, DataMinimizer>();
builder.Services.AddSingleton<ISchemaMapper, SchemaMapper>();
builder.Services.AddSingleton<IPackager, ExcelPackager>();

// Infrastruktur
builder.Services.Configure<ExportSinkOptions>(builder.Configuration.GetSection("ExportSink"));
builder.Services.Configure<ExportWorkerOptions>(builder.Configuration.GetSection("ExportWorker"));
builder.Services.AddSingleton<IExportSink, FileSystemExportSink>();

builder.Services.AddDbContext<ExportLogDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("ExportLog")));

builder.Services.AddHostedService<ExportWorker>();

// Demo-ERP-Datenbank (SQLite) — wird beim Start erstellt und mit Testdaten befüllt.
// Für Produktion: DemoErpReader durch echten PostgreSQL-Reader ersetzen.
builder.Services.AddDbContext<DemoErpDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("DemoErp")));
builder.Services.AddScoped<IErpReader, DemoErpReader>();

var app = builder.Build();

// Demo-ERP beim Start initialisieren (idempotent).
using (var scope = app.Services.CreateScope())
{
    var erpDb = scope.ServiceProvider.GetRequiredService<DemoErpDbContext>();
    erpDb.Database.EnsureCreated();
    DemoErpSeed.Seed(erpDb);

    var exportLogDb = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
    exportLogDb.Database.EnsureCreated();
}

// API-Endpunkte — nur was die Release-UI braucht.

app.MapGet("/api/exports", async (ExportLogDbContext db) =>
    await db.ExportRuns
        .OrderByDescending(r => r.SequenceNo)
        .Select(r => new ExportRunSummary(
            r.SequenceNo, r.ExtractedAt, r.RecordCount,
            r.Sha256.Substring(0, r.Sha256.Length >= 12 ? 12 : r.Sha256.Length),
            r.Status, r.DataFileName))
        .ToListAsync());

app.MapGet("/api/exports/{seqNo:int}", async (int seqNo, ExportLogDbContext db) =>
{
    var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
    return run is null ? Results.NotFound() : Results.Ok(run);
});

/// <summary>
/// Vier-Augen-Freigabe. Operator und Approver müssen verschiedene Benutzer sein.
/// </summary>
app.MapPost("/api/exports/{seqNo:int}/release", async (
    int seqNo,
    ReleaseRequest request,
    ExportLogDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Operator) || string.IsNullOrWhiteSpace(request.Approver))
        return Results.BadRequest("Operator und Approver sind Pflichtfelder.");

    if (string.Equals(request.Operator, request.Approver, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Operator und Approver müssen verschiedene Personen sein (Vier-Augen-Prinzip).");

    var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
    if (run is null) return Results.NotFound();
    if (run.Status != ExportRunStatus.Pending)
        return Results.Conflict($"Run #{seqNo} ist bereits {run.Status}.");

    run.Status = ExportRunStatus.Released;
    run.OperatedBy = request.Operator;
    run.ApprovedBy = request.Approver;
    run.ReleasedAt = DateTimeOffset.UtcNow.ToString("O");
    await db.SaveChangesAsync();

    return Results.Ok();
});

app.Run();

// DTOs — direkt in Program.cs, da zu einfach für eigene Dateien (YAGNI).
record ExportRunSummary(
    int SequenceNo,
    string ExtractedAt,
    int RecordCount,
    string Sha256Short,
    string Status,
    string DataFileName);

record ReleaseRequest(string Operator, string Approver);
