using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Connector.Api.Endpoints;

static class ExportEndpoints
{
    internal static void MapExportEndpoints(this WebApplication app, IReadOnlyDictionary<string, string> userStore)
    {
        // Returns the run list with an IsStale flag so the UI can warn about long-pending runs.
        app.MapGet(
                "/api/exports",
                async (ExportLogDbContext db) =>
                {
                    var now = DateTimeOffset.UtcNow;
                    var runs = await db.ExportRuns.OrderByDescending(r => r.SequenceNo).ToListAsync();

                    return runs.Select(r =>
                        {
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

                    string? gapWarning = null;
                    if (run.Status == ExportRunStatus.Pending)
                    {
                        var unhandled = await db
                            .ExportRuns.Where(r =>
                                r.SequenceNo < seqNo
                                && r.Status != ExportRunStatus.Released
                                && r.Status != ExportRunStatus.Skipped
                            )
                            .Select(r => r.SequenceNo)
                            .OrderByDescending(n => n)
                            .Take(3)
                            .ToListAsync();

                        if (unhandled.Count > 0)
                            gapWarning =
                                $"Sequence gap: {unhandled.Count} earlier run(s) are unresolved "
                                + $"(#{string.Join(", #", unhandled)}). "
                                + $"Investigate or skip them before releasing #{seqNo}.";
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

        app.MapPost(
                "/api/exports/{seqNo:int}/release",
                async (
                    int seqNo,
                    ReleaseRequest request,
                    HttpContext httpContext,
                    ExportLogDbContext db,
                    AuditService audit
                ) =>
                {
                    if (string.IsNullOrWhiteSpace(request.Approver))
                        return Results.BadRequest("Approver is required.");

                    var operatorName = httpContext.User.Identity!.Name!;

                    if (string.Equals(operatorName, request.Approver, StringComparison.OrdinalIgnoreCase))
                        return Results.BadRequest(
                            "Operator and approver must be different users (four-eyes principle)."
                        );

                    if (!userStore.ContainsKey(request.Approver))
                        return Results.BadRequest(
                            $"Unknown approver: '{request.Approver}'. Only registered users can approve a release."
                        );

                    var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
                    if (run is null)
                        return Results.NotFound();
                    if (run.Status != ExportRunStatus.Pending)
                        return Results.Conflict($"Run #{seqNo} is already {run.Status}.");

                    run.Status = ExportRunStatus.Released;
                    run.OperatedBy = operatorName;
                    run.ApprovedBy = request.Approver;
                    run.ReleasedAt = DateTimeOffset.UtcNow.ToString("O");
                    await db.SaveChangesAsync();

                    await audit.LogAsync(operatorName, "export_released", $"#{seqNo} approved by {request.Approver}");
                    return Results.Ok();
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/exports/{seqNo:int}/deliver",
                async (
                    int seqNo,
                    DeliverRequest request,
                    HttpContext httpContext,
                    ExportLogDbContext db,
                    AuditService audit
                ) =>
                {
                    var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
                    if (run is null)
                        return Results.NotFound();
                    if (run.Status != ExportRunStatus.Released)
                        return Results.BadRequest("Only released runs can be marked as delivered.");
                    if (run.DeliveredAt is not null)
                        return Results.Conflict($"Run #{seqNo} has already been recorded as delivered.");
                    if (request.Notes?.Length > 2000)
                        return Results.BadRequest("Delivery notes cannot exceed 2,000 characters.");

                    var user = httpContext.User.Identity!.Name!;
                    run.DeliveredAt = DateTimeOffset.UtcNow.ToString("O");
                    run.DeliveredBy = user;
                    run.ImportedRecordCount = request.ImportedRecordCount;
                    run.DeliveryNotes = request.Notes;
                    await db.SaveChangesAsync();

                    await audit.LogAsync(user, "export_delivered", $"#{seqNo}");
                    return Results.Ok();
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/exports/{seqNo:int}/skip",
                async (
                    int seqNo,
                    SkipRequest request,
                    HttpContext httpContext,
                    ExportLogDbContext db,
                    AuditService audit
                ) =>
                {
                    var run = await db.ExportRuns.FirstOrDefaultAsync(r => r.SequenceNo == seqNo);
                    if (run is null)
                        return Results.NotFound();
                    if (run.Status is not (ExportRunStatus.Pending or ExportRunStatus.Failed))
                        return Results.Conflict($"Run #{seqNo} has status '{run.Status}' and cannot be skipped.");

                    run.Status = ExportRunStatus.Skipped;
                    await db.SaveChangesAsync();

                    var user = httpContext.User.Identity!.Name!;
                    var detail = string.IsNullOrWhiteSpace(request.Reason)
                        ? $"#{seqNo}"
                        : $"#{seqNo}: {request.Reason}";
                    await audit.LogAsync(user, "export_skipped", detail);
                    return Results.Ok();
                }
            )
            .RequireAuthorization();
    }
}
