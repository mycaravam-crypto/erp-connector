using Connector.Core.DataSources;
using Npgsql;

namespace Connector.Infrastructure;

/// <summary>
/// The <see cref="IDataSourceProvider"/> for <see cref="DataSourceType.PostgreSql"/> — the only backend the
/// connector supports today (Arbeitsauftrag 2 explicitly defers MariaDb/ServiceNow). Owns every piece of
/// PostgreSQL-specific connection/schema/query logic that used to live directly in
/// <c>Connector.Api.Endpoints.ConnectionEndpoints</c> and <c>DynamicExportService</c>: building an Npgsql
/// connection string, introspecting <c>information_schema</c>, and executing a caller-built SQL query
/// generically. Registered as a singleton (see <c>Program.cs</c>) — it holds no per-call state; every method
/// opens and disposes its own <see cref="NpgsqlConnection"/>.
/// </summary>
public sealed class PostgreSqlDataSourceProvider : IDataSourceProvider
{
    public DataSourceType Type => DataSourceType.PostgreSql;

    // Security-review finding SR-02: this previously interpolated Host/Database/Username/Password straight
    // into the connection-string text. A Password (or Username/Database) value containing ";Host=evil;..."
    // would append/override keys in the string Npgsql actually parses, letting a caller redirect the
    // connection despite host validation only checking the Host field. NpgsqlConnectionStringBuilder sets
    // each value as a typed property instead, so no field value can ever be interpreted as connection-string
    // syntax. No TrustServerCertificate: Npgsql 10 removed the behavior it used to control (SslMode=Prefer
    // already governs cert handling), and the property is now an obsolete no-op.
    public static string BuildConnectionString(DataSourceConfig config) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = config.Host,
            Port = config.Port,
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

    /// <summary>Executes <paramref name="query"/>'s SQL generically: the provider does not parse or understand
    /// the text itself, only materializes rows as string-keyed dictionaries. Date/timestamp columns are
    /// coerced to ISO-8601 (<c>yyyy-MM-dd</c>) the same way the pre-abstraction row-reading loop in
    /// <c>DynamicExportService</c> did, so every existing caller's output is unchanged.</summary>
    public async Task<QueryResult> ExecuteAsync(
        DataSourceConfig config,
        SourceQuery query,
        CancellationToken cancellationToken
    )
    {
        await using var conn = new NpgsqlConnection(BuildConnectionString(config));
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(query.Sql, conn) { CommandTimeout = query.CommandTimeoutSeconds ?? 30 };
        if (query.Parameters is not null)
            foreach (var (name, value) in query.Parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var rows = new List<IReadOnlyDictionary<string, string?>>();
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(await ReadRowAsync(reader, cancellationToken));
            return new QueryResult(rows);
        }
        catch (PostgresException pex)
        {
            // Wrapped so callers (e.g. DynamicExportService's cardinality-violation guard on SQLSTATE 21000)
            // never need an Npgsql reference themselves.
            throw new DataSourceQueryException(pex.SqlState, pex.Message, pex);
        }
    }

    // Same DBNull/date-coercion contract the pre-abstraction ExecuteQueryAsync/ImportNodeWalker row loops
    // used: a date/timestamp/timestamptz column is coerced to ISO-8601 regardless of locale; everything else
    // is Postgres's own ToString() of the CLR value Npgsql mapped it to (a json/jsonb column comes back as
    // its raw JSON text with Npgsql's default mapping, letting a caller re-parse it as JSON if it needs to).
    private static async Task<IReadOnlyDictionary<string, string?>> ReadRowAsync(
        NpgsqlDataReader reader,
        CancellationToken ct
    )
    {
        var row = new Dictionary<string, string?>(reader.FieldCount);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (await reader.IsDBNullAsync(i, ct))
            {
                row[reader.GetName(i)] = null;
                continue;
            }

            var pgType = reader.GetDataTypeName(i);
            row[reader.GetName(i)] = pgType is "date" or "timestamp" or "timestamptz"
                ? reader.GetDateTime(i).ToString("yyyy-MM-dd")
                : reader.GetValue(i)?.ToString();
        }
        return row;
    }
}
