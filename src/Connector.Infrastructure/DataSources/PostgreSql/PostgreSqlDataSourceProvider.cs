using Connector.Core.DataSources;
using Npgsql;

namespace Connector.Infrastructure.DataSources.PostgreSql;

/// <summary>
/// The <see cref="IDataSourceProvider"/> for <see cref="DataSourceType.PostgreSql"/>. Owns every piece of
/// PostgreSQL-specific connection/schema/query logic that used to live directly in
/// <c>Connector.Api.Endpoints.ConnectionEndpoints</c> and <c>DynamicExportService</c>: building an Npgsql
/// connection string, introspecting <c>information_schema</c>, and executing a caller-built SQL query
/// generically. Registered as a singleton (see <c>Program.cs</c>) — it holds no per-call state; every method
/// opens and disposes its own <see cref="NpgsqlConnection"/>.
/// </summary>
public sealed class PostgreSqlDataSourceProvider : ISqlDataSourceProvider
{
    public DataSourceType Type => DataSourceType.PostgreSql;

    public DataSourceCapabilities Capabilities => DataSourceCapabilities.Sql;

    public ISqlDialect Dialect => PostgreSqlDialect.Instance;

    // Security-review finding SR-02: this previously interpolated Host/Database/Username/Password straight
    // into the connection-string text. A Password (or Username/Database) value containing ";Host=evil;..."
    // would append/override keys in the string Npgsql actually parses, letting a caller redirect the
    // connection despite host validation only checking the Host field. NpgsqlConnectionStringBuilder sets
    // each value as a typed property instead, so no field value can ever be interpreted as connection-string
    // syntax. No TrustServerCertificate: Npgsql 10 removed the behavior it used to control (SslMode=Prefer
    // already governs cert handling), and the property is now an obsolete no-op.
    // Port defaults to Postgres's own standard port when unset — DataSourceConfig.Port is nullable (Arbeitsauftrag
    // 3: not every DataSourceType has a Host/Port at all), but every caller reaching this provider has already
    // gone through ConnectionEndpoints' required-field validation for PostgreSql/MariaDb, so null here only
    // ever means "use the default," never "unset by mistake."
    // Also the choke point for the import paths (ImportNodeWalker/ImportRunReleaser and their callers), which
    // still open Npgsql connections directly: a config for any other source type is refused here with a clear
    // message instead of Npgsql trying to speak PostgreSQL's wire protocol to, say, a MariaDB server.
    public static string BuildConnectionString(DataSourceConfig config) =>
        config.Type != DataSourceType.PostgreSql
            ? throw new UnsupportedDataSourceException(
                $"Data source type '{config.Type}' is not supported here: imports currently require PostgreSQL."
            )
            : new NpgsqlConnectionStringBuilder
            {
                Host = config.Host,
                Port = config.Port ?? 5432,
                Database = config.Database,
                Username = config.Username,
                Password = config.Password,
                SslMode = ParseSslMode(config.SslMode),
                Timeout = 5,
                CommandTimeout = 10,
            }.ConnectionString;

