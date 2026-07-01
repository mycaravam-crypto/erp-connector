using Microsoft.EntityFrameworkCore;

namespace Connector.Infrastructure;

/// <summary>
/// EF Core DbContext for the export log (SQLite).
/// Tables: ExportRun, AppSetting (key/value config store), AuditLog.
/// Schema is managed via EF Core migrations in Connector.Infrastructure/Migrations/.
/// Startup calls Database.MigrateAsync() — add new changes via <c>dotnet ef migrations add</c>.
/// </summary>
public sealed class ExportLogDbContext(DbContextOptions<ExportLogDbContext> options) : DbContext(options)
{
    public DbSet<ExportRunEntity> ExportRuns => Set<ExportRunEntity>();

    /// <summary>Simple key/value store for persisted UI preferences (e.g. active export columns).</summary>
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

    /// <summary>Audit trail — one row per significant action performed by an authenticated user.</summary>
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExportRunEntity>(e =>
        {
            e.ToTable("ExportRun");
            e.HasKey(r => r.Id);
            // SequenceNo muss einmalig und lückenlos sein — unique constraint deckt Duplikate ab.
            e.HasIndex(r => r.SequenceNo).IsUnique();
        });

        modelBuilder.Entity<AppSettingEntity>(e =>
        {
            e.ToTable("AppSetting");
            e.HasKey(s => s.Key);
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Timestamp);
        });
    }
}

/// <summary>Persistent key/value setting. Key is the primary key; Value stores JSON or plain text.</summary>
public sealed class AppSettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>Single entry in the audit trail. Written by the API on every significant user action.</summary>
public sealed class AuditLogEntry
{
    public int Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Detail { get; set; }
}
