using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Connector.Core.DataSources;

namespace Connector.Infrastructure.DataSources.ServiceNow;

/// <summary>Tuning knobs for <see cref="ServiceNowClient"/>; the defaults are what production uses, tests
/// shrink the delays.</summary>
public sealed record ServiceNowClientOptions
{
    /// <summary>Per HTTP request, including reading the response.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Retries after the first attempt, only for HTTP 429/502/503.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>First backoff delay when the response has no usable <c>Retry-After</c>; doubles per retry.</summary>
    public TimeSpan RetryBaseDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Upper bound for any single wait, including a server-sent <c>Retry-After</c>.</summary>
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Records per Table API page (<c>sysparm_limit</c>).</summary>
    public int PageSize { get; init; } = 1000;
}

/// <summary>
/// The only code that talks HTTP to ServiceNow: authenticated, paginated <c>GET /api/now/table/{table}</c> reads
/// over HTTPS. Everything above it (<see cref="ServiceNowSchemaReader"/>, <see cref="ServiceNowTableApiProvider"/>)
/// sees records as string dictionaries. Retries only transient failures (429/502/503) a bounded number of times;
/// maps every other failure to a <see cref="DataSourceQueryException"/> whose message never contains the
/// credentials (they only ever travel in the <c>Authorization</c> header).
/// </summary>
public sealed class ServiceNowClient
{
    private readonly HttpClient _http;
    private readonly ServiceNowClientOptions _options;

    public ServiceNowClient(HttpClient http, ServiceNowClientOptions? options = null)
    {
        _http = http;
        _options = options ?? new ServiceNowClientOptions();
    }

    public int PageSize => _options.PageSize;

    /// <summary>Validates <paramref name="instanceUrl"/> as an absolute HTTPS URL (no plain HTTP, ever) and
    /// returns its base (scheme + authority).</summary>
    public static Uri ParseInstanceUrl(string? instanceUrl)
    {
        if (
            !Uri.TryCreate(instanceUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host)
        )
            throw new ArgumentException("ServiceNow instance URL must be an absolute https:// URL.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    /// <summary>Reads every record of <paramref name="table"/> matching <paramref name="encodedQuery"/>, page by
    /// page, until a short page or <paramref name="maxRecords"/> is reached.</summary>
    public async Task<List<Dictionary<string, string?>>> GetRecordsAsync(
        DataSourceConfig config,
        string table,
        string? encodedQuery,
        IReadOnlyCollection<string>? fields,
        int? maxRecords,
        CancellationToken ct
    )
    {
        var records = new List<Dictionary<string, string?>>();
        while (maxRecords is null || records.Count < maxRecords)
        {
            var pageSize = maxRecords is null ? PageSize : Math.Min(PageSize, maxRecords.Value - records.Count);
            var page = await GetPageAsync(config, table, encodedQuery, fields, pageSize, records.Count, ct);
            records.AddRange(page);
            if (page.Count < pageSize)
                break;
        }
        return records;
    }

    private async Task<List<Dictionary<string, string?>>> GetPageAsync(
        DataSourceConfig config,
        string table,
        string? encodedQuery,
        IReadOnlyCollection<string>? fields,
        int limit,
        int offset,
        CancellationToken ct
    )
    {
        var query = new StringBuilder("sysparm_exclude_reference_link=true&sysparm_display_value=false")
            .Append("&sysparm_no_count=true")
            .Append("&sysparm_limit=")
            .Append(limit.ToString(CultureInfo.InvariantCulture))
            .Append("&sysparm_offset=")
            .Append(offset.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(encodedQuery))
            query.Append("&sysparm_query=").Append(Uri.EscapeDataString(encodedQuery));
        if (fields is { Count: > 0 })
            query.Append("&sysparm_fields=").Append(Uri.EscapeDataString(string.Join(",", fields)));

        var url = new Uri(
            ParseInstanceUrl(config.InstanceUrl),
            $"/api/now/table/{Uri.EscapeDataString(table)}?{query}"
        );
        using var response = await SendWithRetryAsync(config, url, ct);
        return await ReadRecordsAsync(response, ct);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(DataSourceConfig config, Uri url, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            var response = await SendOnceAsync(config, url, ct);
            if (response.IsSuccessStatusCode)
                return response;

            if (!IsTransient(response.StatusCode) || attempt >= _options.MaxRetries)
            {
                var failure = await ToExceptionAsync(response, ct);
                response.Dispose();
                throw failure;
            }

            var delay = RetryDelay(response, attempt++);
            response.Dispose();
            await Task.Delay(delay, ct);
        }
    }

    private async Task<HttpResponseMessage> SendOnceAsync(DataSourceConfig config, Uri url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}"))
        );

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token);
            return response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"ServiceNow did not respond within {_options.RequestTimeout.TotalSeconds:0} s."
            );
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable;

    private TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay =
            retryAfter?.Delta
            ?? retryAfter?.Date - DateTimeOffset.UtcNow
            ?? _options.RetryBaseDelay * Math.Pow(2, attempt);
        return TimeSpan.FromTicks(Math.Clamp(delay.Ticks, 0, _options.MaxRetryDelay.Ticks));
    }

    // ServiceNow reports failures as {"error":{"message":"…","detail":"…"}}; the message is safe to surface (it
    // never echoes the Authorization header). The HTTP status is the error code callers can inspect.
    private static async Task<DataSourceQueryException> ToExceptionAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        var status = (int)response.StatusCode;
        var detail = await ReadErrorMessageAsync(response, ct);
        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "ServiceNow rejected the credentials (HTTP 401).",
            HttpStatusCode.Forbidden =>
                "ServiceNow denied access (HTTP 403): the account lacks read access (ACL/role) to this table.",
            HttpStatusCode.TooManyRequests => "ServiceNow rate limit still exceeded after retrying (HTTP 429).",
            _ => $"ServiceNow request failed (HTTP {status})",
        };
        if (!string.IsNullOrWhiteSpace(detail))
            message += $" {detail}";
        return new DataSourceQueryException(status.ToString(CultureInfo.InvariantCulture), message, null);
    }

    private static async Task<string?> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return
                doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<List<Dictionary<string, string?>>> ReadRecordsAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var records = new List<Dictionary<string, string?>>();
        foreach (var item in doc.RootElement.GetProperty("result").EnumerateArray())
        {
            var record = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
                record[property.Name] = ValueOf(property.Value);
            records.Add(record);
        }
        return records;
    }

    // The Table API returns every field as a string ("" for an empty field — ServiceNow has no separate NULL),
    // or, for a reference field when links aren't excluded, as {"value": …, "link": …}.
    private static string? ValueOf(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Object when value.TryGetProperty("value", out var inner) => ValueOf(inner),
            JsonValueKind.String => value.GetString() is { Length: > 0 } s ? s : null,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
}
