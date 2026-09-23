using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources.ServiceNow;

namespace Connector.Integration.Tests;

/// <summary>
/// An in-memory stand-in for a ServiceNow instance's Table API (<c>GET /api/now/table/{table}</c>), used as the
/// <see cref="HttpMessageHandler"/> behind <see cref="ServiceNowClient"/> — no real instance or credentials in CI.
/// Serves the configured tables plus <c>sys_db_object</c>/<c>sys_dictionary</c> generated from them, and
/// implements the encoded-query subset the connector emits (<c>=</c>, <c>!=</c>, <c>&lt;</c>/<c>&gt;</c>,
/// <c>LIKE</c>, <c>STARTSWITH</c>, <c>ENDSWITH</c>, <c>IN</c>, <c>ISEMPTY</c>, <c>ISNOTEMPTY</c>, <c>ORDERBY</c>,
/// joined by <c>^</c>), <c>sysparm_fields</c>, <c>sysparm_limit</c>/<c>sysparm_offset</c>, Basic auth (401),
/// per-table ACL denial (403) and scripted failures/delays.
/// </summary>
internal sealed partial class FakeServiceNow : HttpMessageHandler
{
    public const string InstanceUrl = "https://fake.service-now.com";
    public const string Username = "svc_reader";
    public const string Password = "sn-secret-pw";

    public static DataSourceConfig Config =>
        new()
        {
            Type = DataSourceType.ServiceNowTableApi,
            InstanceUrl = InstanceUrl,
            Username = Username,
            Password = Password,
        };

    public sealed record Field(string Name, string Type = "string", string? Reference = null, bool Mandatory = false);

    public sealed record Table(string Name, string? Parent, Field[] Fields, List<Dictionary<string, string>> Rows);

    private readonly Dictionary<string, Table> _tables = new(StringComparer.Ordinal);

    /// <summary>Every request URL received, in order.</summary>
    public ConcurrentQueue<Uri> Requests { get; } = new();

    /// <summary>Tables the account's ACLs deny (403).</summary>
    public HashSet<string> ForbiddenTables { get; } = new(StringComparer.Ordinal);

    /// <summary>Status codes to answer the next requests with, in order, before serving normally again.</summary>
    public Queue<(HttpStatusCode Status, TimeSpan? RetryAfter)> ScriptedFailures { get; } = new();

