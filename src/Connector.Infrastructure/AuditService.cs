using Microsoft.Extensions.Logging;

namespace Connector.Infrastructure;

/// <summary>
/// Writes non-fatal, append-only audit entries to the AuditLog table.
/// Failures are logged as warnings and never propagate — audit must not interrupt business logic.
/// Every detail is scrubbed of credentials (<see cref="ErrorSanitizer.Scrub"/>) before it is stored, whatever
/// the caller put into it.
/// </summary>
public sealed class AuditService(ExportLogDbContext db, ILogger<AuditService> logger)
{
    /// <summary>
    /// Appends one audit entry stamped with the current UTC time. The detail is credential-scrubbed; a failure
    /// to save is logged as a warning and swallowed.
    /// </summary>
    /// <param name="username">Who performed the action.</param>
    /// <param name="action">Machine-readable action name, e.g. export_released.</param>
    /// <param name="detail">Optional free-text context (ids, names, counts); scrubbed of credentials before it is stored.</param>
    public async Task LogAsync(string username, string action, string? detail = null)
    {
        try
        {
            db.AuditLog.Add(
                new AuditLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Username = username,
                    Action = action,
                    Detail = detail is null ? null : ErrorSanitizer.Scrub(detail),
                }
            );
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Audit log write failed (non-fatal): action={Action}", action);
        }
    }
}
