using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Connector.Infrastructure;

/// <summary>
/// One-time, idempotent startup step that encrypts every <c>AppSetting.Value</c> still stored as plaintext —
/// rows written before <see cref="EncryptedStringConverter"/> existed. The converter only encrypts a row when
/// it is saved again, and several keys (e.g. <c>scheduler_config</c>, read on every worker tick) are rarely
/// re-saved, so without this they would stay plaintext on disk indefinitely and trigger the converter's
/// plaintext warning on every single read.
/// </summary>
public static class AppSettingEncryptionMigrator
{
    // Every Data Protection payload starts with the same 4-byte magic header (0x09F0C9F0), which base64url
    // encodes to "CfDJ8". A value with that prefix is ciphertext — even one this key ring can't decrypt
    // (e.g. keys lost), which must be left alone rather than "re-encrypted" as if it were plaintext.
    private const string DataProtectionPayloadPrefix = "CfDJ8";

    /// <summary>Encrypts every plaintext row in place and returns how many there were.</summary>
    public static async Task<int> EncryptPlaintextRowsAsync(
        ExportLogDbContext db,
        IDataProtectionProvider dataProtectionProvider,
        ILogger logger,
        CancellationToken ct = default
    )
    {
        // Read the raw column, bypassing the converter: through EF every plaintext row would be logged as a
        // warning and couldn't be told apart from a decrypted one.
        var plaintextRows = new List<(string Key, string Value)>();
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await connection.OpenAsync(ct);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT \"Key\", \"Value\" FROM \"AppSetting\"";
            await using DbDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var value = reader.GetString(1);
                if (!value.StartsWith(DataProtectionPayloadPrefix, StringComparison.Ordinal))
                    plaintextRows.Add((reader.GetString(0), value));
            }
        }
        finally
        {
            if (openedHere)
                await connection.CloseAsync();
        }

        if (plaintextRows.Count == 0)
            return 0;

        var protector = dataProtectionProvider.CreateProtector(EncryptedStringConverter.Purpose);
        foreach (var (key, value) in plaintextRows)
            await db.Database.ExecuteSqlAsync(
                $"UPDATE \"AppSetting\" SET \"Value\" = {protector.Protect(value)} WHERE \"Key\" = {key}",
                ct
            );

        // Keys only — never values, which include the ERP connection password.
        logger.LogInformation(
            "Encrypted {Count} AppSetting row(s) that were still stored as plaintext: {Keys}",
            plaintextRows.Count,
            string.Join(", ", plaintextRows.Select(r => r.Key))
        );
        return plaintextRows.Count;
    }
}
