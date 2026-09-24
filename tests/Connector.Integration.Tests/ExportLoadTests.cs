using System.Diagnostics;
using Connector.Core.DataSources;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;
using Connector.Infrastructure.DataSources.PostgreSql;
using MySqlConnector;
using Npgsql;
using Xunit.Abstractions;

namespace Connector.Integration.Tests;

/// <summary>
/// Load tests for the export query plan: an order → lines tree over 1 000, 10 000 and 100 000
/// root records (two lines each) against real PostgreSQL and MariaDB. Asserts the query count is independent of
/// the row count (one root query plus one child query per 10 000-key batch — never one per root record), that
/// exactly the needed rows are read, and reports duration, allocated memory and output size. The <c>perf_*</c>
/// tables are created and filled on first use (100 000 orders, 200 000 lines) and only ever read afterwards. No-op
/// per backend when its fixture isn't running.
/// </summary>
public sealed class ExportLoadTests(ITestOutputHelper output)
{
    private const int MaxRecords = 100_000;
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static readonly HashSet<string> Seeded = [];

    public static TheoryData<string, int> Cases =>
        new()
        {
            { "postgres", 1_000 },
            { "postgres", 10_000 },
            { "postgres", 100_000 },
            { "mariadb", 1_000 },
            { "mariadb", 10_000 },
            { "mariadb", 100_000 },
        };

    private static ExportNode Tree(int records) =>
        new(
            "root",
            ExportNodeKind.Root,
            null,
            null,
            null,
            null,
            $"id <= {records}",
            null,
            [
                new("id", ExportNodeKind.ScalarField, "id", null, null, null, null, null, [], true),
                new("note", ExportNodeKind.ScalarField, "note", null, null, null, null, null, [], true),
                new(
                    "lines",
                    ExportNodeKind.Array,
                    null,
                    "perf_line",
                    "order_id",
                    "id",
                    null,
                    null,
                    [new("sku", ExportNodeKind.ScalarField, "sku", null, null, null, null, null, [], true)],
                    true
                ),
            ],
            true
        );

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task OrderLinesTree_QueryCountIsIndependentOfRecordCount(string backend, int records)
    {
        var (provider, config) =
            backend == "postgres"
                ? ((IDataSourceProvider)new PostgreSqlDataSourceProvider(), ErpTestFixture.Config)
                : (new MariaDbDataSourceProvider(), MariaDbTestFixture.Config);
        if (!await (backend == "postgres" ? ErpTestFixture.IsAvailableAsync() : MariaDbTestFixture.IsAvailableAsync()))
            return;
        await SeedAsync(backend);

        var allocatedBefore = GC.GetTotalAllocatedBytes();
        var built = await DynamicExportService.BuildExportNodeAsync(
            provider,
            config,
            "perf_order",
            Tree(records),
            "json",
            "v1",
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            gdprDenylist: new HashSet<string>()
        );
        var allocatedMb = (GC.GetTotalAllocatedBytes() - allocatedBefore) / 1024 / 1024;

        var expectedQueries = 1 + (int)Math.Ceiling(records / 10_000.0);
        output.WriteLine(
            $"{backend} {records, 7:N0} records: {built.Metrics.QueryCount} queries, {built.Metrics.RecordsRead:N0} rows read, "
                + $"{built.Metrics.DurationMs:N0} ms, ~{allocatedMb:N0} MB allocated, {built.Bytes.Length / 1024:N0} KB output"
        );

        Assert.Equal(records, built.RecordCount);
        Assert.Equal(expectedQueries, built.Metrics.QueryCount);
        Assert.Equal(records * 3L, built.Metrics.RecordsRead); // each order + its two lines, nothing else
        Assert.True(built.Bytes.Length > records * 20, "output unexpectedly small");
        Assert.True(built.Metrics.DurationMs < 120_000, $"took {built.Metrics.DurationMs} ms");
    }

    [Fact]
    public async Task Cancellation_StopsALargeExport()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;
        await SeedAsync("postgres");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DynamicExportService.BuildExportNodeAsync(
                new PostgreSqlDataSourceProvider(),
                ErpTestFixture.Config,
                "perf_order",
                Tree(MaxRecords),
                "json",
                "v1",
                DateTimeOffset.UtcNow,
                cts.Token,
                gdprDenylist: new HashSet<string>()
            )
        );
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"cancellation took {stopwatch.Elapsed}");
    }

    private static async Task SeedAsync(string backend)
    {
        await SeedLock.WaitAsync();
        try
        {
            if (!Seeded.Add(backend))
                return;
            if (backend == "postgres")
                await SeedPostgresAsync();
            else
                await SeedMariaDbAsync();
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static async Task SeedPostgresAsync()
    {
        await using var conn = new NpgsqlConnection(ErpTestFixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"""
            CREATE TABLE IF NOT EXISTS perf_order (id integer PRIMARY KEY, note character varying(40));
            CREATE TABLE IF NOT EXISTS perf_line (id integer PRIMARY KEY, order_id integer NOT NULL, sku character varying(20));
            INSERT INTO perf_order SELECT g, 'order ' || g FROM generate_series(1, {MaxRecords}) g
                WHERE NOT EXISTS (SELECT 1 FROM perf_order);
            INSERT INTO perf_line SELECT g, (g + 1) / 2, 'SKU-' || g FROM generate_series(1, {MaxRecords * 2}) g
                WHERE NOT EXISTS (SELECT 1 FROM perf_line);
            """,
            conn
        )
        {
            CommandTimeout = 300,
        };
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedMariaDbAsync()
    {
        await using var conn = await MariaDbConnectionFactory.OpenAsync(
            MariaDbTestFixture.Config,
            CancellationToken.None
        );
        async Task RunAsync(string sql)
        {
            await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = 300 };
            await cmd.ExecuteNonQueryAsync();
        }

        await RunAsync("CREATE TABLE IF NOT EXISTS perf_order (id INT PRIMARY KEY, note VARCHAR(40)) ENGINE = InnoDB");
        await RunAsync(
            "CREATE TABLE IF NOT EXISTS perf_line (id INT PRIMARY KEY, order_id INT NOT NULL, sku VARCHAR(20)) ENGINE = InnoDB"
        );
        // seq_1_to_N comes from MariaDB's built-in SEQUENCE engine.
        await RunAsync(
            $"INSERT INTO perf_order SELECT seq, CONCAT('order ', seq) FROM seq_1_to_{MaxRecords} "
                + "WHERE NOT EXISTS (SELECT 1 FROM perf_order)"
        );
        await RunAsync(
            $"INSERT INTO perf_line SELECT seq, (seq + 1) DIV 2, CONCAT('SKU-', seq) FROM seq_1_to_{MaxRecords * 2} "
                + "WHERE NOT EXISTS (SELECT 1 FROM perf_line)"
        );
    }
}
