using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources.ServiceNow;

/// <summary>
/// The <see cref="IDataSourceProvider"/> for <see cref="DataSourceType.ServiceNowTableApi"/> (Arbeitsauftrag 9):
/// ServiceNow read over its REST Table API, behind the same interface as the SQL providers — callers see
/// <see cref="SourceSchema"/>, <see cref="SourceQuery"/> and <see cref="QueryResult"/>, never HTTP. Not an
/// <see cref="ISqlDataSourceProvider"/>: there is no SQL to run, so <see cref="ExecuteNativeAsync"/> and the
/// SQL-rendering export builders refuse it with <see cref="UnsupportedDataSourceException"/>.
/// </summary>
/// <remarks>
/// Joins run in C#: after the root read, each join reads the joined table with <c>column IN (parent values)</c> in
/// batches of <see cref="JoinKeyBatchSize"/> — one request per batch and page, never one per root record — and
/// matches rows by value, as an inner or left equi-join. ServiceNow returns every value as a string and an empty
/// field as <c>""</c>; the provider reports both an empty and a missing field as <c>null</c>.
/// </remarks>
public sealed class ServiceNowTableApiProvider : IDataSourceProvider
{
    /// <summary>Parent key values per joined-table request — keeps the encoded query (part of the URL) short.</summary>
    public const int JoinKeyBatchSize = 100;

    private static readonly HttpClient SharedHttpClient = new(
        new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }
    )
    {
        Timeout = Timeout.InfiniteTimeSpan, // ServiceNowClient enforces its own per-request timeout
    };

    private readonly ServiceNowClient _client;
    private readonly ServiceNowSchemaReader _schemaReader;

    public ServiceNowTableApiProvider()
        : this(new ServiceNowClient(SharedHttpClient)) { }

    public ServiceNowTableApiProvider(ServiceNowClient client)
    {
        _client = client;
        _schemaReader = new ServiceNowSchemaReader(client);
    }

    public DataSourceType Type => DataSourceType.ServiceNowTableApi;

    // No SQL; ServiceNow's LIKE/STARTSWITH/ENDSWITH ignore case; the Table API has no NULL (empty is ""); a filter on
    // a left-joined table would change the join's semantics when applied to the joined read (see the compiler).
    public DataSourceCapabilities Capabilities { get; } =
        new()
        {
            NativeSql = false,
            CaseSensitiveTextMatch = false,
            DistinguishesEmptyFromNull = false,
            ConditionsOnLeftJoinedTables = false,
        };

    /// <summary>Never throws for a reachability/credential/permission failure — reported via
    /// <see cref="TestConnectionResult.Failed"/>.</summary>
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

    public async Task<SourceSchema> ReadSchemaAsync(DataSourceConfig config, CancellationToken cancellationToken) =>
        new(
            ServiceNowClient.ParseInstanceUrl(config.InstanceUrl).Host,
            await _schemaReader.ReadTablesAsync(config, null, cancellationToken)
        );

    public async Task<QueryResult> ExecuteAsync(
        DataSourceConfig config,
        SourceQuery query,
        CancellationToken cancellationToken
    )
    {
        // Only the tables this query touches — reading the whole dictionary per query would be far too slow.
        var tableNames = query.Joins.Select(j => j.Table).Prepend(query.RootTable).Distinct().ToList();
        var schema = new SourceSchema(
            config.InstanceUrl ?? "",
            await _schemaReader.ReadTablesAsync(config, tableNames, cancellationToken)
        );
        var plan = ServiceNowQueryCompiler.Compile(query, schema);

        var rootRecords = await ReadAsync(config, plan.Root, plan.RootLimit, cancellationToken);
        // Each combined row maps table name → that table's record (null for an unmatched left join).
        IEnumerable<Dictionary<string, Dictionary<string, string?>?>> rows = rootRecords.Select(r => new Dictionary<
            string,
            Dictionary<string, string?>?
        >(StringComparer.Ordinal)
        {
            [plan.Root.Table] = r,
        });
        foreach (var step in plan.Joins)
            rows = await JoinAsync(config, rows.ToList(), step, cancellationToken);
        if (plan.Limit is { } limit)
            rows = rows.Take(limit);

        return new QueryResult
        {
            Columns = plan.Columns.Select(c => new QueryResultColumn { Name = c.Name }).ToList(),
            Rows = rows.Select(row => new QueryResultRow
                {
                    Values = plan
                        .Columns.Select(c => row.GetValueOrDefault(c.Table)?.GetValueOrDefault(c.Column))
                        .ToList(),
                })
                .ToList(),
        };
    }

    public Task<QueryResult> ExecuteNativeAsync(
        DataSourceConfig config,
        NativeSqlQuery query,
        CancellationToken cancellationToken
    ) =>
        throw new UnsupportedDataSourceException(
            "The ServiceNow Table API has no native SQL; query it with a SourceQuery instead."
        );

    private async Task<List<Dictionary<string, string?>>> ReadAsync(
        DataSourceConfig config,
        ServiceNowTableRequest request,
        int? limit,
        CancellationToken ct,
        string? extraFilter = null
    ) =>
        await _client.GetRecordsAsync(
            config,
            request.Table,
            extraFilter is null ? request.EncodedQuery : extraFilter + "^" + request.EncodedQuery,
            request.Fields,
            limit,
            ct
        );

    private async Task<List<Dictionary<string, Dictionary<string, string?>?>>> JoinAsync(
        DataSourceConfig config,
        List<Dictionary<string, Dictionary<string, string?>?>> rows,
        ServiceNowJoinStep step,
        CancellationToken ct
    )
    {
        var join = step.Join;
        string? ParentKey(Dictionary<string, Dictionary<string, string?>?> row) =>
            row.GetValueOrDefault(step.ParentTable)?.GetValueOrDefault(join.ParentColumn);

        var keys = rows.Select(ParentKey).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var matches = new List<Dictionary<string, string?>>();
        foreach (var batch in keys.Chunk(JoinKeyBatchSize))
            matches.AddRange(
                await ReadAsync(config, step.Request, null, ct, ServiceNowQueryCompiler.In(join.Column, batch))
            );
        var byKey = matches
            .Where(m => m.GetValueOrDefault(join.Column) is not null)
            .ToLookup(m => m[join.Column]!, StringComparer.Ordinal);

        var joined = new List<Dictionary<string, Dictionary<string, string?>?>>();
        foreach (var row in rows)
        {
            var found = ParentKey(row) is { } key ? byKey[key].ToList() : [];
            if (found.Count == 0 && join.Type == QueryJoinType.Left)
                joined.Add(new(row, StringComparer.Ordinal) { [join.Table] = null });
            foreach (var match in found)
                joined.Add(new(row, StringComparer.Ordinal) { [join.Table] = match });
        }
        return joined;
    }
}
