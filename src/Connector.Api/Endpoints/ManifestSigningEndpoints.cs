using Connector.Api.Authorization;
using Connector.Infrastructure;

namespace Connector.Api.Endpoints;

/// <summary>Manage encrypted per-peer HMAC keys without ever returning them to API clients.</summary>
static class ManifestSigningEndpoints
{
    internal static void MapManifestSigningEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/api/settings/manifest-signing",
                async (ExportLogDbContext db) =>
                {
                    var cfg =
                        await db.GetSettingAsync<ManifestSigningSettings>(SettingsKeys.ManifestSigning)
                        ?? new ManifestSigningSettings();
                    return Results.Ok(
                        new
                        {
                            requireSignedConnectorFiles = cfg.RequireSignedConnectorFiles,
                            outboundPeerInstanceId = cfg.OutboundPeerInstanceId,
                            trustedInstanceIds = cfg.TrustedPeerSecrets.Keys.Order(StringComparer.Ordinal).ToArray(),
                        }
                    );
                }
            )
            .WithSummary("List manifest pairing policy and trusted instance IDs without secrets.")
            .RequirePermission(Permissions.SettingsInstance);

        app.MapPut(
                "/api/settings/manifest-signing/peers/{instanceId}",
                async (
                    string instanceId,
                    SetManifestPeerRequest request,
                    ExportLogDbContext db,
                    HttpContext context,
                    AuditService audit
                ) =>
                {
                    if (!Guid.TryParse(instanceId, out _))
                        return Results.BadRequest("Instance ID must be a GUID.");
                    byte[] key;
                    try
                    {
                        key = Convert.FromBase64String(request.SharedSecret ?? "");
                    }
                    catch (FormatException)
                    {
                        return Results.BadRequest("SharedSecret must be base64-encoded.");
                    }
                    if (key.Length < 32)
                        return Results.BadRequest("SharedSecret must contain at least 32 random bytes.");
                    if (await db.IsOwnInstanceIdAsync(instanceId))
                        return Results.BadRequest("Cannot trust this installation's own instance ID.");

                    var cfg =
                        await db.GetSettingAsync<ManifestSigningSettings>(SettingsKeys.ManifestSigning)
                        ?? new ManifestSigningSettings();
                    cfg.TrustedPeerSecrets[instanceId] = Convert.ToBase64String(key);
                    if (request.UseForOutbound)
                        cfg.OutboundPeerInstanceId = instanceId;
                    await db.SetSettingAsync(SettingsKeys.ManifestSigning, cfg);
                    await audit.LogAsync(
                        context.User.Identity!.Name!,
                        "manifest_pairing_updated",
                        $"peer={instanceId} outbound={request.UseForOutbound}"
                    );
                    return Results.NoContent();
                }
            )
            .WithSummary("Register or rotate a shared secret for a trusted connector instance.")
            .RequirePermission(Permissions.SettingsInstance);

        app.MapDelete(
                "/api/settings/manifest-signing/peers/{instanceId}",
                async (string instanceId, ExportLogDbContext db, HttpContext context, AuditService audit) =>
                {
                    if (!Guid.TryParse(instanceId, out _))
                        return Results.BadRequest("Instance ID must be a GUID.");

                    var cfg =
                        await db.GetSettingAsync<ManifestSigningSettings>(SettingsKeys.ManifestSigning)
                        ?? new ManifestSigningSettings();
                    if (!cfg.TrustedPeerSecrets.Remove(instanceId))
                        return Results.NotFound();
                    if (cfg.OutboundPeerInstanceId == instanceId)
                        cfg.OutboundPeerInstanceId = null;
                    await db.SetSettingAsync(SettingsKeys.ManifestSigning, cfg);
                    await audit.LogAsync(
                        context.User.Identity!.Name!,
                        "manifest_pairing_removed",
                        $"peer={instanceId}"
                    );
                    return Results.NoContent();
                }
            )
            .WithSummary("Remove a trusted connector instance and its signing key.")
            .RequirePermission(Permissions.SettingsInstance);

        app.MapPut(
                "/api/settings/manifest-signing/policy",
                async (
                    SetManifestSigningPolicyRequest request,
                    ExportLogDbContext db,
                    HttpContext context,
                    AuditService audit
                ) =>
                {
                    var cfg =
                        await db.GetSettingAsync<ManifestSigningSettings>(SettingsKeys.ManifestSigning)
                        ?? new ManifestSigningSettings();
                    cfg.RequireSignedConnectorFiles = request.RequireSignedConnectorFiles;
                    await db.SetSettingAsync(SettingsKeys.ManifestSigning, cfg);
                    await audit.LogAsync(
                        context.User.Identity!.Name!,
                        "manifest_signing_policy_updated",
                        $"required={request.RequireSignedConnectorFiles}"
                    );
                    return Results.NoContent();
                }
            )
            .WithSummary("Require authenticated connector manifests for paired imports.")
            .RequirePermission(Permissions.SettingsInstance);
    }

    private sealed record SetManifestPeerRequest(string? SharedSecret, bool UseForOutbound);

    private sealed record SetManifestSigningPolicyRequest(bool RequireSignedConnectorFiles);
}
