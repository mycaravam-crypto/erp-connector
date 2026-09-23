using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources.ServiceNow;

/// <summary>
/// Reads ServiceNow's table/field metadata through the ordinary Table API — <c>sys_db_object</c> (tables and their
/// <c>super_class</c> inheritance) and <c>sys_dictionary</c> (fields) — into the same <see cref="SourceSchema"/>
/// shape the SQL providers return. Needs read access to those two tables (e.g. the <c>personalize_dictionary</c>
/// role), not admin; like every Table API read, the result only contains what the account's ACLs allow.
/// </summary>
/// <remarks>
/// Mapping: a table's fields are its own plus every ancestor's (e.g. <c>incident</c> inherits <c>task</c>'s);
/// <c>sys_id</c> is the primary key; a <c>reference</c> field is a foreign key to <c>&lt;referenced table&gt;.sys_id</c>
/// — so <c>incident.assignment_group</c> is a relation to <c>sys_user_group</c> like any SQL foreign key; a field
/// is nullable unless <c>mandatory</c>.
/// </remarks>
public sealed class ServiceNowSchemaReader(ServiceNowClient client)
{
    private static readonly string[] TableFields = ["sys_id", "name", "label", "super_class"];
    private static readonly string[] DictionaryFields = ["name", "element", "internal_type", "reference", "mandatory"];

    /// <summary>Every table the account can see, or — with <paramref name="tableNames"/> — just those (plus the
    /// ancestors their inherited fields come from), which is what a query needs.</summary>
    public async Task<SourceTable[]> ReadTablesAsync(
        DataSourceConfig config,
        IReadOnlyCollection<string>? tableNames,
        CancellationToken ct
    )
    {
        // A name that isn't a plain identifier can't be a ServiceNow table — drop it here (the query validator
        // then reports it as unknown) rather than let it reach an encoded query.
        tableNames = tableNames?.Where(ServiceNowQueryCompiler.IsIdentifier).ToList();
        var tables = await ReadTableRecordsAsync(config, tableNames, ct);
        var byId = tables.Where(t => t.SysId is not null).ToDictionary(t => t.SysId!, StringComparer.Ordinal);

        List<string> Chain(TableRecord table)
        {
            var chain = new List<string>();
            for (var t = table; t is not null && !chain.Contains(t.Name); )
            {
                chain.Add(t.Name);
                t = t.SuperClass is not null ? byId.GetValueOrDefault(t.SuperClass) : null;
            }
            return chain;
        }

        var wanted = tableNames is null ? tables : tables.Where(t => tableNames.Contains(t.Name)).ToList();
        var chains = wanted.ToDictionary(t => t.Name, Chain, StringComparer.Ordinal);
        var dictionaryTables = tableNames is null ? null : chains.Values.SelectMany(c => c).Distinct().ToList();
        var fieldsByTable = (await ReadDictionaryAsync(config, dictionaryTables, ct)).ToLookup(
            f => f.Table,
            StringComparer.Ordinal
        );

        return wanted
            .Select(t => new SourceTable(
                t.Name,
                t.Label ?? "",
                chains[t.Name]
                    .SelectMany(name => fieldsByTable[name])
                    .DistinctBy(f => f.Element, StringComparer.Ordinal) // a child's override wins over the parent's
                    .Select(ToColumn)
                    .ToArray()
            ))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<List<TableRecord>> ReadTableRecordsAsync(
        DataSourceConfig config,
        IReadOnlyCollection<string>? tableNames,
        CancellationToken ct
    )
    {
        if (tableNames is null)
            return (await client.GetRecordsAsync(config, "sys_db_object", "ORDERBYname", TableFields, null, ct))
                .Select(ToTableRecord)
                .OfType<TableRecord>()
                .ToList();
        if (tableNames.Count == 0)
            return [];

        // Resolve the requested tables, then walk up super_class until every ancestor is loaded.
        var loaded = new Dictionary<string, TableRecord>(StringComparer.Ordinal);
        var query = ServiceNowQueryCompiler.In("name", tableNames);
        while (query is not null)
        {
            foreach (var record in await client.GetRecordsAsync(config, "sys_db_object", query, TableFields, null, ct))
            {
                if (ToTableRecord(record) is { SysId: not null } table)
                    loaded.TryAdd(table.SysId, table);
            }
            var missingParents = loaded
                .Values.Select(t => t.SuperClass)
                .OfType<string>()
                .Where(id => !loaded.ContainsKey(id))
                .Distinct()
                .ToList();
            query = missingParents.Count > 0 ? ServiceNowQueryCompiler.In("sys_id", missingParents) : null;
        }
        return loaded.Values.ToList();
    }

    private async Task<List<DictionaryRecord>> ReadDictionaryAsync(
        DataSourceConfig config,
        IReadOnlyCollection<string>? tableNames,
        CancellationToken ct
    )
    {
        var query = "elementISNOTEMPTY";
        if (tableNames is not null)
            query = ServiceNowQueryCompiler.In("name", tableNames) + "^" + query;
        var records = await client.GetRecordsAsync(config, "sys_dictionary", query, DictionaryFields, null, ct);
        return records
            .Where(r => r.GetValueOrDefault("name") is not null && r.GetValueOrDefault("element") is not null)
            .Select(r => new DictionaryRecord(
                r["name"]!,
                r["element"]!,
                r.GetValueOrDefault("internal_type") ?? "string",
                r.GetValueOrDefault("reference"),
                r.GetValueOrDefault("mandatory") == "true"
            ))
            .ToList();
    }

    private static SourceColumn ToColumn(DictionaryRecord field)
    {
        var isReference = field.InternalType == "reference" && field.Reference is not null;
        return new SourceColumn(
            Name: field.Element,
            Type: field.InternalType,
            Nullable: !field.Mandatory && field.Element != "sys_id",
            PrimaryKey: field.Element == "sys_id",
            ForeignKeyTable: isReference ? field.Reference : null,
            ForeignKeyColumn: isReference ? "sys_id" : null
        );
    }

    private static TableRecord? ToTableRecord(Dictionary<string, string?> r) =>
        r.GetValueOrDefault("name") is { } name
            ? new(r.GetValueOrDefault("sys_id"), name, r.GetValueOrDefault("label"), r.GetValueOrDefault("super_class"))
            : null;

    private sealed record TableRecord(string? SysId, string Name, string? Label, string? SuperClass);

    private sealed record DictionaryRecord(
        string Table,
        string Element,
        string InternalType,
        string? Reference,
        bool Mandatory
    );
}
