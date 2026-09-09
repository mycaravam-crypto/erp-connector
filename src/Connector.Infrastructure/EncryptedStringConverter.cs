using System.Linq.Expressions;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Logging;

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
        catch (CryptographicException)
        {
            // Security-review finding SR-11: this used to fail silently, forever — indistinguishable from
            // the one-time pre-encryption migration case this fallback exists for. A hit here means this
            // row's Value wasn't a Data Protection payload at all: either that intended one-time case (an
            // old row not yet re-saved), or something less innocuous — a manual DB edit, or a future bug
            // that wrote plaintext into an encrypted column. Both look identical to Unprotect, so this can't
            // tell them apart — it can only make sure neither goes unnoticed. A handful of warnings right
            // after this fix ships is expected (each row stops firing once AppSettingsStore.SetSettingAsync
            // re-saves it); one still firing well after that is the signal an operator needs to investigate.
            logger.LogWarning(
                "An AppSetting.Value column was read as plaintext instead of a Data Protection payload — "
                    + "either a pre-encryption row awaiting its next save, or encryption was bypassed. "
                    + "Investigate if this persists."
            );
            return value;
        }
    }
}
