using Connector.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Connector.Integration.Tests;

/// <summary>
/// Boots the real <see cref="Program"/> pipeline — auth, rate limiting, security headers, EF migrations,
/// every <c>MapXEndpoints</c> call — over an isolated temp SQLite file and temp staging/inbound
/// directories, so endpoint-layer tests exercise actual HTTP routing, model binding, and auth instead of
/// calling handler methods directly (unlike <see cref="LocalDb"/>-based tests elsewhere in this project).
/// <see cref="Program"/> is reachable here via the same <c>InternalsVisibleTo</c> grant
/// (<c>Connector.Api/AssemblyInfo.cs</c>) that already lets this project call other internal API types.
///
/// One instance is shared across every HTTP-layer test class via <see cref="ApiCollection"/> — see its
/// doc comment for why a per-class fixture doesn't work here (Serilog's shared static logger).
///
/// Runs with <c>ASPNETCORE_ENVIRONMENT=Development</c> so <see cref="DevAuthSeed"/>'s users
/// (alice/alice123, bob/bob123) and API key (<see cref="DevAuthSeed.DevApiKey"/>) are seeded and
/// HTTPS/HSTS enforcement is skipped — everything else Program.cs needs (JWT secret, DB/staging/inbound
/// paths) is supplied here via in-memory configuration appended after appsettings*.json, so this factory
/// never depends on those files actually being found under the test host's content root.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("connector-api-test-");

    public string StagingPath => Path.Combine(_root.FullName, "staging");
    public string InboundPath => Path.Combine(_root.FullName, "inbound");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // Deliberately doesn't override Auth:JwtSecret: Program.cs reads that value into a local variable
        // to build the JWT bearer signing key *before* builder.Build() runs, while ConfigureAppConfiguration
        // sources aren't guaranteed visible to builder.Configuration until Build() completes — overriding it
        // here raced the two reads and mismatched signing keys ("signature key was not found" on every
        // validated request). appsettings.Development.json's own secret is already valid (32+ chars) and
        // consistently the same source for both reads, so there's nothing to override.
        builder.ConfigureAppConfiguration(
            (_, config) =>
                config.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:ExportLog"] =
                            $"Data Source={Path.Combine(_root.FullName, "export_log.db")}",
                        ["ExportSink:StagingPath"] = StagingPath,
                        ["ImportSink:InboundPath"] = InboundPath,
                        ["DataProtection:KeysDirectory"] = Path.Combine(_root.FullName, "dpkeys"),
                    }
                )
        );
    }

    Task IAsyncLifetime.InitializeAsync()
    {
        Directory.CreateDirectory(StagingPath);
        Directory.CreateDirectory(InboundPath);
        // Forces host + TestServer creation (and Program.cs's startup migrations) now, so a startup
        // failure surfaces here rather than confusingly inside the first test's first request.
        _ = Server;
        return Task.CompletedTask;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            _root.Delete(recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup; a leftover dir under the OS temp path isn't worth failing a test run over.
        }
    }
}
