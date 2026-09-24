using System.Diagnostics;
using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources;

/// <summary>What one export read from its source: how many queries it sent, how many rows came
/// back, and how long the whole build took (queries, assembly and writing the output).</summary>
public readonly record struct ExportQueryMetrics(int QueryCount, long RecordsRead, long DurationMs);

/// <summary>
/// Wraps a provider for the duration of one export build and counts every query sent through it and every row it
/// returned — the data behind <see cref="ExportQueryMetrics"/>. Behaves exactly like the wrapped provider
/// otherwise, including its <see cref="Capabilities"/> and SQL <see cref="Dialect"/>. Not thread-safe: one instance
/// per build, and the export builders run their queries one after another.
/// </summary>
public sealed class MeteredDataSourceProvider(IDataSourceProvider inner) : ISqlDataSourceProvider
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public int QueryCount { get; private set; }

    public long RecordsRead { get; private set; }

    public ExportQueryMetrics Metrics => new(QueryCount, RecordsRead, _stopwatch.ElapsedMilliseconds);

    public DataSourceType Type => inner.Type;

    public DataSourceCapabilities Capabilities => inner.Capabilities;

    public string? ValidateConfig(DataSourceConfig config) => inner.ValidateConfig(config);

    public string TargetHost(DataSourceConfig config) => inner.TargetHost(config);

    public bool IsAlwaysEncrypted(DataSourceConfig config) => inner.IsAlwaysEncrypted(config);

    public ISqlDialect Dialect =>
        (inner as ISqlDataSourceProvider)?.Dialect
        ?? throw new UnsupportedDataSourceException($"Data source type '{inner.Type}' has no SQL dialect.");

    public Task<System.Data.Common.DbConnection> OpenConnectionAsync(
        DataSourceConfig config,
        CancellationToken cancellationToken
    ) =>
        (inner as ISqlDataSourceProvider)?.OpenConnectionAsync(config, cancellationToken)
        ?? throw new UnsupportedDataSourceException($"Data source type '{inner.Type}' has no SQL connection.");

    public Task<TestConnectionResult> TestConnectionAsync(
        DataSourceConfig config,
        CancellationToken cancellationToken
    ) => inner.TestConnectionAsync(config, cancellationToken);

    public Task<SourceSchema> ReadSchemaAsync(DataSourceConfig config, CancellationToken cancellationToken) =>
        inner.ReadSchemaAsync(config, cancellationToken);

    public Task<QueryResult> ExecuteAsync(
        DataSourceConfig config,
        SourceQuery query,
        CancellationToken cancellationToken
    ) => CountAsync(inner.ExecuteAsync(config, query, cancellationToken));

    public Task<QueryResult> ExecuteNativeAsync(
        DataSourceConfig config,
        NativeSqlQuery query,
        CancellationToken cancellationToken
    ) => CountAsync(inner.ExecuteNativeAsync(config, query, cancellationToken));

    private async Task<QueryResult> CountAsync(Task<QueryResult> query)
    {
        QueryCount++;
        var result = await query;
        RecordsRead += result.Rows.Count;
        return result;
    }
}
