using System.Text.RegularExpressions;
using Connector.Infrastructure;

namespace Connector.Api.Endpoints;

static partial class BrandingEndpoints
{
    private const int MaxLogoBytes = 2 * 1024 * 1024;
    private const int MaxFaviconBytes = 512 * 1024;
    private const int MaxBackgroundBytes = 4 * 1024 * 1024;
    private const int MaxAppNameLength = 60;

    internal static void MapBrandingEndpoints(this WebApplication app)
    {
        // Unauthenticated: the login screen, browser tab title, and favicon all need this before sign-in.
        app.MapGet(
            "/api/branding",
            async (ExportLogDbContext db) =>
            {
                var stored = await db.GetSettingAsync<BrandingConfig>(SettingsKeys.Branding);
                return Results.Ok(stored ?? new BrandingConfig(null, null, null, null));
            }
        );

        app.MapPut(
                "/api/branding",
                async (BrandingConfig dto, ExportLogDbContext db, HttpContext httpContext, AuditService audit) =>
                {
                    if (dto.AppName is { Length: > MaxAppNameLength })
                        return Results.BadRequest($"App name must be {MaxAppNameLength} characters or fewer.");
                    if (!IsValidImageDataUrl(dto.LogoDataUrl, MaxLogoBytes, out var logoError))
                        return Results.BadRequest($"Logo image {logoError}.");
                    if (!IsValidImageDataUrl(dto.FaviconDataUrl, MaxFaviconBytes, out var faviconError))
                        return Results.BadRequest($"Favicon image {faviconError}.");
                    if (!IsValidImageDataUrl(dto.BackgroundImageDataUrl, MaxBackgroundBytes, out var backgroundError))
                        return Results.BadRequest($"Background image {backgroundError}.");

                    var normalized = new BrandingConfig(
                        string.IsNullOrWhiteSpace(dto.AppName) ? null : dto.AppName.Trim(),
                        string.IsNullOrEmpty(dto.LogoDataUrl) ? null : dto.LogoDataUrl,
                        string.IsNullOrEmpty(dto.FaviconDataUrl) ? null : dto.FaviconDataUrl,
                        string.IsNullOrEmpty(dto.BackgroundImageDataUrl) ? null : dto.BackgroundImageDataUrl
                    );

                    await db.SetSettingAsync(SettingsKeys.Branding, normalized);
                    var appName = normalized.AppName is null ? "default" : normalized.AppName;
                    var logo = normalized.LogoDataUrl is null ? "unset" : "set";
                    var favicon = normalized.FaviconDataUrl is null ? "unset" : "set";
                    var background = normalized.BackgroundImageDataUrl is null ? "unset" : "set";
                    await audit.LogAsync(
                        httpContext.User.Identity!.Name!,
                        "branding_updated",
                        $"appName={appName} logo={logo} favicon={favicon} background={background}"
                    );
                    return Results.Ok(normalized);
                }
            )
            .RequireAuthorization();
    }

    /// <summary>
    /// A null/empty value is valid — it means "clear this asset and fall back to the default". Anything
    /// else must be a same-origin base64 image data URL under the given size cap; this is the only place
    /// user-supplied asset bytes are accepted, so it also guards against arbitrarily large payloads being
    /// stored in the settings table.
    /// </summary>
    private static bool IsValidImageDataUrl(string? dataUrl, int maxBytes, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(dataUrl))
            return true;

        var match = DataUrlPattern().Match(dataUrl);
        if (!match.Success)
            return Fail("must be a base64 data URL (png, jpeg, svg, webp, or ico)", out error);

        // Base64 encodes 3 bytes as 4 chars; padding makes this an upper-bound estimate, which is fine
        // for a size cap.
        var approxDecodedBytes = (match.Groups["data"].Value.Length * 3) / 4;
        if (approxDecodedBytes > maxBytes)
            return Fail($"must be smaller than {FormatMaxSize(maxBytes)}", out error);

        return true;

        static bool Fail(string message, out string? error)
        {
            error = message;
            return false;
        }
    }

    private static string FormatMaxSize(int maxBytes) =>
        maxBytes % (1024 * 1024) == 0 ? $"{maxBytes / (1024 * 1024)}MB" : $"{maxBytes / 1024}KB";

    [GeneratedRegex(
        @"^data:image/(png|jpe?g|svg\+xml|webp|x-icon|vnd\.microsoft\.icon);base64,(?<data>[A-Za-z0-9+/]+=*)$"
    )]
    private static partial Regex DataUrlPattern();
}
