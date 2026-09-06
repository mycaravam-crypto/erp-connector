using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Connector.Api.Endpoints;

/// <summary>
/// Slice 3 of Phase 17 (import-definitions.md §3 steps 6-8, §5): the four-eyes commit path for a staged
/// <c>ImportRunEntity</c>. Deliberately just release/reject — CRUD, preview, and run history for
/// <c>ImportDefinitionEntity</c> are Slice 5's <c>ImportDefinitionEndpoints.cs</c>, and how a run reaches
/// <see cref="ImportRunStatus.PendingReview"/> in the first place is Slice 4's file-watcher trigger (both out
/// of this slice's scope per its own issue). The actual commit/rollback/audit logic lives in
/// <see cref="ImportRunReleaser"/>, not here, so it's testable without going through HTTP — this file only
/// does request validation and status-code mapping.
/// </summary>
static class ImportRunEndpoints
{
    internal static void MapImportRunEndpoints(this WebApplication app, IReadOnlyDictionary<string, string> userStore)
    {
        app.MapPost(
                "/api/import-runs/{id:int}/release",
                async (
                    int id,
                    ReleaseRequest request,
                    HttpContext httpContext,
                    ExportLogDbContext db,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var operatorName = httpContext.User.Identity!.Name!;

                    var approvalError = FourEyesReview.ValidateApprover(operatorName, request.Approver, userStore);
                    if (approvalError is not null)
                        return Results.BadRequest(approvalError);

                    var run = await db.ImportRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
                    if (run is null)
                        return Results.NotFound();
                    if (run.Status != ImportRunStatus.PendingReview)
                        return Results.Conflict($"Import run #{id} is already {run.Status}.");

                    await ImportRunReleaser.ReleaseAsync(db, run, operatorName, request.Approver, audit, ct);

                    return run.Status == ImportRunStatus.Failed
                        ? Results.Problem(detail: run.ErrorMessage, statusCode: 500)
                        : Results.Ok(ToDto(run));
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/import-runs/{id:int}/reject",
                async (
                    int id,
                    HttpContext httpContext,
                    ExportLogDbContext db,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var run = await db.ImportRuns.FirstOrDefaultAsync(r => r.Id == id, ct);
                    if (run is null)
                        return Results.NotFound();
                    if (run.Status != ImportRunStatus.PendingReview)
                        return Results.Conflict($"Import run #{id} is already {run.Status}.");

                    await ImportRunReleaser.RejectAsync(db, run, httpContext.User.Identity!.Name!, audit, ct);
                    return Results.Ok(ToDto(run));
                }
            )
            .RequireAuthorization();
    }

    private static ImportRunDto ToDto(ImportRunEntity r) =>
        new(
            r.Id,
            r.ImportDefinitionId,
            r.Status,
            r.RecordCount,
            r.MatchedCount,
            r.ChangedCount,
            r.UnchangedCount,
            r.RejectedCount,
            r.ConflictCount,
            r.InvalidCount,
            r.OperatedBy,
            r.ApprovedBy,
            r.ReleasedAt,
            r.ErrorMessage
        );
}
