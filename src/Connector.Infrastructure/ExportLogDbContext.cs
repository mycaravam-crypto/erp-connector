using Microsoft.EntityFrameworkCore;

namespace Connector.Infrastructure;

/// <summary>
/// EF Core DbContext für das Export-Log (SQLite).
/// Einzige Tabelle: ExportRun — eine Zeile pro Export-Run.
/// </summary>
/// <remarks>
/// Migrations werden nicht automatisch beim Start angewendet.
/// Expliziter CLI-Befehl: dotnet ef database update
/// </remarks>
public sealed class ExportLogDbContext(DbContextOptions<ExportLogDbContext> options) : DbContext(options)
{
    public DbSet<ExportRunEntity> ExportRuns => Set<ExportRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExportRunEntity>(e =>
        {
            e.ToTable("ExportRun");
            e.HasKey(r => r.Id);
            // SequenceNo muss einmalig und lückenlos sein — unique constraint deckt Duplikate ab.
            e.HasIndex(r => r.SequenceNo).IsUnique();
        });
    }
}
