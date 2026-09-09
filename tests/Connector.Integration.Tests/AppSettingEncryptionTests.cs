using System.Text.Json;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="EncryptedStringConverter"/> (security audit finding: <c>AppSetting.Value</c> —
/// which holds the ERP connection config, password included — was written as plain JSON straight into the
/// SQLite file). Confirms the bytes actually persisted are not plaintext, that the public
/// <see cref="AppSettingsStore"/> API still round-trips correctly through a fresh, untracked read (not just
/// served back from the writer's own change tracker), and that a row written before this converter existed
/// (plain JSON) is still readable rather than hard-breaking on upgrade.
/// </summary>
public sealed class AppSettingEncryptionTests
{
    // Captures Warning-level log calls so SR-11's "plaintext fallback must be observable" fix has
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
        Assert.DoesNotContain(ErpTestFixture.Config.Host, rawStoredValue, StringComparison.Ordinal);

        // Fresh context, same key ring: forces a real decrypt rather than reading back the writer's own
        // still-plaintext tracked entity.
        await using var reader = new ExportLogDbContext(options, dataProtectionProvider);
        var roundTripped = await reader.GetSettingAsync<ErpConnectionConfig>(SettingsKeys.ErpConnection);
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

        var roundTripped = await db.GetSettingAsync<ErpConnectionConfig>(SettingsKeys.ErpConnection);
        Assert.Equal(ErpTestFixture.Config, roundTripped);
    }

    // Security-review finding SR-11: the plaintext fallback above must never again go unnoticed — a row
    // still hitting it long after the encryption migration shipped is a signal an operator needs, not a
    // silently-accepted no-op.
    [Fact]
    public async Task PreEncryptionPlaintextRow_LogsWarningOnEachRead()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExportLogDbContext>().UseSqlite(connection).Options;
        var logger = new CapturingLogger();
        await using var db = new ExportLogDbContext(options, new EphemeralDataProtectionProvider(), logger);
        await db.Database.EnsureCreatedAsync();

        var plainJson = JsonSerializer.Serialize(ErpTestFixture.Config);
        await WriteRawColumnValueAsync(connection, SettingsKeys.ErpConnection, plainJson);

        await db.GetSettingAsync<ErpConnectionConfig>(SettingsKeys.ErpConnection);

        Assert.Contains(logger.Warnings, w => w.Contains("plaintext", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ProperlyEncryptedRow_LogsNoWarningOnRead()
    {
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExportLogDbContext>().UseSqlite(connection).Options;
        var writerLogger = new CapturingLogger();
        var readerLogger = new CapturingLogger();

        await using (var writer = new ExportLogDbContext(options, dataProtectionProvider, writerLogger))
        {
            await writer.Database.EnsureCreatedAsync();
            await writer.SetSettingAsync(SettingsKeys.ErpConnection, ErpTestFixture.Config);
        }
        Assert.Empty(writerLogger.Warnings);

        await using var reader = new ExportLogDbContext(options, dataProtectionProvider, readerLogger);
        await reader.GetSettingAsync<ErpConnectionConfig>(SettingsKeys.ErpConnection);

        Assert.Empty(readerLogger.Warnings);
    }
}
