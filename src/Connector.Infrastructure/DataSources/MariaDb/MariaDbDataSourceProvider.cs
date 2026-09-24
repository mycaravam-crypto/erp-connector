using System.Globalization;
using Connector.Core.DataSources;
using MySqlConnector;

namespace Connector.Infrastructure.DataSources.MariaDb;

/// <summary>
/// The <see cref="IDataSourceProvider"/> for <see cref="DataSourceType.MariaDb"/>, over
/// MySqlConnector — the same contract as <c>PostgreSqlDataSourceProvider</c>: connection test and schema read
/// (<see cref="MariaDbSchemaReader"/>), neutral queries (<see cref="MariaDbQueryCompiler"/>) and native SQL the
/// export builders render with <see cref="MariaDbDialect"/>. Stateless singleton; every call opens and disposes
/// its own connection (<see cref="MariaDbConnectionFactory"/>).
/// </summary>
public sealed class MariaDbDataSourceProvider : ISqlDataSourceProvider
{
    private const int DefaultCommandTimeoutSeconds = 30;

    public DataSourceType Type => DataSourceType.MariaDb;

    // Imports stay PostgreSQL-only until the walker/releaser have been verified against MariaDB (the dialect already
    // covers their SQL: CAST … AS CHAR, <=>).
    public DataSourceCapabilities Capabilities { get; } = DataSourceCapabilities.Sql with { Imports = false };

    public async Task<System.Data.Common.DbConnection> OpenConnectionAsync(
        DataSourceConfig config,
        CancellationToken cancellationToken
    ) => await MariaDbConnectionFactory.OpenAsync(config, cancellationToken);

    public string? ValidateConfig(DataSourceConfig config) => RelationalConnectionRules.Validate(config);

    public string TargetHost(DataSourceConfig config) => config.Host!;

    public bool IsAlwaysEncrypted(DataSourceConfig config) => RelationalConnectionRules.IsAlwaysEncrypted(config);

    public ISqlDialect Dialect => MariaDbDialect.Instance;

    /// <summary>Never throws for a reachability/credential failure — reported, sanitized, via
    /// <see cref="TestConnectionResult.Failed"/> instead.</summary>
    public async Task<TestConnectionResult> TestConnectionAsync(
        DataSourceConfig config,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return TestConnectionResult.Ok(await ReadSchemaAsync(config, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return TestConnectionResult.Failed(ErrorSanitizer.Detail(ex));
        }
    }

    public async Task<SourceSchema> ReadSchemaAsync(DataSourceConfig config, CancellationToken cancellationToken)
    {
        await using var conn = await MariaDbConnectionFactory.OpenAsync(config, cancellationToken);
        var tables = await MariaDbSchemaReader.ReadTablesAsync(conn, cancellationToken);
        return new SourceSchema($"{config.Host}:{config.Port}/{config.Database}", tables);
    }

    /// <summary>Compiles against the live schema (read fresh, so a dropped column fails as
    /// <see cref="InvalidSourceQueryException"/> before any SQL runs) and executes.</summary>
    public async Task<QueryResult> ExecuteAsync(
        DataSourceConfig config,
        SourceQuery query,
        CancellationToken cancellationToken
    )
    {
        var schema = await ReadSchemaAsync(config, cancellationToken);
        var compiled = MariaDbQueryCompiler.Compile(query, schema);
        return await RunAsync(
            config,
            compiled.Sql,
            compiled.Parameters,
            DefaultCommandTimeoutSeconds,
            false,
            cancellationToken
        );
    }

    public Task<QueryResult> ExecuteNativeAsync(
        DataSourceConfig config,
        NativeSqlQuery query,
        CancellationToken cancellationToken
    )
    {
        var parameters = (query.Parameters ?? new Dictionary<string, object?>())
            .Select(kv => new MySqlParameter(kv.Key, kv.Value ?? DBNull.Value))
            .ToList();
        return RunAsync(
            config,
            query.Sql,
            parameters,
            query.CommandTimeoutSeconds ?? DefaultCommandTimeoutSeconds,
            query.ReturnNativeText,
            cancellationToken
        );
    }

    private static async Task<QueryResult> RunAsync(
        DataSourceConfig config,
        string sql,
        IEnumerable<MySqlParameter> parameters,
        int commandTimeoutSeconds,
        bool returnNativeText,
        CancellationToken cancellationToken
    )
    {
        await using var conn = await MariaDbConnectionFactory.OpenAsync(config, cancellationToken);
        await using var cmd = new MySqlCommand(sql, conn) { CommandTimeout = commandTimeoutSeconds };
        cmd.Parameters.AddRange(parameters.ToArray());

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var columns = Enumerable
                .Range(0, reader.FieldCount)
                .Select(i => new QueryResultColumn { Name = reader.GetName(i), DataType = reader.GetDataTypeName(i) })
                .ToList();
            var rows = new List<QueryResultRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var values = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    if (await reader.IsDBNullAsync(i, cancellationToken))
                        continue;
                    var value = reader.GetValue(i);
                    values[i] = returnNativeText
                        ? FormatNative(value, columns[i].DataType!)
                        : MariaDbDialect.Instance.FormatValue(value, columns[i].DataType!);
                }
                rows.Add(new QueryResultRow { Values = values });
            }
            return new QueryResult { Columns = columns, Rows = rows };
        }
        catch (MySqlException ex) when (ex.SqlState is not null)
        {
            // A server-reported error (e.g. 42S02 unknown table), wrapped like PostgreSQL's SQLSTATE failures so
            // callers never reference MySqlConnector. Client-side failures (timeout, lost connection) carry no
            // SQLSTATE and propagate as-is, as Npgsql's do.
            throw new DataSourceQueryException(ex.SqlState, ex.Message, ex);
        }
    }

    // The provider's "native text": MariaDB's own text rendering of each value (what the server sends over the
    // text protocol), rebuilt from the CLR value MySqlConnector parsed it into — MariaDbDialect.ConvertNativeTextToJson
    // interprets exactly this format.
    private static string FormatNative(object value, string dataType) =>
        value switch
        {
            bool b => b ? "1" : "0",
            DateTime dt when dataType == "DATE" => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString(
                dt.Ticks % TimeSpan.TicksPerSecond == 0 ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm:ss.ffffff",
                CultureInfo.InvariantCulture
            ),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };
}
