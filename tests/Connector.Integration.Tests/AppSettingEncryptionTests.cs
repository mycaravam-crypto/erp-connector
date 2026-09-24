using System.Text.Json;
using Connector.Core.DataSources;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="EncryptedStringConverter"/> (<c>AppSetting.Value</c> holds the ERP connection
/// config, password included). Confirms the bytes actually persisted are not plaintext, that the public
/// <see cref="AppSettingsStore"/> API still round-trips correctly through a fresh, untracked read (not just
/// served back from the writer's own change tracker), and that a row written before this converter existed
/// (plain JSON) is still readable rather than hard-breaking on upgrade.
/// </summary>
public sealed class AppSettingEncryptionTests
{
    // Captures Warning-level log calls so the "plaintext fallback must be observable" behavior has
    // something to assert against, without pulling in a fake-logger test package for one call site.
    private sealed class CapturingLogger : ILogger<EncryptedStringConverter>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    private static async Task<string> ReadRawColumnValueAsync(SqliteConnection connection, string key)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Value FROM AppSetting WHERE Key = @key";
        var param = cmd.CreateParameter();
        param.ParameterName = "@key";
        param.Value = key;
        cmd.Parameters.Add(param);
        return (string)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task WriteRawColumnValueAsync(SqliteConnection connection, string key, string value)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO AppSetting (Key, Value) VALUES (@key, @value)";
        var keyParam = cmd.CreateParameter();
        keyParam.ParameterName = "@key";
        keyParam.Value = key;
        cmd.Parameters.Add(keyParam);
        var valueParam = cmd.CreateParameter();
        valueParam.ParameterName = "@value";
        valueParam.Value = value;
        cmd.Parameters.Add(valueParam);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task ErpConnection_StoredBytesAreNotPlaintext_ButRoundTripsCorrectly()
    {
        // One key ring shared by two ExportLogDbContext instances on the same in-memory database — mirrors
        // the real app, where every request gets its own scoped context backed by the same persisted key
        // ring, not the single long-lived context LocalDb hands a whole test.
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExportLogDbContext>().UseSqlite(connection).Options;

        await using (var writer = new ExportLogDbContext(options, dataProtectionProvider))
        {
            await writer.Database.EnsureCreatedAsync();
            await writer.SetSettingAsync(SettingsKeys.ErpConnection, ErpTestFixture.Config);
        }

        var rawStoredValue = await ReadRawColumnValueAsync(connection, SettingsKeys.ErpConnection);
        Assert.DoesNotContain(ErpTestFixture.Config.Password, rawStoredValue, StringComparison.Ordinal);
        Assert.DoesNotContain(ErpTestFixture.Config.Host!, rawStoredValue, StringComparison.Ordinal);

        // Fresh context, same key ring: forces a real decrypt rather than reading back the writer's own
        // still-plaintext tracked entity.
        await using var reader = new ExportLogDbContext(options, dataProtectionProvider);
        var roundTripped = await reader.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection);
        Assert.Equal(ErpTestFixture.Config, roundTripped);
    }

    [Fact]
    public async Task PreEncryptionPlaintextRow_IsStillReadable()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExportLogDbContext>().UseSqlite(connection).Options;
        await using var db = new ExportLogDbContext(options, new EphemeralDataProtectionProvider());
        await db.Database.EnsureCreatedAsync();

        // Simulate a row written before EncryptedStringConverter existed: plain JSON inserted directly,
        // bypassing EF's converter entirely.
        var plainJson = JsonSerializer.Serialize(ErpTestFixture.Config);
        await WriteRawColumnValueAsync(connection, SettingsKeys.ErpConnection, plainJson);

        var roundTripped = await db.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection);
        Assert.Equal(ErpTestFixture.Config, roundTripped);
    }

    // The plaintext fallback above must never go unnoticed — a row still hitting it is a signal an
    // operator needs, not a silently-accepted no-op.
    [Fact]
    public async Task PreEncryptionPlaintextRow_LogsWarningOnEachRead()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        // EF Core caches its compiled model (and the ValueConverter closures baked into it) per context
        // type by default — reused across every ExportLogDbContext instance in the process regardless of
        // constructor args. Since EncryptedStringConverter closes over this specific `logger`, this test
        // needs EF's own documented escape hatch for stateful value converters, or it'd silently observe
        // whichever logger some *other*, earlier-constructed ExportLogDbContext happened to pass in.
        var options = new DbContextOptionsBuilder<ExportLogDbContext>()
            .UseSqlite(connection)
            .EnableServiceProviderCaching(false)
            .Options;
        var logger = new CapturingLogger();
        await using var db = new ExportLogDbContext(options, new EphemeralDataProtectionProvider(), logger);
        await db.Database.EnsureCreatedAsync();

        var plainJson = JsonSerializer.Serialize(ErpTestFixture.Config);
        await WriteRawColumnValueAsync(connection, SettingsKeys.ErpConnection, plainJson);

        await db.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection);

        Assert.Contains(logger.Warnings, w => w.Contains("plaintext", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProperlyEncryptedRow_LogsNoWarningOnRead()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        // See PreEncryptionPlaintextRow_LogsWarningOnEachRead's comment on EnableServiceProviderCaching.
        var options = new DbContextOptionsBuilder<ExportLogDbContext>()
            .UseSqlite(connection)
            .EnableServiceProviderCaching(false)
            .Options;
        var writerLogger = new CapturingLogger();
        var readerLogger = new CapturingLogger();

        await using (var writer = new ExportLogDbContext(options, dataProtectionProvider, writerLogger))
        {
            await writer.Database.EnsureCreatedAsync();
            await writer.SetSettingAsync(SettingsKeys.ErpConnection, ErpTestFixture.Config);
        }
        Assert.Empty(writerLogger.Warnings);

        await using var reader = new ExportLogDbContext(options, dataProtectionProvider, readerLogger);
        await reader.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection);

        Assert.Empty(readerLogger.Warnings);
    }

    // AppSettingEncryptionMigrator: rows written before the converter existed must not stay plaintext on
    // disk (and keep logging the plaintext warning) just because nobody happens to re-save that key.
    [Fact]
    public async Task Migrator_EncryptsPlaintextRows_LeavesCiphertextAlone_AndIsIdempotent()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        // See PreEncryptionPlaintextRow_LogsWarningOnEachRead's comment on EnableServiceProviderCaching.
        var options = new DbContextOptionsBuilder<ExportLogDbContext>()
            .UseSqlite(connection)
            .EnableServiceProviderCaching(false)
            .Options;

        await using (var writer = new ExportLogDbContext(options, dataProtectionProvider))
        {
            await writer.Database.EnsureCreatedAsync();
            await writer.SetSettingAsync("already_encrypted", "keep me");
        }
        var plainJson = JsonSerializer.Serialize(ErpTestFixture.Config);
        await WriteRawColumnValueAsync(connection, SettingsKeys.ErpConnection, plainJson);
        await WriteRawColumnValueAsync(connection, "scheduler_config", "{\"Enabled\":true}");
        // Ciphertext from a key ring this app no longer has: must never be treated as plaintext.
        var foreignCiphertext = new EphemeralDataProtectionProvider()
            .CreateProtector("Connector.Infrastructure.AppSettingEntity.Value.v1")
            .Protect("from a lost key ring");
        await WriteRawColumnValueAsync(connection, "foreign", foreignCiphertext);
        var encryptedBefore = await ReadRawColumnValueAsync(connection, "already_encrypted");

        await using (var db = new ExportLogDbContext(options, dataProtectionProvider))
        {
            var migrated = await AppSettingEncryptionMigrator.EncryptPlaintextRowsAsync(
                db,
                dataProtectionProvider,
                Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
            );
            Assert.Equal(2, migrated);
            Assert.Equal(
                0,
                await AppSettingEncryptionMigrator.EncryptPlaintextRowsAsync(
                    db,
                    dataProtectionProvider,
                    Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
                )
            );
        }

        var rawConnection = await ReadRawColumnValueAsync(connection, SettingsKeys.ErpConnection);
        Assert.StartsWith("CfDJ8", rawConnection, StringComparison.Ordinal);
        Assert.DoesNotContain(ErpTestFixture.Config.Password, rawConnection, StringComparison.Ordinal);
        Assert.StartsWith(
            "CfDJ8",
            await ReadRawColumnValueAsync(connection, "scheduler_config"),
            StringComparison.Ordinal
        );
        Assert.Equal(encryptedBefore, await ReadRawColumnValueAsync(connection, "already_encrypted"));
        Assert.Equal(foreignCiphertext, await ReadRawColumnValueAsync(connection, "foreign"));

        // Reads now decrypt normally — same values, and no plaintext warning any more.
        var logger = new CapturingLogger();
        await using var reader = new ExportLogDbContext(options, dataProtectionProvider, logger);
        Assert.Equal(ErpTestFixture.Config, await reader.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection));
        Assert.Equal("{\"Enabled\":true}", (await reader.AppSettings.FindAsync("scheduler_config"))!.Value);
        Assert.Equal("keep me", await reader.GetSettingAsync<string>("already_encrypted"));
        Assert.Empty(logger.Warnings);
    }
}
