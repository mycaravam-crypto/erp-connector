using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;
using Connector.Infrastructure.DataSources.PostgreSql;
using Connector.Infrastructure.DataSources.ServiceNow;

namespace Connector.Integration.Tests;

/// <summary>
/// Each provider's own config rules (Arbeitsauftrag 14 moved them out of <c>ConnectionEndpoints</c>):
/// <see cref="IDataSourceProvider.ValidateConfig"/> (required fields per source type, the shared TLS-mode vocabulary,
/// ServiceNow's HTTPS instance URL), <see cref="IDataSourceProvider.TargetHost"/> and
/// <see cref="IDataSourceProvider.IsAlwaysEncrypted"/>. Pure unit tests, no connection is opened.
/// </summary>
public sealed class DataSourceConfigValidationTests
{
    public static TheoryData<string> RelationalProviders => new() { "postgres", "mariadb" };

    private static IDataSourceProvider Relational(string name) =>
        name == "postgres" ? new PostgreSqlDataSourceProvider() : new MariaDbDataSourceProvider();

    private static readonly IDataSourceProvider ServiceNow = new ServiceNowTableApiProvider();

    private static DataSourceConfig ValidRelational() =>
        new()
        {
            Host = "erp-db.internal",
            Port = 5432,
            Database = "erp",
            Username = "reader",
            Password = "p",
        };

    private static DataSourceConfig ValidServiceNow() =>
        new()
        {
            Type = DataSourceType.ServiceNowTableApi,
            InstanceUrl = "https://acme.service-now.com",
            Username = "svc",
            Password = "p",
        };

    [Theory]
    [MemberData(nameof(RelationalProviders))]
    public void Relational_AllRequiredFieldsPresent_IsValid(string provider)
    {
        Assert.Null(Relational(provider).ValidateConfig(ValidRelational()));
        Assert.Equal("erp-db.internal", Relational(provider).TargetHost(ValidRelational()));
    }

    public static TheoryData<string, DataSourceConfig> MissingRelationalFields
    {
        get
        {
            var data = new TheoryData<string, DataSourceConfig>();
            foreach (var provider in new[] { "postgres", "mariadb" })
            {
                data.Add(provider, ValidRelational() with { Host = null });
                data.Add(provider, ValidRelational() with { Port = null });
                data.Add(provider, ValidRelational() with { Database = "  " });
                data.Add(provider, ValidRelational() with { Username = "" });
                // A ServiceNow-shaped payload sent as a relational type is still missing Host/Port/Database.
                data.Add(
                    provider,
                    new DataSourceConfig { InstanceUrl = "https://not-relevant.example", Username = "u" }
                );
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(MissingRelationalFields))]
    public void Relational_MissingRequiredField_IsInvalid(string provider, DataSourceConfig config) =>
        Assert.Contains("required", Relational(provider).ValidateConfig(config));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Disable")]
    [InlineData("Allow")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    [InlineData("verifyfull")]
    public void Relational_KnownOrUnsetSslMode_IsValid(string? sslMode)
    {
        foreach (var provider in new[] { "postgres", "mariadb" })
            Assert.Null(Relational(provider).ValidateConfig(ValidRelational() with { SslMode = sslMode }));
    }

    [Theory]
    [InlineData("Trust")]
    [InlineData("verify-full")]
    [InlineData("VerifyFull; DROP TABLE users")]
    public void Relational_UnknownSslMode_IsInvalid(string sslMode)
    {
        foreach (var provider in new[] { "postgres", "mariadb" })
            Assert.Contains(
                "SslMode",
                Relational(provider).ValidateConfig(ValidRelational() with { SslMode = sslMode })
            );
    }

    [Theory]
    [InlineData("Require", true)]
    [InlineData("VerifyCA", true)]
    [InlineData("verifyfull", true)]
    [InlineData(null, false)]
    [InlineData("Prefer", false)]
    [InlineData("Allow", false)]
    [InlineData("Disable", false)]
    public void Relational_IsAlwaysEncrypted_OnlyForMandatoryTls(string? sslMode, bool expected)
    {
        foreach (var provider in new[] { "postgres", "mariadb" })
            Assert.Equal(
                expected,
                Relational(provider).IsAlwaysEncrypted(ValidRelational() with { SslMode = sslMode })
            );
    }

    [Fact]
    public void ServiceNow_ValidConfig_IsValidAndAlwaysEncrypted()
    {
        Assert.Null(ServiceNow.ValidateConfig(ValidServiceNow()));
        Assert.Equal("acme.service-now.com", ServiceNow.TargetHost(ValidServiceNow()));
        Assert.True(ServiceNow.IsAlwaysEncrypted(ValidServiceNow()));
    }

    [Fact]
    public void ServiceNow_MissingInstanceUrlOrUsername_IsInvalid()
    {
        Assert.Contains("required", ServiceNow.ValidateConfig(ValidServiceNow() with { InstanceUrl = null }));
        Assert.Contains("required", ServiceNow.ValidateConfig(ValidServiceNow() with { Username = "" }));
        // A relational-shaped payload sent as ServiceNow is still missing its InstanceUrl.
        Assert.Contains(
            "required",
            ServiceNow.ValidateConfig(ValidRelational() with { Type = DataSourceType.ServiceNowTableApi })
        );
    }

    [Theory]
    [InlineData("http://acme.service-now.com")]
    [InlineData("acme.service-now.com")]
    [InlineData("ftp://acme.service-now.com")]
    public void ServiceNow_NonHttpsInstanceUrl_IsInvalid(string instanceUrl) =>
        Assert.Contains("https", ServiceNow.ValidateConfig(ValidServiceNow() with { InstanceUrl = instanceUrl }));

    // Only a provider with the Imports capability hands out an import connection; the check runs before any
    // connection is opened.
    [Theory]
    [InlineData(DataSourceType.MariaDb)]
    [InlineData(DataSourceType.ServiceNowTableApi)]
    public async Task ImportConnection_ProviderWithoutImportsCapability_IsRefused(DataSourceType type)
    {
        var resolver = new DataSourceProviderResolver([new MariaDbDataSourceProvider(), ServiceNow]);

        var ex = await Assert.ThrowsAsync<UnsupportedDataSourceException>(() =>
            ImportConnection.OpenAsync(resolver, ValidRelational() with { Type = type }, CancellationToken.None)
        );
        Assert.Contains("Imports are not supported", ex.Message);
    }
}
