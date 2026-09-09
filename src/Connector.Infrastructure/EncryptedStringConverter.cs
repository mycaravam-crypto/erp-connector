using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Connector.Infrastructure;

/// <summary>
/// Encrypts a string column at rest via ASP.NET Core Data Protection. Applied to
/// <see cref="AppSettingEntity.Value"/> (security audit finding: that column stores the ERP connection
/// config — including its password — and was previously written as plain JSON straight into the SQLite
/// file, so a stolen backup or disk snapshot handed over the live ERP database password). EF Core runs the
/// conversion on every SaveChanges/materialization, so every caller — <see cref="AppSettingsStore"/>'s
/// helpers and the several call sites that read <see cref="ExportLogDbContext.AppSettings"/> directly —
/// keeps seeing plaintext JSON; only the bytes actually persisted to disk are ciphertext.
/// </summary>
internal sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    // Scopes key derivation to this exact column, per Data Protection's purpose-string convention — versioned
    // so a future rekey/format change can introduce "...v2" without touching already-encrypted rows.
    private const string Purpose = "Connector.Infrastructure.AppSettingEntity.Value.v1";

    public EncryptedStringConverter(IDataProtectionProvider provider)
        : base(BuildProtect(provider), BuildUnprotect(provider)) { }

    private static Expression<Func<string, string>> BuildProtect(IDataProtectionProvider provider)
    {
        var protector = provider.CreateProtector(Purpose);
        return value => protector.Protect(value);
    }

    private static Expression<Func<string, string>> BuildUnprotect(IDataProtectionProvider provider)
    {
        var protector = provider.CreateProtector(Purpose);
        return value => TryUnprotect(protector, value);
    }

    // Tolerates rows written before this converter existed (plain JSON, not a Data Protection payload) so
    // upgrading doesn't hard-break an existing database — they're read as-is once, then re-encrypted the
    // next time AppSettingsStore.SetSettingAsync saves that key.
    private static string TryUnprotect(IDataProtector protector, string value)
    {
        try
        {
            return protector.Unprotect(value);
        }
        catch (CryptographicException)
        {
            return value;
        }
    }
}
