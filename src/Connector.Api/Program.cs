using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Connector.Api;
using Connector.Api.Authorization;
using Connector.Api.Endpoints;
using Connector.Core.DataSources;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;
using Connector.Infrastructure.DataSources.PostgreSql;
using Connector.Infrastructure.DataSources.ServiceNow;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(
    (ctx, services, cfg) =>
    {
        cfg.MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .ReadFrom.Configuration(ctx.Configuration)
            .ReadFrom.Services(services);

        // Every sink formats through SanitizingLogFormatter, so no log line carries a credential.
        cfg.WriteTo.Console(
            new SanitizingLogFormatter(
                ctx.HostingEnvironment.IsProduction()
                    ? new Serilog.Formatting.Json.JsonFormatter()
                    : new Serilog.Formatting.Display.MessageTemplateTextFormatter(
                        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
                    )
            )
        );
    }
);

// ── Auth ──────────────────────────────────────────────────────────────────────

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

var jwtSecret =
    builder.Configuration["Auth:JwtSecret"] ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");

// appsettings.json/appsettings.Production.json both ship this literal placeholder — if an operator deploys
// without setting the Auth__JwtSecret environment variable, the app would otherwise start up fine and sign
// every JWT with a secret that's public in this repository, letting anyone forge a valid session token.
// Fail fast instead, in every environment, on either the placeholder or a too-short/weak value.
const string PlaceholderJwtSecret = "REPLACE_WITH_SECURE_SECRET_MINIMUM_32_CHARS";
const int MinJwtSecretLength = 32;
if (jwtSecret == PlaceholderJwtSecret)
{
    throw new InvalidOperationException(
        "Auth:JwtSecret is still set to the placeholder value from appsettings.json. Set the "
            + "Auth__JwtSecret environment variable to a unique, securely generated random secret before "
            + "starting the app."
    );
}
if (jwtSecret.Length < MinJwtSecretLength)
{
    throw new InvalidOperationException(
        $"Auth:JwtSecret must be at least {MinJwtSecretLength} characters (got {jwtSecret.Length})."
    );
}

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
    {
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
        // Every request re-reads the user from the user table: a deleted user's tokens stop working at once, and
        // the role claim the permission checks use (PermissionAuthorizationHandler) is always the current one
        // rather than whatever the role was at login. A signature- and expiry-valid JWT could also be one this
        // user has explicitly revoked (POST /api/auth/revoke-my-sessions) — its issued-at claim is checked
        // against that user's stored revocation cutover. A token without an "iat" claim skips that check.
        opts.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var username = context.Principal?.Identity?.Name;
                var db = context.HttpContext.RequestServices.GetRequiredService<ExportLogDbContext>();
                var user = await db.FindUserAsync(username);
                if (user is null || context.Principal?.Identity is not ClaimsIdentity identity)
                {
                    context.Fail("User no longer exists.");
                    return;
                }

                var iatValue = context.Principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;
                if (iatValue is not null && long.TryParse(iatValue, out var iatUnixSeconds))
                {
                    var revokedBefore = await db.GetRevokedBeforeAsync(user.Username);
                    if (revokedBefore is not null && DateTimeOffset.FromUnixTimeSeconds(iatUnixSeconds) < revokedBefore)
                    {
                        context.Fail("Token has been revoked.");
                        return;
                    }
                }

                foreach (var stale in identity.FindAll(ClaimTypes.Role).ToList())
                    identity.RemoveClaim(stale);
                identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
            },
        };
    })
    // Second, opt-in scheme for machine-to-machine callers (X-Api-Key header) — only endpoints that
    // explicitly list "ApiKey" alongside the default JWT scheme via RequireAuthorization(policy => ...)
    // accept it; every other endpoint is unaffected and still requires a JWT.
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

builder.Services.AddPermissionAuthorization();

