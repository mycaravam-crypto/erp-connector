using System.Net;
using Connector.Core.DataSources;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.ServiceNow;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Connector.Api.Endpoints;

static class ConnectionEndpoints
{
    // The ERP connection target is deliberately admin-configurable to an arbitrary host on the operator's
    // own network — that's the whole point of this endpoint — so this is not a general private-network
    // block (that would break every real deployment, where the ERP database lives on a private IP). It
    // blocks only the link-local range that hosts cloud-provider instance-metadata services
    // (169.254.169.254 on AWS/GCP/Azure/DigitalOcean and IPv6 link-local equivalents), the classic
    // SSRF-to-credential-theft target, which is never a legitimate ERP database address.
    private static readonly IPNetwork[] BlockedNetworks =
    [
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("fe80::/10"),
    ];

    // Security-review finding SR-03: SslMode was previously hardcoded to Prefer, which silently downgrades
    // to an unencrypted connection whenever the server doesn't offer TLS. Validated here against Npgsql's
    // own enum names rather than a hand-maintained list, so save-time validation and
    // DynamicExportService.ParseSslMode can never quietly drift apart on what's "valid."
    internal static bool IsValidSslMode(string? sslMode) =>
        string.IsNullOrWhiteSpace(sslMode) || Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out _);

    // Arbeitsauftrag 3: each DataSourceType has its own set of fields it actually needs — a relational
    // source needs Host/Port/Database, an HTTP API source needs InstanceUrl instead — so "required fields
    // present" can no longer be one flat check. Also the single place an out-of-range/unrecognized Type
    // (e.g. a JSON payload with a numeric value outside the enum, or a future member this switch hasn't been
    // taught yet) is rejected as invalid input rather than silently falling through to a provider that isn't
    // registered for it (DataSourceProviderResolver.Resolve already throws for that case too, but this gives
    // the caller a clear 400 instead of the resolver's exception surfacing as a 500-ish failure).
    internal static string? ValidateRequiredFields(DataSourceConfig config) =>
        config.Type switch
        {
            DataSourceType.PostgreSql or DataSourceType.MariaDb => string.IsNullOrWhiteSpace(config.Host)
            || config.Port is null
            || string.IsNullOrWhiteSpace(config.Database)
            || string.IsNullOrWhiteSpace(config.Username)
                ? "Host, Port, Database, and Username are required for this data source type."
                : null,
            DataSourceType.ServiceNowTableApi or DataSourceType.ServiceNowSqlApi => string.IsNullOrWhiteSpace(
                config.InstanceUrl
            ) || string.IsNullOrWhiteSpace(config.Username)
                ? "InstanceUrl and Username are required for this data source type."
                : null,
            _ => $"Unknown data source type '{config.Type}'.",
        };

    // Arbeitsauftrag 8: GET /api/connection never returns the password, so the form re-submits an empty one to
    // mean "keep the stored password". It is only carried over when the request still points at the same
    // system with the same account — otherwise a changed Host/InstanceUrl could send the stored credential to
    // a different server.
    internal static DataSourceConfig WithStoredPasswordIfUnchanged(
        DataSourceConfig request,
        DataSourceConfig? stored
    ) =>
        !request.HasPassword
        && stored is { HasPassword: true }
        && stored.Type == request.Type
        && stored.Host == request.Host
        && stored.Port == request.Port
        && stored.Database == request.Database
        && stored.InstanceUrl == request.InstanceUrl
        && stored.Username == request.Username
            ? request with
            {
                Password = stored.Password,
            }
            : request;

    // ServiceNow is only ever reached over HTTPS: the Basic-auth credentials travel in every request.
    internal static string? ValidateInstanceUrl(string? instanceUrl)
    {
        try
        {
            ServiceNowClient.ParseInstanceUrl(instanceUrl);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }

    internal static async Task<string?> ValidateHostAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var ip) ? [ip] : await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (Exception ex)
        {
            return $"Could not resolve host '{host}': {ErrorSanitizer.Detail(ex)}";
        }

        foreach (var address in addresses)
        {
            var mapped = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
            if (BlockedNetworks.Any(net => net.Contains(mapped)))
                return $"Host '{host}' resolves to a blocked address ({mapped}) "
                    + "and cannot be used as an ERP connection target.";
        }

        return null;
    }

    // Arbeitsauftrag 11: in production an ERP connection must be encrypted unless an operator explicitly opts out;
    // elsewhere (local dev, the docker test database) plaintext stays allowed unless configured otherwise.
    internal static bool AllowUnencryptedConnections(IConfiguration configuration, IHostEnvironment environment) =>
        configuration.GetValue<bool?>(TransportSecurity.AllowUnencryptedSetting) ?? !environment.IsProduction();

    internal static void MapConnectionEndpoints(this WebApplication app)
    {
        var allowUnencrypted = AllowUnencryptedConnections(app.Configuration, app.Environment);

        // Returns the stored connection — never the password itself, only whether one is set
        // (HasPassword). See knowledge/architecture/data-source-configuration.md.
        app.MapGet(
                "/api/connection",
                async (ExportLogDbContext db) =>
                {
                    var cfg = await db.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection);
                    if (cfg is null)
                        return Results.NotFound();

                    return Results.Ok(
                        new ErpConnectionInfo(
                            cfg.Type,
                            cfg.Host,
                            cfg.Port,
                            cfg.Database,
                            cfg.InstanceUrl,
                            cfg.Username,
                            cfg.SslMode,
                            cfg.HasPassword
                        )
                    );
                }
            )
            .RequireAuthorization();

        // Tests the connection, persists it on success, and returns the live source schema.
        app.MapPost(
                "/api/connection",
                async (
                    DataSourceConfig request,
                    ExportLogDbContext db,
                    IDataSourceProviderResolver resolver,
                    CancellationToken ct
                ) =>
                {
                    var requiredFieldError = ValidateRequiredFields(request);
                    if (requiredFieldError is not null)
                        return Results.BadRequest(requiredFieldError);

                    // SslMode only applies to the relational (Host/Port) sources; the SSRF host check applies to
                    // every source — for ServiceNow to the InstanceUrl's host, which must also be HTTPS.
                    string host;
                    if (request.Type is DataSourceType.PostgreSql or DataSourceType.MariaDb)
                    {
                        if (!IsValidSslMode(request.SslMode))
                            return Results.BadRequest(
                                "SslMode must be one of: Disable, Allow, Prefer, Require, VerifyCA, VerifyFull."
                            );
                        host = request.Host!;
                    }
                    else
                    {
                        var instanceError = ValidateInstanceUrl(request.InstanceUrl);
                        if (instanceError is not null)
                            return Results.BadRequest(instanceError);
                        host = new Uri(request.InstanceUrl!.Trim()).Host;
                    }

                    if (!allowUnencrypted && !TransportSecurity.IsAlwaysEncrypted(request))
                        return Results.BadRequest(TransportSecurity.UnencryptedRefusal(request));

                    var hostError = await ValidateHostAsync(host, ct);
                    if (hostError is not null)
                        return Results.BadRequest(hostError);

                    request = WithStoredPasswordIfUnchanged(
                        request,
                        await db.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection)
                    );

                    try
                    {
                        var provider = resolver.Resolve(request.Type);
                        var result = await provider.TestConnectionAsync(request, ct);
                        if (!result.Success)
                            return Results.BadRequest($"Connection failed: {result.Error}");

                        await db.SetSettingAsync(SettingsKeys.ErpConnection, request);
                        return Results.Ok(result.Schema);
                    }
                    catch (UnsupportedDataSourceException)
                    {
                        return Results.BadRequest(
                            $"Connection failed: data source type '{request.Type}' is not supported by this connector version yet."
                        );
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest($"Connection failed: {ErrorSanitizer.Detail(ex)}");
                    }
                }
            )
            .RequireAuthorization();

        // Returns schema from the persisted Postgres connection when one is configured, falling back to
        // the hardcoded demo schema only when no connection has been stored yet. A stored connection that
        // fails to introspect is reported as an error rather than silently substituting the demo schema —
        // swallowing that failure previously let a mapping get built against demo-only tables (e.g.
        // "masterdata") that don't exist in the real database, surfacing much later as a confusing
        // "relation ... does not exist" error at preview/export time instead of here.
        app.MapGet(
                "/api/source-schema",
                async (ExportLogDbContext db, IDataSourceProviderResolver resolver, CancellationToken ct) =>
                {
                    var cfg = await db.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection);
                    if (cfg is null)
                        return Results.Ok(DemoSourceSchema());

                    try
                    {
                        var provider = resolver.Resolve(cfg.Type);
                        return Results.Ok(await provider.ReadSchemaAsync(cfg, ct));
                    }
                    catch (Exception ex)
                    {
                        return Results.Problem(
                            detail: $"Could not read the schema from {cfg.InstanceUrl ?? $"{cfg.Host}:{cfg.Port}/{cfg.Database}"}: {ErrorSanitizer.Detail(ex)}",
                            statusCode: StatusCodes.Status502BadGateway
                        );
                    }
                }
            )
            .RequireAuthorization();
    }

    // Hardcoded demo schema that mirrors what a real production PostgreSQL ERP database would expose.
    internal static SourceSchema DemoSourceSchema() =>
        new(
            "demo-erp (SQLite in dev · PostgreSQL in prod)",
            new SourceTable[]
            {
                new(
                    "systemconfiguration",
                    "Installed CI instances — one row per physical unit",
                    new SourceColumn[]
                    {
                        new("id", "uuid", Nullable: false, PrimaryKey: true),
                        new("serial", "character varying(100)", Nullable: true, PrimaryKey: false),
                        new(
                            "article_id",
                            "uuid",
                            Nullable: true,
                            PrimaryKey: false,
                            ForeignKeyTable: "masterdata",
                            ForeignKeyColumn: "id"
                        ),
                        new("status", "character varying(50)", Nullable: true, PrimaryKey: false),
                        new("commission_date", "date", Nullable: true, PrimaryKey: false),
                        new("technician_name", "character varying(100)", Nullable: true, PrimaryKey: false),
                        new("storage_location", "character varying(200)", Nullable: true, PrimaryKey: false),
                    }
                ),
                new(
                    "masterdata",
                    "Article/model master records — one row per model type",
                    new SourceColumn[]
                    {
                        new("id", "uuid", Nullable: false, PrimaryKey: true),
                        new("article_name", "character varying(200)", Nullable: true, PrimaryKey: false),
                        new("part_number", "character varying(100)", Nullable: true, PrimaryKey: false),
                        new("manufacturer", "character varying(100)", Nullable: true, PrimaryKey: false),
                    }
                ),
                new(
                    "maintenance_plan",
                    "Maintenance plan assignments — drives scope filter",
                    new SourceColumn[]
                    {
                        new("id", "uuid", Nullable: false, PrimaryKey: true),
                        new(
                            "system_configuration_id",
                            "uuid",
                            Nullable: false,
                            PrimaryKey: false,
                            ForeignKeyTable: "systemconfiguration",
                            ForeignKeyColumn: "id"
                        ),
                        new("status", "character varying(50)", Nullable: false, PrimaryKey: false),
                        new("allocation_chart_ref", "character varying(100)", Nullable: true, PrimaryKey: false),
                    }
                ),
                new(
                    "articlestructure",
                    "BOM parent–child relationships",
                    new SourceColumn[]
                    {
                        new("id", "uuid", Nullable: false, PrimaryKey: true),
                        new(
                            "parent_id",
                            "uuid",
                            Nullable: true,
                            PrimaryKey: false,
                            ForeignKeyTable: "masterdata",
                            ForeignKeyColumn: "id"
                        ),
                        new(
                            "child_id",
                            "uuid",
                            Nullable: true,
                            PrimaryKey: false,
                            ForeignKeyTable: "masterdata",
                            ForeignKeyColumn: "id"
                        ),
                    }
                ),
            }
        );
}
