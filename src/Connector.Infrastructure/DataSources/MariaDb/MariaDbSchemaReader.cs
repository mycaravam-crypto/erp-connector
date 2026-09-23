using Connector.Core.DataSources;
using MySqlConnector;

namespace Connector.Infrastructure.DataSources.MariaDb;

/// <summary>
/// Reads the tables of the connection's current database (<c>DATABASE()</c>) from <c>information_schema</c>
/// (<c>TABLES</c>, <c>COLUMNS</c>, <c>KEY_COLUMN_USAGE</c>, <c>REFERENTIAL_CONSTRAINTS</c>) into the same
/// <see cref="SourceTable"/> shape the PostgreSQL provider returns.
/// </summary>
public static class MariaDbSchemaReader
{
    private const string Sql = """
        SELECT
            c.TABLE_NAME,
            c.COLUMN_NAME,
            c.DATA_TYPE,
            c.IS_NULLABLE,
            c.EXTRA,
            EXISTS (
                SELECT 1 FROM information_schema.KEY_COLUMN_USAGE pk
                WHERE pk.TABLE_SCHEMA = c.TABLE_SCHEMA
                  AND pk.TABLE_NAME = c.TABLE_NAME
                  AND pk.COLUMN_NAME = c.COLUMN_NAME
                  AND pk.CONSTRAINT_NAME = 'PRIMARY'
            ) AS IS_PK,
            (
                SELECT fk.REFERENCED_TABLE_NAME FROM information_schema.KEY_COLUMN_USAGE fk
                JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
                    ON rc.CONSTRAINT_SCHEMA = fk.CONSTRAINT_SCHEMA AND rc.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
                WHERE fk.TABLE_SCHEMA = c.TABLE_SCHEMA
                  AND fk.TABLE_NAME = c.TABLE_NAME
                  AND fk.COLUMN_NAME = c.COLUMN_NAME
                ORDER BY fk.CONSTRAINT_NAME
                LIMIT 1
            ) AS FK_TABLE,
            (
                SELECT fk.REFERENCED_COLUMN_NAME FROM information_schema.KEY_COLUMN_USAGE fk
                JOIN information_schema.REFERENTIAL_CONSTRAINTS rc
                    ON rc.CONSTRAINT_SCHEMA = fk.CONSTRAINT_SCHEMA AND rc.CONSTRAINT_NAME = fk.CONSTRAINT_NAME
                WHERE fk.TABLE_SCHEMA = c.TABLE_SCHEMA
                  AND fk.TABLE_NAME = c.TABLE_NAME
                  AND fk.COLUMN_NAME = c.COLUMN_NAME
                ORDER BY fk.CONSTRAINT_NAME
                LIMIT 1
            ) AS FK_COLUMN
        FROM information_schema.COLUMNS c
        JOIN information_schema.TABLES t
            ON t.TABLE_SCHEMA = c.TABLE_SCHEMA AND t.TABLE_NAME = c.TABLE_NAME
        WHERE c.TABLE_SCHEMA = DATABASE()
        ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
        """;

    public static async Task<SourceTable[]> ReadTablesAsync(MySqlConnection connection, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(Sql, connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var byTable = new Dictionary<string, List<SourceColumn>>();
        while (await reader.ReadAsync(ct))
        {
            var table = reader.GetString(0);
            if (!byTable.TryGetValue(table, out var columns))
                byTable[table] = columns = [];

            // EXTRA holds e.g. "auto_increment", "VIRTUAL GENERATED", "STORED GENERATED".
            var extra = reader.GetString(4);
            columns.Add(
                new SourceColumn(
                    Name: reader.GetString(1),
                    Type: reader.GetString(2),
                    Nullable: reader.GetString(3) == "YES",
                    PrimaryKey: reader.GetInt32(5) == 1,
                    ForeignKeyTable: await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6),
                    ForeignKeyColumn: await reader.IsDBNullAsync(7, ct) ? null : reader.GetString(7),
                    IsIdentity: extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase),
                    IsGenerated: extra.Contains("GENERATED", StringComparison.OrdinalIgnoreCase)
                )
            );
        }

        return byTable
            .Select(kv => new SourceTable(kv.Key, "", kv.Value.ToArray()))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