    /// <summary>Delay before every response (for timeout/cancellation tests).</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public FakeServiceNow AddTable(
        string name,
        string? parent,
        Field[] fields,
        params Dictionary<string, string>[] rows
    )
    {
        _tables[name] = new Table(name, parent, fields, rows.ToList());
        return this;
    }

    public IEnumerable<Uri> TableRequests(string table) =>
        Requests.Where(u => u.AbsolutePath == $"/api/now/table/{table}");

    public ServiceNowTableApiProvider CreateProvider(ServiceNowClientOptions? options = null) =>
        new(
            new ServiceNowClient(
                new HttpClient(this),
                options
                    ?? new ServiceNowClientOptions
                    {
                        RetryBaseDelay = TimeSpan.FromMilliseconds(1),
                        RequestTimeout = TimeSpan.FromSeconds(5),
                    }
            )
        );

    /// <summary>A small incident/task/user-group instance: <c>incident</c> extends <c>task</c> and references
    /// <c>sys_user_group</c> through <c>assignment_group</c>.</summary>
    public static FakeServiceNow WithIncidents() =>
        new FakeServiceNow()
            .AddTable(
                "task",
                null,
                [
                    new("sys_id", "GUID"),
                    new("number", Mandatory: true),
                    new("short_description"),
                    new("priority", "integer"),
                ]
            )
            .AddTable(
                "incident",
                "task",
                [new("assignment_group", "reference", "sys_user_group"), new("category")],
                Row(
                    "i1",
                    ("number", "INC001"),
                    ("short_description", "Printer down"),
                    ("priority", "1"),
                    ("assignment_group", "g1"),
                    ("category", "hardware")
                ),
                Row(
                    "i2",
                    ("number", "INC002"),
                    ("short_description", "VPN slow"),
                    ("priority", "3"),
                    ("assignment_group", "g2"),
                    ("category", "")
                ),
                Row(
                    "i3",
                    ("number", "INC003"),
                    ("short_description", "Mail bounce"),
                    ("priority", "2"),
                    ("assignment_group", ""),
                    ("category", "software")
                )
            )
            .AddTable(
                "sys_user_group",
                null,
                [new("sys_id", "GUID"), new("name")],
                Row("g1", ("name", "Service Desk")),
                Row("g2", ("name", "Network"))
            );

    public static Dictionary<string, string> Row(string sysId, params (string Field, string Value)[] values)
    {
        var row = new Dictionary<string, string>(StringComparer.Ordinal) { ["sys_id"] = sysId };
        foreach (var (field, value) in values)
            row[field] = value;
        return row;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        Requests.Enqueue(request.RequestUri!);
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, cancellationToken);

        if (request.RequestUri!.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("ServiceNow was called over plain HTTP.");

        var expectedAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}"));
        if (request.Headers.Authorization?.Scheme != "Basic" || request.Headers.Authorization.Parameter != expectedAuth)
            return Error(HttpStatusCode.Unauthorized, "User Not Authenticated");

        if (ScriptedFailures.TryDequeue(out var failure))
        {
            var response = Error(failure.Status, "Scripted failure");
            if (failure.RetryAfter is { } retryAfter)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter);
            return response;
        }

        var match = TablePathRegex().Match(request.RequestUri.AbsolutePath);
        if (!match.Success)
            return Error(HttpStatusCode.NotFound, "Unknown API");
        var tableName = match.Groups[1].Value;
        if (ForbiddenTables.Contains(tableName))
            return Error(HttpStatusCode.Forbidden, "Operation Failed");

        var rows = RowsOf(tableName);
        if (rows is null)
            return Error(HttpStatusCode.BadRequest, "Invalid table " + tableName);

        var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
        IEnumerable<Dictionary<string, string>> result = rows;
        foreach (var term in (query["sysparm_query"] ?? "").Split('^', StringSplitOptions.RemoveEmptyEntries))
        {
            if (term.StartsWith("ORDERBY", StringComparison.Ordinal))
            {
                var field = term["ORDERBY".Length..];
                result = result.OrderBy(r => r.GetValueOrDefault(field) ?? "", StringComparer.Ordinal);
                continue;
            }
            var predicate = Parse(term);
            if (predicate is null)
                return Error(HttpStatusCode.BadRequest, "Invalid query term " + term);
            result = result.Where(predicate);
        }

        var offset = int.Parse(query["sysparm_offset"] ?? "0", CultureInfo.InvariantCulture);
        var limit = int.Parse(query["sysparm_limit"] ?? "10000", CultureInfo.InvariantCulture);
        var fields = query["sysparm_fields"]?.Split(',');
        var page = result
            .Skip(offset)
            .Take(limit)
            .Select(r =>
                fields is null
                    ? r
                    : fields.ToDictionary(f => f, f => r.GetValueOrDefault(f) ?? "", StringComparer.Ordinal)
            )
            .ToList();

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { result = page }),
                Encoding.UTF8,
                "application/json"
            ),
        };
    }

    private List<Dictionary<string, string>>? RowsOf(string table) =>
        table switch
        {
            "sys_db_object" => _tables
                .Values.Select(t =>
                    Row(
                        "obj_" + t.Name,
                        ("name", t.Name),
                        ("label", t.Name),
                        ("super_class", t.Parent is null ? "" : "obj_" + t.Parent)
                    )
                )
                .ToList(),
            "sys_dictionary" => _tables
                .Values.SelectMany(t =>
                    t.Fields.Select(f =>
                            Row(
                                $"dict_{t.Name}_{f.Name}",
                                ("name", t.Name),
                                ("element", f.Name),
                                ("internal_type", f.Type),
                                ("reference", f.Reference ?? ""),
                                ("mandatory", f.Mandatory ? "true" : "false")
                            )
                        )
                        .Prepend(
                            Row($"dict_{t.Name}", ("name", t.Name), ("element", ""), ("internal_type", "collection"))
                        )
                )
                .ToList(),
            _ => _tables.GetValueOrDefault(table)?.Rows,
        };

    private static Func<Dictionary<string, string>, bool>? Parse(string term)
    {
        var m = TermRegex().Match(term);
        if (!m.Success)
            return null;
        var (field, op, value) = (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value);
        string V(Dictionary<string, string> r) => r.GetValueOrDefault(field) ?? "";
        int Compare(Dictionary<string, string> r) =>
            decimal.TryParse(V(r), CultureInfo.InvariantCulture, out var a)
            && decimal.TryParse(value, CultureInfo.InvariantCulture, out var b)
                ? a.CompareTo(b)
                : string.CompareOrdinal(V(r), value);
        return op switch
        {
            "=" => r => V(r) == value,
            "!=" => r => V(r) != value,
            ">" => r => V(r) != "" && Compare(r) > 0,
            ">=" => r => V(r) != "" && Compare(r) >= 0,
            "<" => r => V(r) != "" && Compare(r) < 0,
            "<=" => r => V(r) != "" && Compare(r) <= 0,
            "LIKE" => r => V(r).Contains(value, StringComparison.OrdinalIgnoreCase),
            "STARTSWITH" => r => V(r).StartsWith(value, StringComparison.OrdinalIgnoreCase),
            "ENDSWITH" => r => V(r).EndsWith(value, StringComparison.OrdinalIgnoreCase),
            "IN" => r => value.Split(',').Contains(V(r)),
            "ISEMPTY" => r => V(r) == "",
            "ISNOTEMPTY" => r => V(r) != "",
            _ => null,
        };
    }

    private static HttpResponseMessage Error(HttpStatusCode status, string message) =>
        new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { error = new { message, detail = "" }, status = "failure" }),
                Encoding.UTF8,
                "application/json"
            ),
        };

    [GeneratedRegex("^/api/now/table/([A-Za-z0-9_]+)$")]
    private static partial Regex TablePathRegex();

    [GeneratedRegex("^([a-z0-9_]+?)(ISNOTEMPTY|ISEMPTY|STARTSWITH|ENDSWITH|LIKE|IN|>=|<=|!=|=|>|<)(.*)$")]
    private static partial Regex TermRegex();
}