// Per-client-IP fixed-window throttle on /api/auth/login (AuthEndpoints.LoginRateLimiterPolicyName) —
// without it there was no defense at all against brute-force/password-spray login attempts.
builder.Services.AddRateLimiter(opts =>
{
    opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opts.AddPolicy(
        AuthEndpoints.LoginRateLimiterPolicyName,
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                // 20/minute per client IP: well above the handful of logins a normal user or the e2e
                // suite performs in that window, but a hard ceiling on brute-force/password-spray
                // throughput (on top of BCrypt's own per-attempt cost).
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 20,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );

    // Per-client-IP fixed-window throttle on the four-eyes release endpoints (FourEyesReview.
    // ApprovalRateLimiterPolicyName) — same brute-force concern as login, since ValidateApprover also
    // checks a password (the approver's), and a normal release is a rare, deliberate action.
    opts.AddPolicy(
        FourEyesReview.ApprovalRateLimiterPolicyName,
        httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }
            )
    );
});

// Dev vs Production API key source, resolved now (before Build()) so it can go into the container as a
// singleton for ApiKeyAuthenticationHandler — mirrors the user seed's Dev/Production split below.
var apiKeyEntries = builder.Environment.IsDevelopment()
    ? DevAuthSeed.CreateApiKeys()
    : builder.Configuration.GetSection("Auth:ApiKeys").Get<List<ApiKeyOptions>>() ?? [];
builder.Services.AddSingleton(
    new ApiKeyStore(apiKeyEntries.ToDictionary(k => k.KeyHash.ToLowerInvariant(), k => k.Name, StringComparer.Ordinal))
);

// ── CORS ──────────────────────────────────────────────────────────────────────

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(opts =>
        opts.AddDefaultPolicy(p => p.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod())
    );
}

// ── Infrastructure ────────────────────────────────────────────────────────────

builder.Services.Configure<ExportSinkOptions>(builder.Configuration.GetSection("ExportSink"));
builder.Services.Configure<ExportWorkerOptions>(builder.Configuration.GetSection("ExportWorker"));
builder.Services.AddSingleton<FileSystemExportSink>();

builder.Services.Configure<ImportSinkOptions>(builder.Configuration.GetSection("ImportSink"));
builder.Services.Configure<ImportWorkerOptions>(builder.Configuration.GetSection("ImportWorker"));

// Backs EncryptedStringConverter (ExportLogDbContext.OnModelCreating), which encrypts the AppSetting.Value
// column at rest — it holds the ERP connection config, password included. Keys are persisted to disk on
// their own volume, separate from the SQLite database (see docker-compose.yml's connector-dpkeys volume
// and appsettings.Production.json's DataProtection comment — a single volume backup/snapshot must never
// yield both the ciphertext and the key to decrypt it), so they survive container
// restarts/redeploys; losing this directory makes every previously-stored setting unrecoverable, same
// operational tradeoff as losing Auth:JwtSecret.
var dataProtectionKeysDirectory = builder.Configuration["DataProtection:KeysDirectory"] ?? "dp-keys";
builder
    .Services.AddDataProtection()
    .SetApplicationName("Connector.Api")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDirectory));

builder.Services.AddDbContext<ExportLogDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("ExportLog"))
);

// Data source abstraction: one provider per DataSourceType — DataSourceProviderResolver.Resolve throws
// UnsupportedDataSourceException for any type without one (ServiceNowSqlApi today). All are singletons: none
// holds per-call state, and every provider method opens/disposes its own connection (or HTTP request).
builder.Services.AddSingleton<IDataSourceProvider, PostgreSqlDataSourceProvider>();
builder.Services.AddSingleton<IDataSourceProvider, MariaDbDataSourceProvider>();
builder.Services.AddSingleton<IDataSourceProvider, ServiceNowTableApiProvider>();
builder.Services.AddSingleton<IDataSourceProviderResolver, DataSourceProviderResolver>();

