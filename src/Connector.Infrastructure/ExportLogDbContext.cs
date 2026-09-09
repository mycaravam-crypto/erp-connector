using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Connector.Infrastructure;

/// <summary>Canonical <see cref="AppSettingEntity"/> keys — one constant per stored setting, so a typo is a compile error instead of a silent runtime bug.</summary>
public static class SettingsKeys
{
    public const string ErpConnection = "erp_connection";
    public const string ExportMapping = "export_mapping";
    public const string ExportPresets = "export_presets";
    public const string SchedulerConfig = "scheduler_config";
    public const string GdprDeniedFields = "gdpr_denied_fields";
}

/// <summary>
/// Typed find/deserialize/upsert helpers over <see cref="ExportLogDbContext.AppSettings"/>, replacing the
/// repeated find-null-check-deserialize (read) and find-null-check-add-or-mutate-save (write) pattern that
/// used to be hand-rolled at every call site.
/// </summary>
public static class AppSettingsStore
{
    /// <summary>Returns the raw stored JSON for <paramref name="key"/>, or null if unset.</summary>
    public static async Task<string?> GetSettingRawAsync(this ExportLogDbContext db, string key) =>
        (await db.AppSettings.FindAsync(key))?.Value;

    /// <summary>Returns the setting deserialized as <typeparamref name="T"/>, or default if unset.</summary>
    public static async Task<T?> GetSettingAsync<T>(this ExportLogDbContext db, string key)
    {
        var raw = await db.GetSettingRawAsync(key);
        return raw is null ? default : JsonSerializer.Deserialize<T>(raw);
    }

    /// <summary>Serializes <paramref name="value"/> and upserts it under <paramref name="key"/>, saving immediately.</summary>
    public static async Task SetSettingAsync<T>(this ExportLogDbContext db, string key, T value)
    {
        var serialized = JsonSerializer.Serialize(value);
        var setting = await db.AppSettings.FindAsync(key);
        if (setting is null)
            db.AppSettings.Add(new AppSettingEntity { Key = key, Value = serialized });
        else
            setting.Value = serialized;
        await db.SaveChangesAsync();
    }
}

/// <summary>
/// EF Core DbContext for the export log (SQLite).
/// Tables: ExportRun, AppSetting (key/value config store), AuditLog.
/// Schema is managed via EF Core migrations in Connector.Infrastructure/Migrations/.
/// Startup calls Database.MigrateAsync() — add new changes via <c>dotnet ef migrations add</c>.
/// </summary>
public sealed class ExportLogDbContext(
    DbContextOptions<ExportLogDbContext> options,
    IDataProtectionProvider dataProtectionProvider,
    // Optional — resolved from DI in production (see Program.cs's AddDbContext<ExportLogDbContext>) but
    // defaulted rather than required so every test that constructs this context directly with `new` (there's
    // no DI container in play there) keeps compiling unchanged; SR-11's plaintext-fallback warning just goes
    // nowhere for those instead of failing to construct.
    ILogger<EncryptedStringConverter>? encryptedStringConverterLogger = null
) : DbContext(options)
{
    public DbSet<ExportRunEntity> ExportRuns => Set<ExportRunEntity>();

    /// <summary>Simple key/value store for persisted UI preferences (e.g. active export columns).</summary>
    public DbSet<AppSettingEntity> AppSettings => Set<AppSettingEntity>();

    /// <summary>Audit trail — one row per significant action performed by an authenticated user.</summary>
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    /// <summary>Phase 14 export definitions — the generic, tree-based replacement for the legacy
    /// single mapping + presets.</summary>
    public DbSet<ExportDefinitionEntity> ExportDefinitions => Set<ExportDefinitionEntity>();

    /// <summary>Execution history for <see cref="ExportDefinitions"/> — one row per run.</summary>
    public DbSet<ExportDefinitionRunEntity> ExportDefinitionRuns => Set<ExportDefinitionRunEntity>();

    /// <summary>Phase 17 import definitions — the write-side counterpart to <see cref="ExportDefinitions"/>.</summary>
    public DbSet<ImportDefinitionEntity> ImportDefinitions => Set<ImportDefinitionEntity>();

    /// <summary>Execution history for <see cref="ImportDefinitions"/> — one row per run.</summary>
    public DbSet<ImportRunEntity> ImportRuns => Set<ImportRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ExportRunEntity>(e =>
        {
            e.ToTable("ExportRun");
            e.HasKey(r => r.Id);
            // SequenceNo muss einmalig und lückenlos sein — unique constraint deckt Duplikate ab.
            e.HasIndex(r => r.SequenceNo).IsUnique();
            // Status doubles as the optimistic-concurrency token: EF includes its as-loaded value in
            // every UPDATE's WHERE clause, so two concurrent release/skip/deliver calls against the same
            // row can't both silently win a check-then-act race (security audit finding) — the loser's
            // SaveChangesAsync throws DbUpdateConcurrencyException instead of overwriting the winner's
            // write. No schema change: this is purely how EF shapes the UPDATE statement.
            e.Property(r => r.Status).IsConcurrencyToken();
        });

        modelBuilder.Entity<AppSettingEntity>(e =>
        {
            e.ToTable("AppSetting");
            e.HasKey(s => s.Key);
            // Encrypted at rest — see EncryptedStringConverter's doc comment for why (this table holds the
            // ERP connection password among other settings) and why it's applied to the whole column rather
            // than special-cased per key.
            e.Property(s => s.Value)
                .HasConversion(
                    new EncryptedStringConverter(
                        dataProtectionProvider,
                        encryptedStringConverterLogger ?? NullLogger<EncryptedStringConverter>.Instance
                    )
                );
        });

        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Timestamp);
        });

        modelBuilder.Entity<ExportDefinitionEntity>(e =>
        {
            e.ToTable("ExportDefinition");
            e.HasKey(d => d.Id);
        });

        modelBuilder.Entity<ExportDefinitionRunEntity>(e =>
        {
            e.ToTable("ExportDefinitionRun");
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.ExportDefinitionId);
        });

        modelBuilder.Entity<ImportDefinitionEntity>(e =>
        {
            e.ToTable("ImportDefinition");
            e.HasKey(d => d.Id);
        });

        modelBuilder.Entity<ImportRunEntity>(e =>
        {
            e.ToTable("ImportRun");
            e.HasKey(r => r.Id);
            e.HasIndex(r => r.ImportDefinitionId);
            // Same source file staged twice for the same definition is a no-op, not a second pending
            // review (Open Decision #13) — enforced at the database level so a race between two worker
            // polls can't both insert it.
            e.HasIndex(r => new { r.ImportDefinitionId, r.Sha256Checksum }).IsUnique();
            // Same optimistic-concurrency treatment as ExportRunEntity.Status — see that property's comment.
            e.Property(r => r.Status).IsConcurrencyToken();
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
