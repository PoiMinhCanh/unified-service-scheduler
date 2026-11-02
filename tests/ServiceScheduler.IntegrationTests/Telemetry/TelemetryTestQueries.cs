using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace ServiceScheduler.IntegrationTests.Telemetry;

/// <summary>
/// Bounded polling for the real telemetry backend APIs. Failures include the endpoint/query,
/// elapsed time, last status/body, and the expected signal so a missing export is diagnosable.
/// </summary>
public sealed class TelemetryTestQueries : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };

    public TelemetryTestQueries(string prometheusBaseUrl, string tempoBaseUrl, string grafanaBaseUrl)
    {
        PrometheusBaseUrl = prometheusBaseUrl.TrimEnd('/');
        TempoBaseUrl = tempoBaseUrl.TrimEnd('/');
        GrafanaBaseUrl = grafanaBaseUrl.TrimEnd('/');
    }

    public string PrometheusBaseUrl { get; }

    public string TempoBaseUrl { get; }

    public string GrafanaBaseUrl { get; }

    public async Task<IReadOnlyList<PrometheusSeries>> WaitForPrometheusAsync(
        string query,
        Func<IReadOnlyList<PrometheusSeries>, bool> predicate,
        string expectation,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        string? lastBody = null;
        HttpStatusCode? lastStatus = null;
        while (started.Elapsed < DefaultTimeout)
        {
            var (status, body, series) = await QueryPrometheusCoreAsync(query, cancellationToken)
                .ConfigureAwait(false);
            lastStatus = status;
            lastBody = body;
            if (status == HttpStatusCode.OK && predicate(series))
            {
                return series;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Prometheus query did not satisfy '{expectation}' within {DefaultTimeout}. " +
            $"Query: {query}. Elapsed: {started.Elapsed}. " +
            $"Last status: {(int?)lastStatus}. Last body: {Truncate(lastBody)}");
    }

    public async Task<IReadOnlyList<PrometheusSeries>> QueryPrometheusAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var (status, _, series) = await QueryPrometheusCoreAsync(query, cancellationToken, throwOnFailure: true)
            .ConfigureAwait(false);
        return status == HttpStatusCode.OK ? series : [];
    }

    public Task<TempoTrace> WaitForTempoTraceAsync(
        string traceId,
        CancellationToken cancellationToken) =>
        WaitForTempoTraceAsync(
            traceId,
            trace => trace.Spans.Count > 0,
            "at least one span",
            cancellationToken);

    public async Task<TempoTrace> WaitForTempoTraceAsync(
        string traceId,
        Func<TempoTrace, bool> predicate,
        string expectation,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        string? lastBody = null;
        while (started.Elapsed < DefaultTimeout)
        {
            var (status, body, trace) = await GetTempoTraceAsync(traceId, cancellationToken)
                .ConfigureAwait(false);
            lastBody = body;
            if (status == HttpStatusCode.OK && trace is not null && predicate(trace))
            {
                return trace;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Tempo trace '{traceId}' did not satisfy '{expectation}' within {DefaultTimeout}. " +
            $"Endpoint: {TempoBaseUrl}/api/v2/traces/{traceId}. Elapsed: {started.Elapsed}. " +
            $"Last body: {Truncate(lastBody)}");
    }

    public async Task<TempoTrace> WaitForTempoTraceThroughGrafanaAsync(
        string traceId,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        string? lastBody = null;
        var url = $"{GrafanaBaseUrl}/api/datasources/proxy/uid/tempo/api/v2/traces/{traceId}";
        while (started.Elapsed < DefaultTimeout)
        {
            using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            lastBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var trace = ParseTempoTrace(lastBody);
                if (trace.Spans.Count > 0)
                {
                    return trace;
                }
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException(
            $"Grafana Tempo proxy did not return trace '{traceId}' within {DefaultTimeout}. " +
            $"Endpoint: {url}. Elapsed: {started.Elapsed}. Last body: {Truncate(lastBody)}");
    }

    public async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {url} returned {(int)response.StatusCode}. Body: {Truncate(body)}");
        }

        return JsonDocument.Parse(body);
    }

    public async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET {url} returned {(int)response.StatusCode}. Body: {Truncate(body)}");
        }

        return body;
    }

    public void Dispose() => _client.Dispose();

    private async Task<(HttpStatusCode Status, string Body, IReadOnlyList<PrometheusSeries> Series)>
        QueryPrometheusCoreAsync(
            string query,
            CancellationToken cancellationToken,
            bool throwOnFailure = false)
    {
        var url = $"{PrometheusBaseUrl}/api/v1/query?query={Uri.EscapeDataString(query)}";
        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (throwOnFailure)
            {
                throw new InvalidOperationException(
                    $"Prometheus query failed with {(int)response.StatusCode}. Query: {query}. Body: {Truncate(body)}");
            }

            return (response.StatusCode, body, []);
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.GetProperty("status").GetString() != "success")
        {
            if (throwOnFailure)
            {
                throw new InvalidOperationException($"Prometheus query failed. Query: {query}. Body: {Truncate(body)}");
            }

            return (response.StatusCode, body, []);
        }

        var series = new List<PrometheusSeries>();
        if (root.GetProperty("data").TryGetProperty("result", out var results))
        {
            foreach (var result in results.EnumerateArray())
            {
                var labels = new Dictionary<string, string>(StringComparer.Ordinal);
                if (result.TryGetProperty("metric", out var metric))
                {
                    foreach (var label in metric.EnumerateObject())
                    {
                        labels[label.Name] = label.Value.GetString() ?? string.Empty;
                    }
                }

                var value = result.TryGetProperty("value", out var valueArray)
                    && valueArray.ValueKind == JsonValueKind.Array
                    && valueArray.GetArrayLength() > 1
                    ? double.Parse(valueArray[1].GetString()!, System.Globalization.CultureInfo.InvariantCulture)
                    : 0;
                series.Add(new PrometheusSeries(labels, value));
            }
        }

        return (response.StatusCode, body, series);
    }

    private async Task<(HttpStatusCode Status, string Body, TempoTrace? Trace)> GetTempoTraceAsync(
        string traceId,
        CancellationToken cancellationToken)
    {
        var url = $"{TempoBaseUrl}/api/v2/traces/{traceId}";
        using var response = await _client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode
            ? (response.StatusCode, body, ParseTempoTrace(body))
            : (response.StatusCode, body, null);
    }

    private static TempoTrace ParseTempoTrace(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("trace", out var trace))
        {
            return new TempoTrace([]);
        }

        var spans = new List<TempoSpan>();
        if (!trace.TryGetProperty("resourceSpans", out var resourceSpans))
        {
            return new TempoTrace(spans);
        }

        foreach (var resourceSpan in resourceSpans.EnumerateArray())
        {
            if (!resourceSpan.TryGetProperty("scopeSpans", out var scopeSpans))
            {
                continue;
            }

            foreach (var scopeSpan in scopeSpans.EnumerateArray())
            {
                if (!scopeSpan.TryGetProperty("spans", out var scopeSpansArray))
                {
                    continue;
                }

                foreach (var span in scopeSpansArray.EnumerateArray())
                {
                    var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
                    if (span.TryGetProperty("attributes", out var attributeArray))
                    {
                        foreach (var attribute in attributeArray.EnumerateArray())
                        {
                            var value = attribute.TryGetProperty("value", out var rawValue)
                                ? ReadAttributeValue(rawValue)
                                : null;
                            attributes[attribute.GetProperty("key").GetString()!] = value;
                        }
                    }

                    spans.Add(new TempoSpan(span.GetProperty("name").GetString()!, attributes));
                }
            }
        }

        return new TempoTrace(spans);
    }

    private static string? ReadAttributeValue(JsonElement value)
    {
        foreach (var property in value.EnumerateObject())
        {
            return property.Name switch
            {
                "stringValue" => property.Value.GetString(),
                "intValue" => property.Value.GetString(),
                "doubleValue" => property.Value.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                "boolValue" => property.Value.GetBoolean().ToString(),
                _ => property.Value.ToString(),
            };
        }

        return null;
    }

    private static string Truncate(string? value) =>
        value is null ? "<empty>" : value.Length <= 600 ? value : value[..600] + "…";
}

public sealed record PrometheusSeries(
    IReadOnlyDictionary<string, string> Labels,
    double Value);

public sealed record TempoTrace(IReadOnlyList<TempoSpan> Spans);

public sealed record TempoSpan(
    string Name,
    IReadOnlyDictionary<string, string?> Attributes);
