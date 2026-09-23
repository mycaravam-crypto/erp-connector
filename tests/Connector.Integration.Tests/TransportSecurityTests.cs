using Connector.Api.Endpoints;
using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Connector.Integration.Tests;

/// <summary>Arbeitsauftrag 11 transport security: which configs are guaranteed encrypted, and when an unencrypted
/// one is refused (production, unless explicitly allowed). Pure unit tests.</summary>
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

    [Theory]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    [InlineData("verifyfull")]
    public void MandatoryTls_IsAlwaysEncrypted(string sslMode)
    {
        Assert.True(TransportSecurity.IsAlwaysEncrypted(Relational(DataSourceType.PostgreSql, sslMode)));
        Assert.True(TransportSecurity.IsAlwaysEncrypted(Relational(DataSourceType.MariaDb, sslMode)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Prefer")]
    [InlineData("Allow")]
    [InlineData("Disable")]
    public void OptionalOrNoTls_IsNotAlwaysEncrypted(string? sslMode)
    {
        Assert.False(TransportSecurity.IsAlwaysEncrypted(Relational(DataSourceType.PostgreSql, sslMode)));
        Assert.False(TransportSecurity.IsAlwaysEncrypted(Relational(DataSourceType.MariaDb, sslMode)));
    }

    [Fact]
    public void ServiceNow_IsAlwaysEncrypted_BecauseItsClientOnlySpeaksHttps() =>
        Assert.True(
            TransportSecurity.IsAlwaysEncrypted(
                new DataSourceConfig
                {
                    Type = DataSourceType.ServiceNowTableApi,
                    InstanceUrl = "https://x.service-now.com",
                    Username = "u",
                }
            )
        );

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
