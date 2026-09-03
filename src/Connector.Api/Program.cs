using System.Text;
using Connector.Api;
using Connector.Api.Endpoints;
using Connector.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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

        if (ctx.HostingEnvironment.IsProduction())
            cfg.WriteTo.Console(new Serilog.Formatting.Json.JsonFormatter());
        else
            cfg.WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
    }
);

// ── Auth ──────────────────────────────────────────────────────────────────────

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

var jwtSecret =
    builder.Configuration["Auth:JwtSecret"] ?? throw new InvalidOperationException("Auth:JwtSecret is not configured.");

builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opts =>
        opts.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        }
    )
    // Second, opt-in scheme for machine-to-machine callers (X-Api-Key header) — only endpoints that
    // explicitly list "ApiKey" alongside the default JWT scheme via RequireAuthorization(policy => ...)
    // accept it; every other endpoint is unaffected and still requires a JWT.
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, null);

builder.Services.AddAuthorization();

// Dev vs Production API key source, resolved now (before Build()) so it can go into the container as a
// singleton for ApiKeyAuthenticationHandler — mirrors the Users list's Dev/Production split below, which
// is resolved after Build() instead only because it's passed as a plain constructor/closure argument to
// endpoint-mapping methods rather than needing DI.
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

builder.Services.AddDbContext<ExportLogDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("ExportLog"))
);

builder.Services.AddScoped<AuditService>();
builder.Services.AddHostedService<ExportWorker>();

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

// ── Database initialisation ───────────────────────────────────────────────────

using (var scope = app.Services.CreateScope())
{
    // Export log: EF Core migrations manage schema from this point forward.
    // BootstrapMigrationsAsync handles databases that were created before migrations were introduced.
    var exportLogDb = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
    await BootstrapMigrationsAsync(exportLogDb);
    await exportLogDb.Database.MigrateAsync();

    // AuditLog may be missing on databases where InitialSchema was stamped via bootstrap
    // before Phase 8 added the table. IF NOT EXISTS makes this a safe no-op on intact DBs.
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

    // Phase 14: one-time conversion of the legacy single mapping + presets into ExportDefinition rows.
    // No-ops once any ExportDefinition row exists.
    await ExportDefinitionMigrator.MigrateLegacyMappingsAsync(exportLogDb);
}

// ── User store ────────────────────────────────────────────────────────────────
// Development: hard-coded seed (alice/alice123, bob/bob123).
// Production: BCrypt hashes from Auth:Users in appsettings.json / env vars.

IReadOnlyDictionary<string, string> userStore;
if (app.Environment.IsDevelopment())
{
    userStore = DevAuthSeed.CreateUsers();
    app.Logger.LogInformation("Dev auth: users alice/alice123 and bob/bob123 are active.");
    app.Logger.LogInformation(
        "Dev auth: API key '{Key}' is active (send as the X-Api-Key header).",
        DevAuthSeed.DevApiKey
    );
}
else
{
    var authUsers = app.Configuration.GetSection("Auth:Users").Get<List<AuthUser>>() ?? [];
    userStore = authUsers.ToDictionary(u => u.Username, u => u.PasswordHash, StringComparer.OrdinalIgnoreCase);
}

// ── Endpoints ─────────────────────────────────────────────────────────────────

app.MapHealthEndpoints();
app.MapAuthEndpoints(userStore);
app.MapExportEndpoints(userStore);
app.MapPipelineEndpoints();
app.MapSchemaEndpoints();
app.MapConnectionEndpoints();
app.MapSettingsEndpoints();
app.MapExportMappingEndpoints();
app.MapExportDefinitionEndpoints();

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
