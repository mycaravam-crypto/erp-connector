using Microsoft.Extensions.Logging;

namespace Connector.Infrastructure;

/// <summary>
/// Writes non-fatal, append-only audit entries to the AuditLog table.
/// Failures are logged as warnings and never propagate — audit must not interrupt business logic.
/// Every detail is scrubbed of credentials (<see cref="ErrorSanitizer.Scrub"/>) before it is stored, whatever
/// the caller put into it (Arbeitsauftrag 11).
/// </summary>
public sealed class AuditService(ExportLogDbContext db, ILogger<AuditService> logger)
{
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
