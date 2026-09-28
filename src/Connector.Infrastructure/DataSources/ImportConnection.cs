using System.Data.Common;
using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources;

/// <summary>
/// The import path's handle on the ERP: an open ADO.NET connection plus the SQL dialect to
/// render statements with, both from the configured provider — so <see cref="ImportNodeWalker"/> and
/// <see cref="ImportRunReleaser"/> never name a database driver. Only a provider with the
/// <see cref="DataSourceCapabilities.Imports"/> capability hands one out.
/// </summary>
public sealed class ImportConnection(DbConnection connection, ISqlDialect dialect) : IAsyncDisposable
{
    public DbConnection Connection { get; } = connection;

    public ISqlDialect Dialect { get; } = dialect;

    /// <summary>
    /// Opens a connection to the configured data source for an import, paired with that provider's SQL dialect.
    /// Throws UnsupportedDataSourceException when the provider isn't SQL-based or lacks the Imports capability.
    /// </summary>
    public static async Task<ImportConnection> OpenAsync(
        IDataSourceProviderResolver resolver,
        DataSourceConfig config,
        CancellationToken ct
    )
    {
        var provider = resolver.Resolve(config.Type);
        if (provider is not ISqlDataSourceProvider sql || !provider.Capabilities.Imports)
            throw new UnsupportedDataSourceException(
                $"Imports are not supported for data source type '{config.Type}'."
            );
        return new ImportConnection(await sql.OpenConnectionAsync(config, ct), sql.Dialect);
    }

    public ValueTask DisposeAsync() => Connection.DisposeAsync();
}
