using Connector.Api.Endpoints;
using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Connector.Integration.Tests;

/// <summary>Transport security: when an unencrypted config is refused (production, unless
/// explicitly allowed). Which configs are always encrypted is each provider's call — see
/// <see cref="DataSourceConfigValidationTests"/>. Pure unit tests.</summary>
public sealed class TransportSecurityTests
{
    private static DataSourceConfig Relational(DataSourceType type, string? sslMode) =>
        new()
        {
            Type = type,
            Host = "h",
            Port = 1,
            Database = "d",
            Username = "u",
            SslMode = sslMode,
        };

    [Fact]
    public void Refusal_NamesTheModeAndTheOptOut()
    {
        var message = TransportSecurity.UnencryptedRefusal(Relational(DataSourceType.MariaDb, null));

        Assert.Contains("Prefer (default)", message);
        Assert.Contains(TransportSecurity.AllowUnencryptedSetting, message);
    }

    [Theory]
    [InlineData("Production", null, false)]
    [InlineData("Production", "true", true)]
    [InlineData("Development", null, true)]
    [InlineData("Development", "false", false)]
    public void AllowUnencrypted_DefaultsToFalseOnlyInProduction(string environment, string? setting, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { [TransportSecurity.AllowUnencryptedSetting] = setting }
            )
            .Build();

        Assert.Equal(
            expected,
            ConnectionEndpoints.AllowUnencryptedConnections(configuration, new FakeEnvironment(environment))
        );
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
