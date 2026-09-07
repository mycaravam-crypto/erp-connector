using System.Text.Json;
using System.Text.Json.Nodes;

namespace Connector.Infrastructure;

public static partial class DynamicExportService
{
    /// <summary>
    /// ERP field names that must never appear in any export artifact (GDPR Art. 5(1)(c)).
    /// Checked at mapping-save time (API) and stripped at query time as defence-in-depth.
    /// This is the hardcoded fallback; admins can override via the gdpr_denied_fields AppSetting.
    /// </summary>
    public static readonly IReadOnlySet<string> GdprDeniedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "technician_name",
        "technician_id",
        "employee_id",
        "contact_name",
        "contact_email",
        "contact_phone",
        "operator_name",
    };

    /// <summary>
    /// Returns the active GDPR denylist: the DB-stored list if present, else <see cref="GdprDeniedFields"/>.
    /// </summary>
    public static async Task<IReadOnlySet<string>> GetDeniedFieldsAsync(ExportLogDbContext db)
    {
        var setting = await db.AppSettings.FindAsync("gdpr_denied_fields");
        if (setting is not null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            var parsed = JsonSerializer.Deserialize<string[]>(setting.Value);
            if (parsed is { Length: > 0 })
                return new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        return GdprDeniedFields;
    }

    // Walks the parsed JSON tree removing any property whose key matches the GDPR denylist, at every
    // depth. Shared by the legacy nested-JSON path and the ExportNode tree engine — same "match by output
    // key name" defence-in-depth heuristic both apply post-query.
    private static void StripGdprFieldsRecursive(JsonNode? node, IReadOnlySet<string> denylist)
    {
        if (node is JsonObject obj)
        {
            foreach (var key in obj.Select(kv => kv.Key).Where(denylist.Contains).ToList())
                obj.Remove(key);
            foreach (var kv in obj)
                StripGdprFieldsRecursive(kv.Value, denylist);
        }
        else if (node is JsonArray arr)
        {
            foreach (var item in arr)
                StripGdprFieldsRecursive(item, denylist);
        }
    }
}
