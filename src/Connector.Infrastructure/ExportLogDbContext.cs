using Microsoft.EntityFrameworkCore;

namespace Connector.Infrastructure;

/// <summary>
/// EF Core DbContext für das Export-Log (SQLite).
/// Tabellen: ExportRun (eine Zeile pro Run), AppSetting (Key/Value-Store für Konfiguration).
/// </summary>
/// <remarks>
/// Migrations werden nicht automatisch beim Start angewendet.
/// Additive Schemaänderungen werden beim Start über ExecuteSqlRawAsync eingespielt
/// (ALTER TABLE ADD COLUMN IF NOT EXISTS / CREATE TABLE IF NOT EXISTS).
/// </remarks>
public sealed class ExportLogDbContext(DbContextOptions<ExportLogDbContext> options) : DbContext(options)
{
    public DbSet<ExportRunEntity> ExportRuns => Set<ExportRunEntity>();

    /// <summary>Simple key/value store for persisted UI preferences (e.g. active export columns).</summary>
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

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
    }
}

/// <summary>Persistent key/value setting. Key is the primary key; Value stores JSON or plain text.</summary>
public sealed class AppSettingEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
