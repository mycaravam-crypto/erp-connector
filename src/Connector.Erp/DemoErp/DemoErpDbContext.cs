using Microsoft.EntityFrameworkCore;

namespace Connector.Erp.DemoErp;

/// <summary>
/// EF Core DbContext für das Demo-ERP (SQLite).
/// Modelliert die vier ERP-Tabellen, die die Pipeline in Iteration 1 liest.
/// </summary>
/// <remarks>
/// Tabellennamen entsprechen der ERP-Namenskonvention (snake_case), damit ein
/// zukünftiger PostgreSQL-Reader dieselben Namen ohne Anpassung verwenden kann.
/// </remarks>
public sealed class DemoErpDbContext(DbContextOptions<DemoErpDbContext> options) : DbContext(options)
{
    public DbSet<ErpMasterdata> Masterdata => Set<ErpMasterdata>();
    public DbSet<ErpSystemConfiguration> SystemConfigurations => Set<ErpSystemConfiguration>();
    public DbSet<ErpArticleStructure> ArticleStructures => Set<ErpArticleStructure>();
    public DbSet<ErpMaintenancePlan> MaintenancePlans => Set<ErpMaintenancePlan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ErpMasterdata>(e =>
        {
            e.ToTable("masterdata");
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasColumnName("id");
            e.Property(m => m.ArticleName).HasColumnName("article_name");
            e.Property(m => m.PartNumber).HasColumnName("part_number");
            e.Property(m => m.Manufacturer).HasColumnName("manufacturer");
        });

        modelBuilder.Entity<ErpSystemConfiguration>(e =>
        {
            e.ToTable("systemconfiguration");
            e.HasKey(sc => sc.Id);
            e.Property(sc => sc.Id).HasColumnName("id");
            e.Property(sc => sc.Serial).HasColumnName("serial");
            e.Property(sc => sc.ArticleId).HasColumnName("article_id");
            e.Property(sc => sc.Status).HasColumnName("status");
            e.Property(sc => sc.CommissionDate).HasColumnName("commission_date");
            e.Property(sc => sc.TechnicianName).HasColumnName("technician_name");
            e.Property(sc => sc.StorageLocation).HasColumnName("storage_location");

            e.HasOne(sc => sc.Article)
                .WithMany(m => m.Instances)
                .HasForeignKey(sc => sc.ArticleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ErpArticleStructure>(e =>
        {
            e.ToTable("articlestructure");
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).HasColumnName("id");
            e.Property(a => a.ParentId).HasColumnName("parent_id");
            e.Property(a => a.ChildId).HasColumnName("child_id");

            // Zwei FKs auf dieselbe Tabelle — explizite Konfiguration vermeidet Ambiguität.
            e.HasOne(a => a.Parent)
                .WithMany(sc => sc.ChildLinks)
                .HasForeignKey(a => a.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.Child)
                .WithMany(sc => sc.ParentLinks)
                .HasForeignKey(a => a.ChildId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ErpMaintenancePlan>(e =>
        {
            e.ToTable("maintenance_plan");
            e.HasKey(mp => mp.Id);
            e.Property(mp => mp.Id).HasColumnName("id");
            e.Property(mp => mp.SystemConfigurationId).HasColumnName("systemconfiguration_id");
            e.Property(mp => mp.Status).HasColumnName("status");
            e.Property(mp => mp.AllocationChartRef).HasColumnName("allocation_chart_ref");

            e.HasOne(mp => mp.SystemConfiguration)
                .WithMany(sc => sc.MaintenancePlans)
                .HasForeignKey(mp => mp.SystemConfigurationId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
