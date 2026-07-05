using System.Text.Json;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Connector.Api.Endpoints;

static class SettingsEndpoints
{
    internal static void MapSettingsEndpoints(this WebApplication app)
    {
        // Returns the effective scheduler config: DB value if saved, else appsettings defaults.
        app.MapGet(
                "/api/settings/scheduler",
                async (ExportLogDbContext db, IOptions<ExportWorkerOptions> defaults) =>
                {
                    var setting = await db.AppSettings.FindAsync("scheduler_config");
                    if (setting is not null)
                    {
                        var stored = JsonSerializer.Deserialize<SchedulerConfigData>(setting.Value);
                        if (stored is not null)
                            return Results.Ok(stored);
                    }
                    return Results.Ok(
                        new SchedulerConfigData(
                            defaults.Value.ScheduledTimeUtc.ToString(@"hh\:mm"),
                            defaults.Value.RetentionDays
                        )
                    );
                }
            )
            .RequireAuthorization();

        // Validates and persists the scheduler config. Takes effect on the worker's next sleep cycle.
        app.MapPut(
                "/api/settings/scheduler",
                async (SchedulerConfigData dto, ExportLogDbContext db, HttpContext httpContext, AuditService audit) =>
                {
                    if (
                        !TimeSpan.TryParse(
                            dto.ScheduledTimeUtc,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var ts
                        )
                        || ts < TimeSpan.Zero
                        || ts >= TimeSpan.FromDays(1)
                    )
                        return Results.BadRequest(
                            "ScheduledTimeUtc must be a valid time in HH:mm format (00:00 – 23:59)."
                        );
                    if (dto.RetentionDays < 1 || dto.RetentionDays > 3650)
                        return Results.BadRequest("RetentionDays must be between 1 and 3650.");

                    var serialized = JsonSerializer.Serialize(dto);
                    var setting = await db.AppSettings.FindAsync("scheduler_config");
                    if (setting is null)
                        db.AppSettings.Add(new AppSettingEntity { Key = "scheduler_config", Value = serialized });
                    else
                        setting.Value = serialized;
                    await db.SaveChangesAsync();
                    await audit.LogAsync(
                        httpContext.User.Identity!.Name!,
                        "scheduler_updated",
                        $"time={dto.ScheduledTimeUtc} retention={dto.RetentionDays}d"
                    );
                    return Results.Ok(dto);
                }
            )
            .RequireAuthorization();

        // Returns the currently active GDPR denylist (DB value if set, else defaults).
        app.MapGet(
                "/api/gdpr-denied-fields",
                async (ExportLogDbContext db) =>
                {
                    var fields = await DynamicExportService.GetDeniedFieldsAsync(db);
                    return Results.Ok(new { fields = fields.ToArray() });
                }
            )
            .RequireAuthorization();

        // Replaces the GDPR denylist. Validates and stores as JSON in AppSetting.
        app.MapMethods(
                "/api/gdpr-denied-fields",
                ["PATCH"],
                async (
                    GdprDenylistRequest request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit
                ) =>
                {
                    if (request.Fields is null || request.Fields.Count == 0)
                        return Results.BadRequest("At least one field is required.");
                    if (request.Fields.Any(f => string.IsNullOrWhiteSpace(f)))
                        return Results.BadRequest("Field names must not be empty or whitespace.");
                    if (request.Fields.Count > 50)
                        return Results.BadRequest("Maximum 50 fields allowed in the GDPR denylist.");

                    var serialized = JsonSerializer.Serialize(request.Fields);
                    var setting = await db.AppSettings.FindAsync("gdpr_denied_fields");
                    if (setting is null)
                        db.AppSettings.Add(new AppSettingEntity { Key = "gdpr_denied_fields", Value = serialized });
                    else
                        setting.Value = serialized;

                    await db.SaveChangesAsync();
                    await audit.LogAsync(
                        httpContext.User.Identity!.Name!,
                        "gdpr_denylist_updated",
                        $"{request.Fields.Count} fields"
                    );
                    return Results.Ok(new { fields = request.Fields });
                }
            )
            .RequireAuthorization();

        // Returns the most recent N audit entries (default 100) ordered newest-first.
        app.MapGet(
                "/api/audit",
                async (ExportLogDbContext db, int? limit) =>
                {
                    var cap = limit ?? 100;
                    var entries = await db
                        .AuditLog.OrderByDescending(a => a.Id)
                        .Take(cap)
                        .Select(a => new AuditEntryDto(a.Id, a.Timestamp, a.Username, a.Action, a.Detail))
                        .ToListAsync();
                    return Results.Ok(entries);
                }
            )
            .RequireAuthorization();
    }
}