    // Security-review finding SR-03: SslMode was previously hardcoded to Prefer everywhere — silently
    // downgrading to an unencrypted connection whenever the server doesn't offer TLS, with no way for an
    // operator to require and verify it instead. config.SslMode is validated against these same names at save
    // time (ConnectionEndpoints), but this falls back to the prior Prefer default rather than throwing for
    // null/empty/unrecognized input, so it can never itself turn a previously-working connection (or a config
    // saved before this field existed) into a hard failure.
    private static SslMode ParseSslMode(string? sslMode) =>
        !string.IsNullOrWhiteSpace(sslMode) && Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out var parsed)
            ? parsed
            : SslMode.Prefer;

    /// <summary>Opens a connection and reads its schema; never throws for a reachability/credential failure —
    /// that's reported via <see cref="TestConnectionResult.Failed"/> instead, sanitized of any
    /// connection-string/credential detail by <see cref="ErrorSanitizer.Detail"/> (security-review finding
    /// SR-14: Npgsql can echo the offending connection string, password included, back inside an exception
    /// message).</summary>
    public async Task<TestConnectionResult> TestConnectionAsync(
        DataSourceConfig config,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var schema = await ReadSchemaAsync(config, cancellationToken);
            return TestConnectionResult.Ok(schema);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return TestConnectionResult.Failed(ErrorSanitizer.Detail(ex));
        }
    }

    public async Task<SourceSchema> ReadSchemaAsync(DataSourceConfig config, CancellationToken cancellationToken)
    {
        await using var conn = new NpgsqlConnection(BuildConnectionString(config));
        await conn.OpenAsync(cancellationToken);
        var tables = await IntrospectTablesAsync(conn, cancellationToken);
        return new SourceSchema($"{config.Host}:{config.Port}/{config.Database}", tables);
    }

    // Introspects the public schema of an open Npgsql connection using information_schema views — moved
    // verbatim from the pre-abstraction ConnectionEndpoints.IntrospectSchemaAsync (same SQL, same shape).
    private static async Task<SourceTable[]> IntrospectTablesAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        var sql = """
            SELECT
                c.table_name,
                c.column_name,
                c.data_type,
                c.is_nullable,
                c.is_identity,
                c.is_generated,
                EXISTS (
                    SELECT 1
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                        ON kcu.constraint_name = tc.constraint_name
                        AND kcu.table_schema  = tc.table_schema
                        AND kcu.table_name    = tc.table_name
                        AND kcu.column_name   = c.column_name
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                      AND tc.table_schema    = 'public'
                      AND tc.table_name      = c.table_name
                ) AS is_pk,
                fk.foreign_table_name,
                fk.foreign_column_name
            FROM information_schema.columns c
            LEFT JOIN LATERAL (
                SELECT ccu.table_name AS foreign_table_name, ccu.column_name AS foreign_column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                    ON kcu.constraint_name = tc.constraint_name
                    AND kcu.table_schema   = tc.table_schema
                JOIN information_schema.constraint_column_usage ccu
                    ON ccu.constraint_name = tc.constraint_name
                    AND ccu.table_schema   = tc.table_schema
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_schema    = 'public'
                  AND tc.table_name      = c.table_name
                  AND kcu.column_name    = c.column_name
                LIMIT 1
            ) fk ON true
            WHERE c.table_schema = 'public'
            ORDER BY c.table_name, c.ordinal_position
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var byTable = new Dictionary<string, List<SourceColumn>>();
        while (await reader.ReadAsync(ct))
        {
            var table = reader.GetString(0);
            if (!byTable.ContainsKey(table))
                byTable[table] = [];
            byTable[table]
                .Add(
                    new SourceColumn(
                        Name: reader.GetString(1),
                        Type: reader.GetString(2),
                        Nullable: reader.GetString(3) == "YES",
                        PrimaryKey: reader.GetBoolean(6),
                        ForeignKeyTable: await reader.IsDBNullAsync(7, ct) ? null : reader.GetString(7),
                        ForeignKeyColumn: await reader.IsDBNullAsync(8, ct) ? null : reader.GetString(8),
                        IsIdentity: reader.GetString(4) == "YES",
                        // "ALWAYS" (GENERATED ALWAYS AS ... STORED) or "NEVER" — never NULL for a real column.
                        IsGenerated: reader.GetString(5) != "NEVER"
                    )
                );
        }

        return byTable.Select(kv => new SourceTable(kv.Key, "", kv.Value.ToArray())).OrderBy(t => t.Name).ToArray();
    }

    /// <summary>Compiles <paramref name="query"/> with <see cref="PostgreSqlQueryCompiler"/> against the live
    /// schema (read fresh on every call, so a column dropped since a definition was saved is reported as unknown
    /// rather than failing inside Postgres) and executes it — same row materialization as
    /// <see cref="ExecuteNativeAsync"/>.</summary>
    public async Task<QueryResult> ExecuteAsync(
        DataSourceConfig config,
        SourceQuery query,
        CancellationToken cancellationToken
    )
    {
        var schema = await ReadSchemaAsync(config, cancellationToken);
        var compiled = PostgreSqlQueryCompiler.Compile(query, schema);
        return await RunAsync(
            config,
            compiled.Sql,
            compiled.Parameters,
            DefaultCommandTimeoutSeconds,
            cancellationToken
        );
    }

    /// <summary>Executes <paramref name="query"/>'s SQL generically: the provider does not parse or understand
    /// the text itself, only materializes rows. Date/timestamp columns are coerced to ISO-8601
    /// (<c>yyyy-MM-dd</c>) the same way the pre-abstraction row-reading loop in <c>DynamicExportService</c> did,
    /// so every existing caller's output is unchanged.</summary>
    public Task<QueryResult> ExecuteNativeAsync(
        DataSourceConfig config,
        NativeSqlQuery query,
        CancellationToken cancellationToken
    )
    {
        var parameters = (query.Parameters ?? new Dictionary<string, object?>())
            .Select(kv => new NpgsqlParameter(kv.Key, kv.Value ?? DBNull.Value))
            .ToList();
        return RunAsync(
            config,
            query.Sql,
            parameters,
            query.CommandTimeoutSeconds ?? DefaultCommandTimeoutSeconds,
            cancellationToken,
            query.ReturnNativeText
        );
    }

    private const int DefaultCommandTimeoutSeconds = 30;

    private static async Task<QueryResult> RunAsync(
        DataSourceConfig config,
        string sql,
        IEnumerable<NpgsqlParameter> parameters,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken,
        bool returnNativeText = false
    )
    {
        await using var conn = new NpgsqlConnection(BuildConnectionString(config));
        await conn.OpenAsync(cancellationToken);

        // AllResultTypesAreUnknown makes Postgres send every column in its text output format, which Npgsql
        // then hands back verbatim as a string — while GetDataTypeName still reports the real column type.
        await using var cmd = new NpgsqlCommand(sql, conn)
        {
            CommandTimeout = commandTimeoutSeconds,
            AllResultTypesAreUnknown = returnNativeText,
        };
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
                rows.Add(
                    returnNativeText
                        ? await ReadNativeTextRowAsync(reader, cancellationToken)
                        : await ReadRowAsync(reader, cancellationToken)
                );
            return new QueryResult { Columns = columns, Rows = rows };
        }
        catch (PostgresException pex)
        {
            // Wrapped so callers that need to inspect a failure (by its SQLSTATE)
            // never need an Npgsql reference themselves.
            throw new DataSourceQueryException(pex.SqlState, pex.Message, pex);
        }
    }

    private static async Task<QueryResultRow> ReadNativeTextRowAsync(NpgsqlDataReader reader, CancellationToken ct)
    {
        var values = new string?[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            values[i] = await reader.IsDBNullAsync(i, ct) ? null : await reader.GetFieldValueAsync<string>(i, ct);
        return new QueryResultRow { Values = values };
    }

    // Same DBNull/date-coercion contract the pre-abstraction ExecuteQueryAsync/ImportNodeWalker row loops
    // used: a date/timestamp/timestamptz column is coerced to ISO-8601 regardless of locale; everything else
    // is Postgres's own ToString() of the CLR value Npgsql mapped it to (a json/jsonb column comes back as
    // its raw JSON text with Npgsql's default mapping, letting a caller re-parse it as JSON if it needs to).
    private static async Task<QueryResultRow> ReadRowAsync(NpgsqlDataReader reader, CancellationToken ct)
    {
        var values = new string?[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (await reader.IsDBNullAsync(i, ct))
                continue;

            var pgType = reader.GetDataTypeName(i);
            values[i] = pgType is "date" or "timestamp" or "timestamptz"
                ? reader.GetDateTime(i).ToString("yyyy-MM-dd")
                : reader.GetValue(i)?.ToString();
        }
        return new QueryResultRow { Values = values };
    }
}
