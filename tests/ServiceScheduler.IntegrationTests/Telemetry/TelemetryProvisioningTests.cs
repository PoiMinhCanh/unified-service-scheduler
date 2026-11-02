using Xunit;

namespace ServiceScheduler.IntegrationTests.Telemetry;

/// <summary>
/// Uses a fresh stack and no application traffic to verify Grafana provisioning and the documented
/// zero-traffic behavior of the committed dashboard queries.
/// </summary>
public sealed class TelemetryProvisioningTests : IClassFixture<TelemetryStackFixture>
{
    private readonly TelemetryStackFixture _telemetry;

    public TelemetryProvisioningTests(TelemetryStackFixture telemetry)
    {
        _telemetry = telemetry;
    }

    [Fact]
    public async Task GrafanaHealthDataSourcesAndDashboardAreProvisioned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var queries = CreateQueries();

        var health = await queries.GetJsonAsync(
            $"{_telemetry.GrafanaBaseUrl}/api/health",
            cancellationToken);
        Assert.True(health.RootElement.TryGetProperty("version", out _));

        var prometheus = await queries.GetJsonAsync(
            $"{_telemetry.GrafanaBaseUrl}/api/datasources/uid/prometheus",
            cancellationToken);
        Assert.Equal("prometheus", prometheus.RootElement.GetProperty("uid").GetString());

        var tempo = await queries.GetJsonAsync(
            $"{_telemetry.GrafanaBaseUrl}/api/datasources/uid/tempo",
            cancellationToken);
        Assert.Equal("tempo", tempo.RootElement.GetProperty("uid").GetString());

        var dashboard = await queries.GetJsonAsync(
            $"{_telemetry.GrafanaBaseUrl}/api/dashboards/uid/scheduler-overview",
            cancellationToken);
        var model = dashboard.RootElement.GetProperty("dashboard");
        Assert.Equal("scheduler-overview", model.GetProperty("uid").GetString());
        Assert.Equal("Unified Service Scheduler - Operations", model.GetProperty("title").GetString());
        Assert.Contains(
            "service-scheduler",
            model.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.True(model.GetProperty("panels").GetArrayLength() >= 9);

        var expressions = model.GetProperty("panels")
            .EnumerateArray()
            .SelectMany(panel => panel.TryGetProperty("targets", out var targets)
                ? targets.EnumerateArray().Select(target => target.GetProperty("expr").GetString()!)
                : [])
            .ToArray();

        Assert.Contains(
            expressions,
            expression => expression.Contains("scheduler_http_requests_total", StringComparison.Ordinal));
        Assert.Contains(
            expressions,
            expression => expression.Contains("scheduler_booking_outcomes_total", StringComparison.Ordinal));
        Assert.Contains(
            expressions,
            expression => expression.Contains("scheduler_booking_procedure_attempts_total", StringComparison.Ordinal));
        Assert.Contains(
            expressions,
            expression => expression.Contains("scheduler_cache_operations_total", StringComparison.Ordinal));
        Assert.Contains(
            expressions,
            expression => expression.Contains("http_server_request_duration_seconds_bucket", StringComparison.Ordinal));
        Assert.Contains(
            expressions,
            expression => expression.Contains("histogram_quantile(0.95", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ZeroTrafficQueriesMatchTheDocumentedBehavior()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var queries = CreateQueries();

        Assert.Empty(await queries.QueryPrometheusAsync(
            "scheduler_http_requests_total",
            cancellationToken));
        Assert.Empty(await queries.QueryPrometheusAsync(
            "scheduler_booking_outcomes_total",
            cancellationToken));
        Assert.Empty(await queries.QueryPrometheusAsync(
            "scheduler_cache_operations_total",
            cancellationToken));
        Assert.Empty(await queries.QueryPrometheusAsync(
            "histogram_quantile(0.95, sum by (le, http_route, http_request_method) " +
            "(rate(http_server_request_duration_seconds_bucket[5m])))",
            cancellationToken));

        var errorRatio = await queries.QueryPrometheusAsync(
            "sum(rate(scheduler_http_requests_total{status_class=\"5xx\"}[5m])) / " +
            "clamp_min(sum(rate(scheduler_http_requests_total[5m])), 1e-9) or vector(0)",
            cancellationToken);
        Assert.Single(errorRatio);
        Assert.Equal(0, errorRatio[0].Value);

        var cacheRatio = await queries.QueryPrometheusAsync(
            "sum(rate(scheduler_cache_operations_total{operation=\"get\", result=\"hit\"}[5m])) / " +
            "clamp_min(sum(rate(scheduler_cache_operations_total{operation=\"get\", result=~\"hit|miss\"}[5m])), 1e-9) " +
            "or vector(0)",
            cancellationToken);
        Assert.Single(cacheRatio);
        Assert.Equal(0, cacheRatio[0].Value);
    }

    private TelemetryTestQueries CreateQueries() =>
        new(_telemetry.PrometheusBaseUrl, _telemetry.TempoBaseUrl, _telemetry.GrafanaBaseUrl);
}
