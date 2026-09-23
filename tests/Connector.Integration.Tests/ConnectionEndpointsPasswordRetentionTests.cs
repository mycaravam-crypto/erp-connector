using Connector.Api.Endpoints;
using Connector.Core.DataSources;

namespace Connector.Integration.Tests;

/// <summary>
/// Pure unit tests (no DB) for <c>ConnectionEndpoints.WithStoredPasswordIfUnchanged</c> (Arbeitsauftrag 8): the
/// connection form re-submits an empty password to mean "keep the stored one", which is only honored while the
/// request still targets the same system with the same account.
/// </summary>
public sealed class ConnectionEndpointsPasswordRetentionTests
{
    private static readonly DataSourceConfig Stored = new()
    {
        Type = DataSourceType.MariaDb,
        Host = "erp-db.internal",
        Port = 3306,
        Database = "erp",
        Username = "reader",
        Password = "stored-secret",
        SslMode = "Require",
    };

    [Fact]
    public void EmptyPassword_SameTarget_KeepsStoredPassword()
    {
        // SslMode is not part of the target: tightening TLS must not require re-entering the password.
        var result = ConnectionEndpoints.WithStoredPasswordIfUnchanged(
            Stored with
            {
                Password = "",
                SslMode = "VerifyFull",
            },
            Stored
        );

        Assert.Equal("stored-secret", result.Password);
        Assert.Equal("VerifyFull", result.SslMode);
    }

    [Fact]
    public void NewPassword_Wins() =>
        Assert.Equal(
            "new-secret",
            ConnectionEndpoints.WithStoredPasswordIfUnchanged(Stored with { Password = "new-secret" }, Stored).Password
        );

    public static TheoryData<DataSourceConfig> ChangedTargets =>
        new()
        {
            Stored with
            {
                Password = "",
                Host = "evil.example",
            },
            Stored with
            {
                Password = "",
                Port = 3307,
            },
            Stored with
            {
                Password = "",
                Database = "other",
            },
            Stored with
            {
                Password = "",
                Username = "admin",
            },
            Stored with
            {
                Password = "",
                Type = DataSourceType.PostgreSql,
            },
            Stored with
            {
                Password = "",
                InstanceUrl = "https://evil.service-now.com",
            },
        };

    [Theory]
    [MemberData(nameof(ChangedTargets))]
    public void EmptyPassword_ChangedTarget_DoesNotReuseStoredPassword(DataSourceConfig request) =>
        Assert.Equal("", ConnectionEndpoints.WithStoredPasswordIfUnchanged(request, Stored).Password);

    [Fact]
    public void EmptyPassword_NothingStored_StaysEmpty() =>
        Assert.Equal(
            "",
            ConnectionEndpoints.WithStoredPasswordIfUnchanged(Stored with { Password = "" }, null).Password
        );
}
