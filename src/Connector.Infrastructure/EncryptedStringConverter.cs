using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;

namespace Connector.Infrastructure;

/// <summary>
/// Encrypts a string column at rest via ASP.NET Core Data Protection. Applied to
/// <see cref="AppSettingEntity.Value"/>, which stores the ERP connection config including its password, so
/// a stolen backup or disk snapshot of the SQLite file doesn't expose it. EF Core runs the
/// conversion on every SaveChanges/materialization, so every caller — <see cref="AppSettingsStore"/>'s
/// helpers and the several call sites that read <see cref="ExportLogDbContext.AppSettings"/> directly —
/// keeps seeing plaintext JSON; only the bytes actually persisted to disk are ciphertext.
/// </summary>
// Public rather than internal: ExportLogDbContext's constructor (public) now takes an
// ILogger<EncryptedStringConverter> parameter, and a public member can't
// expose a less-accessible type.
public sealed class EncryptedStringConverter : ValueConverter<string, string>
{
    // Scopes key derivation to this exact column, per Data Protection's purpose-string convention — versioned
    // so a future rekey/format change can introduce "...v2" without touching already-encrypted rows.
    internal const string Purpose = "Connector.Infrastructure.AppSettingEntity.Value.v1";

    public EncryptedStringConverter(IDataProtectionProvider provider, ILogger<EncryptedStringConverter> logger)
        : base(BuildProtect(provider), BuildUnprotect(provider, logger)) { }

    private static Expression<Func<string, string>> BuildProtect(IDataProtectionProvider provider)
    {
        var protector = provider.CreateProtector(Purpose);
        return value => protector.Protect(value);
    }

    private static Expression<Func<string, string>> BuildUnprotect(
        IDataProtectionProvider provider,
        ILogger<EncryptedStringConverter> logger
    )
    {
        var protector = provider.CreateProtector(Purpose);
        return value => TryUnprotect(protector, logger, value);
    }

    // Tolerates rows written before this converter existed (plain JSON, not a Data Protection payload) so
    // upgrading doesn't hard-break an existing database — they're read as-is once, then re-encrypted the
    // next time AppSettingsStore.SetSettingAsync saves that key.
    private static string TryUnprotect(IDataProtector protector, ILogger<EncryptedStringConverter> logger, string value)
    {
        try
        {
            return protector.Unprotect(value);
        }
        catch (CryptographicException ex)
        {
            // The row's Value wasn't a Data Protection payload: either an old plaintext row not yet
            // re-saved (it stops firing once AppSettingsStore.SetSettingAsync re-saves it), or something less
            // innocuous — a manual DB edit, or a bug that wrote plaintext into an encrypted column. Unprotect
            // can't tell them apart, so the fallback is logged as a warning rather than passing silently.
            logger.LogWarning(
                ex,
                "An AppSetting.Value column was read as plaintext instead of a Data Protection payload — "
                    + "either a pre-encryption row awaiting its next save, or encryption was bypassed. "
                    + "Investigate if this persists."
            );
            return value;
        }
    }
}