builder.Services.AddScoped<AuditService>();
builder.Services.AddHostedService<ExportWorker>();
builder.Services.AddHostedService<ExportDefinitionWorker>();
builder.Services.AddHostedService<ImportWorker>();

// ─────────────────────────────────────────────────────────────────────────────

var app = builder.Build();

if (allowedOrigins.Length > 0)
    app.UseCors();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.Use(
    async (ctx, next) =>
    {
        var headers = ctx.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] =
            "default-src 'none'; "
            + "script-src 'self'; "
            + "style-src 'self' 'unsafe-inline'; "
            + "img-src 'self' data:; "
            + "font-src 'self'; "
            + "connect-src 'self'; "
            + "frame-ancestors 'none'";
        await next();
    }
);

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// ── Database initialisation ───────────────────────────────────────────────────

using (var scope = app.Services.CreateScope())
{
    // Export log: EF Core migrations manage schema from this point forward.
    // BootstrapMigrationsAsync handles databases that were created before migrations were introduced.
    var exportLogDb = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
    await BootstrapMigrationsAsync(exportLogDb);
    await exportLogDb.Database.MigrateAsync();

    // AuditLog may be missing on databases where InitialSchema was stamped via bootstrap
    // without the table. IF NOT EXISTS makes this a safe no-op on intact DBs.
    await exportLogDb.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE IF NOT EXISTS "AuditLog" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLog" PRIMARY KEY AUTOINCREMENT,
            "Timestamp" TEXT NOT NULL,
            "Username" TEXT NOT NULL,
            "Action" TEXT NOT NULL,
            "Detail" TEXT NULL
        )
        """
    );
    await exportLogDb.Database.ExecuteSqlRawAsync(
        """CREATE INDEX IF NOT EXISTS "IX_AuditLog_Timestamp" ON "AuditLog" ("Timestamp")"""
    );

    // Encrypts AppSetting rows still stored as plaintext from before EncryptedStringConverter existed —
    // otherwise they stay plaintext on disk and log the converter's plaintext warning on every read. No-op once
    // every row is encrypted.
    await AppSettingEncryptionMigrator.EncryptPlaintextRowsAsync(
        exportLogDb,
        scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(),
        app.Logger
    );

    // An unencrypted stored connection (saved before the production TLS rule, or with the opt-out) keeps working,
    // but is called out on every start so it doesn't go unnoticed.
    var storedConnection = await exportLogDb.GetSettingAsync<DataSourceConfig>(SettingsKeys.ErpConnection);
    var storedProvider = storedConnection is null
        ? null
        : scope.ServiceProvider.GetServices<IDataSourceProvider>().FirstOrDefault(p => p.Type == storedConnection.Type);
    if (storedConnection is not null && storedProvider?.IsAlwaysEncrypted(storedConnection) == false)
        app.Logger.LogWarning(
            "The stored ERP connection ({Type}) can use an unencrypted connection (TLS mode '{SslMode}'). "
                + "Set it to Require, VerifyCA or VerifyFull.",
            storedConnection.Type,
            storedConnection.SslMode ?? "Prefer (default)"
        );

    // One-time conversion of the legacy single mapping + presets into ExportDefinition rows.
    // No-ops once any ExportDefinition row exists.
    await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(exportLogDb);

    // Creates this installation's instance id (stamped into every export manifest) before any worker runs.
    await exportLogDb.GetProducerAsync();
}

// ── Users ─────────────────────────────────────────────────────────────────────
// The user table is managed in the UI (Settings → Users) and seeded once, while it is still empty:
// Development: alice/alice123 and bob/bob123 (Admins), carol/carol123 (User).
// Production: the BCrypt hashes and roles from Auth:Users in appsettings.json / env vars.

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
    var seed = app.Environment.IsDevelopment()
        ? DevAuthSeed.CreateUsers()
        : AuthUser.ToSeed(app.Configuration.GetSection("Auth:Users").Get<List<AuthUser>>() ?? [], app.Logger);
    var seeded = await db.SeedUsersIfEmptyAsync(seed);
    if (seeded > 0)
        app.Logger.LogInformation("Created {Count} user(s) from the configured user list.", seeded);
    else if (seed.Count > 0)
        app.Logger.LogInformation(
            "Users are managed in Settings → Users; the configured user list only seeds an empty user table."
        );

    if (app.Environment.IsDevelopment())
    {
        app.Logger.LogInformation("Dev auth: admins alice/alice123 and bob/bob123, user carol/carol123.");
        app.Logger.LogInformation(
            "Dev auth: API key '{Key}' is active (send as the X-Api-Key header).",
            DevAuthSeed.DevApiKey
        );
    }
    else if (!await db.Users.AnyAsync(u => u.Role == UserRoles.Admin))
    {
        // Nobody could manage users or permissions, configure the connection, or ever change that from the UI.
        throw new InvalidOperationException(
            "No Admin user exists. Add one to Auth:Users with \"Role\": \"Admin\" (it is only used while the user "
                + "table is empty), or set an existing user's Role to Admin in the User table."
        );
    }
}

// ── Endpoints ─────────────────────────────────────────────────────────────────

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapUserEndpoints();
app.MapPermissionEndpoints();
app.MapExportEndpoints();
app.MapPipelineEndpoints();
app.MapSchemaEndpoints();
app.MapConnectionEndpoints();
app.MapSettingsEndpoints();
app.MapBrandingEndpoints();
app.MapExportMappingEndpoints();
app.MapExportDefinitionEndpoints();
app.MapImportDefinitionEndpoints();
app.MapImportRunEndpoints();

// SPA fallback: any path not matched by an API route serves index.html
// so Vue Router can handle client-side navigation.
app.MapFallbackToFile("index.html");

await app.RunAsync();

// ── Helpers ───────────────────────────────────────────────────────────────────

// Handles databases created via EnsureCreatedAsync before EF Core migrations were introduced.
// If the core tables exist but __EFMigrationsHistory does not, we create the history table,
// mark InitialSchema as already applied, and manually add any indexes that weren't in the
// original EnsureCreatedAsync schema. MigrateAsync() is then a safe no-op for those databases.
async Task BootstrapMigrationsAsync(ExportLogDbContext db)
{
    var historyExists =
        (
            await db
                .Database.SqlQueryRaw<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'"
                )
                .ToListAsync()
        )[0] > 0;

    if (historyExists)
        return;

    var tablesExist =
        (
            await db
                .Database.SqlQueryRaw<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ExportRun'")
                .ToListAsync()
        )[0] > 0;

    if (!tablesExist)
        return; // fresh install — MigrateAsync creates everything

    var auditLogExists =
        (
            await db
                .Database.SqlQueryRaw<int>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AuditLog'")
                .ToListAsync()
        )[0] > 0;

    // Pre-migration database: create history table and stamp the initial migration as applied.
    // Runs regardless of whether AuditLog exists — if it's absent, the startup CREATE TABLE IF
    // NOT EXISTS guard below will add it after MigrateAsync is done.
    await db.Database.ExecuteSqlRawAsync(
        """
        CREATE TABLE "__EFMigrationsHistory" (
            "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
            "ProductVersion" TEXT NOT NULL
        )
        """
    );
    await db.Database.ExecuteSqlRawAsync(
        "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20260701083054_InitialSchema', '9.0.6')"
    );
    // Add the AuditLog Timestamp index — only when the table already exists; if it's absent
    // the startup CREATE TABLE IF NOT EXISTS guard will create both table and index.
    if (auditLogExists)
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_AuditLog_Timestamp\" ON \"AuditLog\" (\"Timestamp\")"
        );
}

// Top-level statements generate an internal Program class by default; this partial declaration makes it
// public so Connector.Integration.Tests's WebApplicationFactory<Program> (ApiFactory) can reference it.
public partial class Program
{
    protected Program() { }
}
