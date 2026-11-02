using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Startup;

/// <summary>
/// Proves the API starts and returns W3C trace IDs while an OTLP endpoint is unreachable. Delivery
/// to real backends is covered by the telemetry delivery tests.
/// </summary>
public sealed class TelemetryTraceTests
{
    private static readonly IReadOnlyDictionary<string, string?> TelemetryEnabledSettings =
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Scheduler:Telemetry:Enabled"] = "true",
            ["Scheduler:Telemetry:OtlpEndpoint"] = "http://127.0.0.1:1",
            ["Scheduler:Telemetry:SamplingRatio"] = "1.0",
            ["Scheduler:Telemetry:OtlpTimeoutMilliseconds"] = "250",
            ["Scheduler:Telemetry:TraceBatchDelayMilliseconds"] = "100",
            ["Scheduler:Telemetry:TraceExportTimeoutMilliseconds"] = "500",
            ["Scheduler:Telemetry:MetricExportIntervalMilliseconds"] = "1000",
            ["Scheduler:Telemetry:MetricExportTimeoutMilliseconds"] = "500",
        };

    [Fact]
    public async Task NotFoundProblemCarriesThirtyTwoHexTraceIdWithTelemetryEnabled()
    {
        using var factory = new SchedulerApiFactory(TelemetryEnabledSettings);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/unknown-route",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var traceId = await ReadTraceIdAsync(response);
        AssertThirtyTwoLowercaseHex(traceId);
    }

    [Fact]
    public async Task ValidationProblemCarriesThirtyTwoHexTraceIdWithTelemetryEnabled()
    {
        using var factory = new SchedulerApiFactory(TelemetryEnabledSettings);
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(
            AppointmentTestUrls.Collection(Guid.NewGuid()),
            new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertThirtyTwoLowercaseHex(await ReadTraceIdAsync(response));
    }

    [Fact]
    public async Task DependencyProblemCarriesThirtyTwoHexTraceIdWithUnreachableExporter()
    {
        using var factory = new SchedulerApiFactory(TelemetryEnabledSettings);
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(Guid.NewGuid()),
            AppointmentRequests.ForSaigon("2026-11-03T03:00:00Z"),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("dependency_unavailable", await ReadCodeAsync(response));
        AssertThirtyTwoLowercaseHex(await ReadTraceIdAsync(response));
    }

    private static async Task<string> ReadTraceIdAsync(HttpResponseMessage response)
    {
        var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var traceId = document.RootElement.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(traceId));
        Assert.DoesNotContain("StackTrace", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        return traceId!;
    }

    private static async Task<string> ReadCodeAsync(HttpResponseMessage response)
    {
        var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("code").GetString()!;
    }

    private static void AssertThirtyTwoLowercaseHex(string traceId) =>
        Assert.Matches("^[0-9a-f]{32}$", traceId);
}
