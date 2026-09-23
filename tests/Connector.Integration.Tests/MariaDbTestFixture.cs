using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;

namespace Connector.Integration.Tests;

/// <summary>
/// The MariaDB counterpart of <see cref="ErpTestFixture"/>: connection info for the local MariaDB test instance
/// (<c>docker-compose --profile test up -d testdb-mariadb</c>, seeded from testdb/mariadb-init.sql) and the same
/// "no-op instead of fail" availability check every MariaDB-backed test uses when it isn't running.
/// </summary>
internal static class MariaDbTestFixture
{
    internal static readonly DataSourceConfig Config = new()
    {
        Type = DataSourceType.MariaDb,
        Host = "127.0.0.1",
        Port = 3306,
        Database = "erp_testdb",
        Username = "erp_test",
        Password = "erp_test_pw",
    };

    internal static async Task<bool> IsAvailableAsync()
    {
        try
        {
            await using var conn = await MariaDbConnectionFactory.OpenAsync(Config, CancellationToken.None);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
