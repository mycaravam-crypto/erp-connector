using Connector.Api.Endpoints;
using Connector.Core.DataSources;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="ConnectionEndpoints.ValidateRequiredFields"/> — Arbeitsauftrag 3's per-
/// <see cref="DataSourceType"/> required-field check, covering the "invalid combinations" and "unknown data
/// source type" cases from the work order's test list. Pure unit tests, no database/HTTP involved.
/// </summary>
public sealed class ConnectionEndpointsRequiredFieldValidationTests
{
    private static DataSourceConfig ValidPostgreSql() =>
        new()
        {
            Type = DataSourceType.PostgreSql,
            Host = "h",
            Port = 5432,
            Database = "d",
            Username = "u",
            Password = "p",
        };

    private static DataSourceConfig ValidServiceNow(DataSourceType type) =>
        new()
        {
            Type = type,
            InstanceUrl = "https://acme.service-now.com",
            Username = "u",
            Password = "p",
        };

    [Theory]
    [InlineData(DataSourceType.PostgreSql)]
    [InlineData(DataSourceType.MariaDb)]
    public void RelationalType_AllRequiredFieldsPresent_IsValid(DataSourceType type)
    {
        var config = ValidPostgreSql() with { Type = type };

        Assert.Null(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    [Theory]
    [InlineData(DataSourceType.PostgreSql)]
    [InlineData(DataSourceType.MariaDb)]
    public void RelationalType_MissingHost_IsInvalid(DataSourceType type)
    {
        var config = ValidPostgreSql() with { Type = type, Host = null };

        Assert.NotNull(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    [Theory]
    [InlineData(DataSourceType.PostgreSql)]
    [InlineData(DataSourceType.MariaDb)]
    public void RelationalType_MissingPort_IsInvalid(DataSourceType type)
    {
        var config = ValidPostgreSql() with { Type = type, Port = null };

        Assert.NotNull(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    [Theory]
    [InlineData(DataSourceType.PostgreSql)]
    [InlineData(DataSourceType.MariaDb)]
    public void RelationalType_MissingDatabase_IsInvalid(DataSourceType type)
    {
        var config = ValidPostgreSql() with { Type = type, Database = "  " };

        Assert.NotNull(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    [Theory]
    [InlineData(DataSourceType.PostgreSql)]
    [InlineData(DataSourceType.MariaDb)]
    public void RelationalType_MissingUsername_IsInvalid(DataSourceType type)
    {
        var config = ValidPostgreSql() with { Type = type, Username = "" };

        Assert.NotNull(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    // A relational config carrying InstanceUrl instead of Host/Port/Database — a plausible "invalid
    // combination" a caller could send by mistake (e.g. copy-pasting a ServiceNow payload's shape) — must
    // still be rejected on its missing Host/Port/Database, regardless of what else is set.
    [Fact]
    public void RelationalType_InstanceUrlInsteadOfHost_IsStillInvalid()
    {
        var config = new DataSourceConfig
        {
            Type = DataSourceType.PostgreSql,
            InstanceUrl = "https://not-relevant.example",
            Username = "u",
            Password = "p",
        };

        Assert.NotNull(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    [Theory]
    [InlineData(DataSourceType.ServiceNowTableApi)]
    [InlineData(DataSourceType.ServiceNowSqlApi)]
    public void HttpApiType_AllRequiredFieldsPresent_IsValid(DataSourceType type)
    {
        Assert.Null(ConnectionEndpoints.ValidateRequiredFields(ValidServiceNow(type)));
    }

    [Theory]
    [InlineData(DataSourceType.ServiceNowTableApi)]
    [InlineData(DataSourceType.ServiceNowSqlApi)]
    public void HttpApiType_MissingInstanceUrl_IsInvalid(DataSourceType type)
    {
        var config = ValidServiceNow(type) with { InstanceUrl = null };

        Assert.NotNull(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    [Theory]
    [InlineData(DataSourceType.ServiceNowTableApi)]
    [InlineData(DataSourceType.ServiceNowSqlApi)]
    public void HttpApiType_MissingUsername_IsInvalid(DataSourceType type)
    {
        var config = ValidServiceNow(type) with { Username = "" };

        Assert.NotNull(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    // An HTTP-API config carrying Host/Port instead of InstanceUrl — the mirror image of
    // RelationalType_InstanceUrlInsteadOfHost_IsStillInvalid — must still be rejected on its missing
    // InstanceUrl.
    [Fact]
    public void HttpApiType_HostInsteadOfInstanceUrl_IsStillInvalid()
    {
        var config = new DataSourceConfig
        {
            Type = DataSourceType.ServiceNowTableApi,
            Host = "not-relevant.example",
            Port = 443,
            Username = "u",
            Password = "p",
        };

        Assert.NotNull(ConnectionEndpoints.ValidateRequiredFields(config));
    }

    // ── Unknown data source type ─────────────────────────────────────────────────────────

    [Fact]
    public void UnknownType_IsInvalid()
    {
        var config = ValidServiceNow(DataSourceType.ServiceNowTableApi) with { Type = (DataSourceType)999 };

        var error = ConnectionEndpoints.ValidateRequiredFields(config);

        Assert.NotNull(error);
        Assert.Contains("Unknown data source type", error);
    }
}
