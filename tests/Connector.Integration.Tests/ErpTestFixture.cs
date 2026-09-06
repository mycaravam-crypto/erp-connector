using Connector.Core.DynamicExport;
using Npgsql;

namespace Connector.Integration.Tests;

/// <summary>
/// Shared local <c>testdb</c> connection info for every Postgres-backed test in this project (start it with
/// <c>docker-compose --profile test up -d testdb</c>; see testdb/init.sql for seed data), plus the
/// "no-op instead of fail" open-or-null helper they all use when it isn't running — this repo's xunit
/// version, 2.9.2, predates <c>Assert.Skip</c>.
/// </summary>
internal static class ErpTestFixture
{
    internal const string ConnectionString =
        "Host=localhost;Port=5432;Database=erp_testdb;Username=erp_test;Password=erp_test_pw;Timeout=2";

    internal static readonly ErpConnectionConfig Config = new(
        Host: "localhost",
        Port: 5432,
        Database: "erp_testdb",
        Username: "erp_test",
        Password: "erp_test_pw"
    );

    internal static async Task<NpgsqlConnection?> TryOpenAsync()
    {
        try
        {
            var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            return conn;
        }
        catch
        {
            return null;
        }
    }

    internal static async Task<bool> IsAvailableAsync()
    {
        await using var conn = await TryOpenAsync();
        return conn is not null;
    }
}
