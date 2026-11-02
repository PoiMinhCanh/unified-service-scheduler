using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Features.ServiceCatalog;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Telemetry;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class TelemetryDeliveryTests : CacheSqlServerTestBase, IClassFixture<TelemetryStackFixture>
{
    private static readonly Guid DealershipId = SeedData.DealershipIds.Saigon;
    private static readonly Guid ServiceTypeId = SeedData.ServiceTypeIds.SaigonOilChange;

    private readonly TelemetryStackFixture _telemetry;
    private readonly List<Guid> _createdAppointmentIds = [];

    public TelemetryDeliveryTests(
        SqlServerContainerFixture fixture,
        RedisContainerFixture redisFixture,
        TelemetryStackFixture telemetry)
        : base(fixture, redisFixture)
    {
        _telemetry = telemetry;
    }

    public override async ValueTask DisposeAsync()
    {
        foreach (var appointmentId in _createdAppointmentIds)
        {
            await TestData.DeleteAppointmentAsync(appointmentId, CancellationToken.None)
                .ConfigureAwait(false);
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task RequestsReachPrometheusAndTempoWithSpanCoverageAndCorrelation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var traceId = NewTraceId();
        using var factory = CreateTelemetryFactory();
        using var client = factory.CreateClient();
        using var queries = CreateQueries();

        var catalogUrl = CatalogUrl();
        using (var catalog = await SendGetAsync(client, catalogUrl, traceId, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        }

        using (var catalogHit = await SendGetAsync(client, catalogUrl, traceId, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, catalogHit.StatusCode);
        }

        using (var availability = await client.SendAsync(
                   CreateRequest(
                       HttpMethod.Get,
                       AvailabilityUrl("2026-11-10T09:00:00+07:00"),
                       traceId),
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        }

        using (var created = await PostBookingAsync(client, "2026-11-10T02:00:00Z", traceId, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var conflictTraceId = NewTraceId();
        using (var conflict = await PostBookingAsync(
                   client,
                   "2026-11-10T02:00:00Z",
                   conflictTraceId,
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            var body = JsonDocument.Parse(
                await conflict.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(
                conflictTraceId,
                body.RootElement.GetProperty("traceId").GetString());
        }

        await queries.WaitForPrometheusAsync(
            "scheduler_http_requests_total",
            series => series.Any(item =>
                item.Labels.GetValueOrDefault("route") == "api/dealerships/{dealershipId}/appointments"
                && item.Labels.GetValueOrDefault("status_class") == "2xx"),
            "booking request success series",
            cancellationToken);
        await queries.WaitForPrometheusAsync(
            "scheduler_booking_outcomes_total",
            series => series.Any(item => item.Labels.GetValueOrDefault("outcome") == "created")
                      && series.Any(item => item.Labels.GetValueOrDefault("outcome") == "capacity_unavailable"),
            "created and capacity booking outcomes",
            cancellationToken);
        await queries.WaitForPrometheusAsync(
            "scheduler_booking_procedure_attempts_total",
            series => series.Any(item => item.Labels.GetValueOrDefault("result") == "succeeded"),
            "procedure attempt result",
            cancellationToken);
        await queries.WaitForPrometheusAsync(
            "scheduler_cache_operations_total",
            series => series.Any(item => item.Labels.GetValueOrDefault("result") == "miss")
                      && series.Any(item => item.Labels.GetValueOrDefault("result") == "hit"),
            "cache miss and hit operations",
            cancellationToken);

        var trace = await queries.WaitForTempoTraceAsync(
            traceId,
            candidate => candidate.Spans.Any(span => span.Name == "booking.allocate")
                         && candidate.Spans.Any(span =>
                             span.Attributes.GetValueOrDefault("db.stored_procedure.name")
                             == "dbo.CreateAppointment"),
            "booking allocation and stored-procedure spans",
            cancellationToken);
        Assert.Contains(trace.Spans, span => span.Name == "availability.evaluate");
        Assert.Contains(trace.Spans, span => span.Name == "booking.allocate");
        Assert.Contains(
            trace.Spans,
            span => span.Name == "cache.get"
                    && span.Attributes.GetValueOrDefault("cache.result") == "hit");
        Assert.Contains(
            trace.Spans,
            span => span.Name == "cache.set"
                    && span.Attributes.GetValueOrDefault("cache.result") == "success");
        Assert.Contains(trace.Spans, span => span.Name.StartsWith("SELECT ", StringComparison.Ordinal));
        Assert.Contains(
            trace.Spans,
            span => span.Attributes.GetValueOrDefault("db.stored_procedure.name") == "dbo.CreateAppointment");
        Assert.Contains(
            trace.Spans,
            span => span.Attributes.GetValueOrDefault("http.route")
                    == "api/dealerships/{dealershipId}/appointments");

        var conflictTrace = await queries.WaitForTempoTraceAsync(
            conflictTraceId,
            candidate => candidate.Spans.Any(span =>
                span.Attributes.GetValueOrDefault("http.route")
                == "api/dealerships/{dealershipId}/appointments"),
            "booking conflict HTTP span",
            cancellationToken);
        // The overlap precheck removes the only candidate pair before the allocator is called, so
        // a capacity conflict has HTTP/SQL spans but no booking.allocate span.
        Assert.Contains(conflictTrace.Spans, span => span.Name.StartsWith("SELECT ", StringComparison.Ordinal));
        Assert.Contains(
            conflictTrace.Spans,
            span => span.Attributes.GetValueOrDefault("http.route")
                    == "api/dealerships/{dealershipId}/appointments");

        var grafanaTrace = await queries.WaitForTempoTraceThroughGrafanaAsync(traceId, cancellationToken);
        Assert.Contains(grafanaTrace.Spans, span => span.Name == "booking.allocate");
    }

    [Fact]
    public async Task MetricContractDistinguishesOutcomesAndUsesBoundedLabels()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateTelemetryFactory();
        using var client = factory.CreateClient();
        using var queries = CreateQueries();

        await Cache.RemoveAsync(
            ServiceCatalogCacheKeys.ForDealership(DealershipId),
            cancellationToken);
        using (var catalogMiss = await client.GetAsync(CatalogUrl(), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, catalogMiss.StatusCode);
        }

        using (var catalogHit = await client.GetAsync(CatalogUrl(), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, catalogHit.StatusCode);
        }

        using (var invalid = await client.PostAsJsonAsync(
                   AppointmentTestUrls.Collection(DealershipId),
                   AppointmentRequests.ForSaigon("not-a-timestamp"),
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using (var created = await PostBookingAsync(client, "2026-11-11T02:00:00Z", NewTraceId(), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using (var conflict = await PostBookingAsync(client, "2026-11-11T02:00:00Z", NewTraceId(), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }

        var outcomes = await queries.WaitForPrometheusAsync(
            "scheduler_booking_outcomes_total",
            series => series.Any(item => item.Labels.GetValueOrDefault("outcome") == "created")
                      && series.Any(item => item.Labels.GetValueOrDefault("outcome") == "capacity_unavailable")
                      && series.Any(item => item.Labels.GetValueOrDefault("outcome") == "validation_error"),
            "created, capacity, and validation booking outcomes",
            cancellationToken);

        var cache = await queries.WaitForPrometheusAsync(
            "scheduler_cache_operations_total",
            series => series.Any(item => item.Labels.GetValueOrDefault("operation") == "get"
                                          && item.Labels.GetValueOrDefault("result") == "miss"),
            "cache miss series",
            cancellationToken);

        Assert.All(outcomes, series => AssertAllowedLabels(series.Labels));
        Assert.All(cache, series => AssertAllowedLabels(series.Labels));
        Assert.All(
            await queries.QueryPrometheusAsync("scheduler_http_requests_total", cancellationToken),
            series => AssertAllowedLabels(series.Labels));
        Assert.All(
            await queries.QueryPrometheusAsync("scheduler_booking_procedure_attempts_total", cancellationToken),
            series => AssertAllowedLabels(series.Labels));

        using var dependencyFactory = CreateTelemetryFactory(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Runtime"] = SchedulerApiFactory.UnreachableRuntimeConnection,
            });
        using var dependencyClient = dependencyFactory.CreateClient();
        using (var dependency = await PostBookingAsync(
                   dependencyClient,
                   "2026-11-13T02:00:00Z",
                   NewTraceId(),
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, dependency.StatusCode);
            var body = JsonDocument.Parse(await dependency.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal("dependency_unavailable", body.RootElement.GetProperty("code").GetString());
        }

        // An unreachable SQL runtime fails inside the service prechecks, so the escaped
        // dependency failure is represented by the request outcome (the controller never returns
        // an appointment result); the booking counter covers controller-returned outcomes.
        await queries.WaitForPrometheusAsync(
            "scheduler_http_requests_total",
            series => series.Any(item => item.Labels.GetValueOrDefault("outcome") == "dependency_unavailable"),
            "dependency request outcome",
            cancellationToken);
    }

    [Fact]
    public async Task SensitiveValuesAreExcludedFromLogsSpansAndMetricLabels()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var loggerProvider = new CapturingLoggerProvider();
        using var factory = CreateTelemetryFactory(
            configureServices: services => services.AddSingleton<ILoggerProvider>(loggerProvider));
        using var client = factory.CreateClient();
        using var queries = CreateQueries();
        var traceId = NewTraceId();

        using (var catalog = await SendGetAsync(
                   client,
                   $"{CatalogUrl()}?vin=SYNTHETICVIN123",
                   traceId,
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        }

        using (var created = await PostBookingAsync(client, "2026-11-12T02:00:00Z", traceId, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        var completion = await WaitForCompletionAsync(
            loggerProvider,
            "api/dealerships/{dealershipId}/service-types",
            cancellationToken);
        AssertNoSensitiveValue(completion.Message);

        var trace = await queries.WaitForTempoTraceAsync(
            traceId,
            candidate => candidate.Spans.Any(span => span.Name == "booking.allocate")
                         && candidate.Spans.Any(span => span.Attributes.ContainsKey("http.route")),
            "booking allocation and HTTP spans",
            cancellationToken);
        foreach (var span in trace.Spans)
        {
            Assert.DoesNotContain("url.path", span.Attributes.Keys);
            Assert.DoesNotContain("url.query", span.Attributes.Keys);
            Assert.DoesNotContain("user_agent.original", span.Attributes.Keys);
            foreach (var value in span.Attributes.Values)
            {
                AssertNoSensitiveValue(value);
            }
        }

        foreach (var metric in new[]
                 {
                     "scheduler_http_requests_total",
                     "scheduler_booking_outcomes_total",
                     "scheduler_booking_procedure_attempts_total",
                     "scheduler_cache_operations_total",
                     "scheduler_booking_allocation_duration_seconds_count",
                 })
        {
            foreach (var series in await queries.QueryPrometheusAsync(metric, cancellationToken))
            {
                AssertAllowedLabels(series.Labels);
                foreach (var value in series.Labels.Values)
                {
                    AssertNoSensitiveValue(value);
                }
            }
        }
    }

    [Fact]
    public async Task TelemetryOutageDoesNotGateApiAndNewExportsResumeAfterRecovery()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _telemetry.StopCollectorAsync(cancellationToken);
        try
        {
            using var factory = CreateTelemetryFactory();
            using var client = factory.CreateClient();
            using var queries = CreateQueries();

            using (var catalog = await client.GetAsync(CatalogUrl(), cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
            }

            using (var readiness = await client.GetAsync("/health/ready", cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
            }

            using var created = await PostBookingAsync(
                client,
                "2026-11-13T02:00:00Z",
                NewTraceId(),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var appointmentId = (await ReadJsonAsync(created, cancellationToken))
                .RootElement.GetProperty("id").GetString();

            using (var retrieved = await client.GetAsync(
                       AppointmentTestUrls.For(DealershipId, Guid.Parse(appointmentId!)),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, retrieved.StatusCode);
            }

            await _telemetry.StartCollectorAsync(cancellationToken);

            var recoveryTraceId = NewTraceId();
            using (var recovered = await SendGetAsync(client, CatalogUrl(), recoveryTraceId, cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            }

            await queries.WaitForTempoTraceAsync(recoveryTraceId, cancellationToken);
        }
        finally
        {
            await _telemetry.StartCollectorAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RedisDegradationProducesSignalsWhileFallbackAndBookingRemainCorrect()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var loggerProvider = new CapturingLoggerProvider();
        using var factory = CreateTelemetryFactory(
            configureServices: services => services.AddSingleton<ILoggerProvider>(loggerProvider));
        using var client = factory.CreateClient();
        using var queries = CreateQueries();

        await PauseCacheAsync();
        try
        {
            using (var catalog = await client.GetAsync(CatalogUrl(), cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
            }

            using var created = await PostBookingAsync(
                client,
                "2026-11-17T02:00:00Z",
                NewTraceId(),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var appointmentId = (await ReadJsonAsync(created, cancellationToken))
                .RootElement.GetProperty("id").GetString();

            using (var retrieved = await client.GetAsync(
                       AppointmentTestUrls.For(DealershipId, Guid.Parse(appointmentId!)),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, retrieved.StatusCode);
            }

            using (var readiness = await client.GetAsync("/health/ready", cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, readiness.StatusCode);
                Assert.Contains(
                    "Degraded",
                    await readiness.Content.ReadAsStringAsync(cancellationToken),
                    StringComparison.OrdinalIgnoreCase);
            }

            Assert.Contains(
                loggerProvider.Entries,
                entry => entry.Properties.GetValueOrDefault("EventName") as string == "CacheOperation"
                         && entry.Message.Contains("Redis cache", StringComparison.OrdinalIgnoreCase));

            await queries.WaitForPrometheusAsync(
                "scheduler_cache_operations_total",
                series => series.Any(item => item.Labels.GetValueOrDefault("operation") == "get"
                                              && item.Labels.GetValueOrDefault("result") == "failure"),
                "cache failure series during Redis outage",
                cancellationToken);
        }
        finally
        {
            await UnpauseCacheAsync();
        }

        await WaitForReadinessAsync(client, "Healthy", cancellationToken);
    }

    [Fact]
    public async Task UnreachableSqlIsClassifiedAsDependencyNotCapacityAndStoresNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await TestData.CountAppointmentsAsync(cancellationToken);
        using var factory = CreateTelemetryFactory(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Runtime"] = SchedulerApiFactory.UnreachableRuntimeConnection,
            });
        using var client = factory.CreateClient();
        using var queries = CreateQueries();

        using (var catalog = await client.GetAsync(CatalogUrl(), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, catalog.StatusCode);
            Assert.Equal(
                "dependency_unavailable",
                (await ReadJsonAsync(catalog, cancellationToken))
                .RootElement.GetProperty("code").GetString());
        }

        using (var availability = await client.GetAsync(
                   AvailabilityUrl("2026-11-16T09:00:00+07:00"),
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, availability.StatusCode);
        }

        using (var booking = await PostBookingAsync(
                   client,
                   "2026-11-16T02:00:00Z",
                   NewTraceId(),
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, booking.StatusCode);
            Assert.Equal(
                "dependency_unavailable",
                (await ReadJsonAsync(booking, cancellationToken))
                .RootElement.GetProperty("code").GetString());
        }

        using (var readiness = await client.GetAsync("/health/ready", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);
        }

        Assert.Equal(before, await TestData.CountAppointmentsAsync(cancellationToken));
        // The SQL connection fails before the controller can record a booking outcome; the
        // escaped dependency failure is represented by the request outcome metric.
        await queries.WaitForPrometheusAsync(
            "scheduler_http_requests_total",
            series => series.Any(item => item.Labels.GetValueOrDefault("outcome") == "dependency_unavailable"),
            "dependency request outcome for unreachable SQL",
            cancellationToken);
    }


    [Fact]
    public async Task HealthTrafficIsExcludedFromRequestMetricsAndServerSpans()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateTelemetryFactory();
        using var client = factory.CreateClient();
        using var queries = CreateQueries();

        using (var live = await client.GetAsync("/health/live", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        }

        using (var ready = await client.GetAsync("/health/ready", cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }

        // Positive control: wait for non-health request and server-duration series so the
        // Collector/Prometheus pipeline has demonstrably delivered data from this host before
        // asserting that the earlier health datapoints are absent.
        using (var control = await SendGetAsync(client, CatalogUrl(), NewTraceId(), cancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
        }

        await queries.WaitForPrometheusAsync(
            "scheduler_http_requests_total",
            series => series.Any(item =>
                item.Labels.GetValueOrDefault("route") == "api/dealerships/{dealershipId}/service-types"),
            "positive-control request metric",
            cancellationToken);
        await queries.WaitForPrometheusAsync(
            "http_server_request_duration_seconds_count",
            series => series.Any(item =>
                item.Labels.GetValueOrDefault("http_route") == "api/dealerships/{dealershipId}/service-types"),
            "positive-control server duration metric",
            cancellationToken);

        Assert.Empty(await queries.QueryPrometheusAsync(
            "scheduler_http_requests_total{route=~\"/health/.*\"}",
            cancellationToken));
        Assert.Empty(await queries.QueryPrometheusAsync(
            "http_server_request_duration_seconds_count{http_route=~\"/health/.*\"}",
            cancellationToken));
    }

    private SchedulerApiFactory CreateTelemetryFactory(
        IReadOnlyDictionary<string, string?>? overrides = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Scheduler:Telemetry:Enabled"] = "true",
            ["Scheduler:Telemetry:OtlpEndpoint"] = _telemetry.CollectorGrpcEndpoint,
            ["Scheduler:Telemetry:SamplingRatio"] = "1.0",
            ["Scheduler:Telemetry:TraceBatchDelayMilliseconds"] = "100",
            ["Scheduler:Telemetry:TraceExportTimeoutMilliseconds"] = "1000",
            ["Scheduler:Telemetry:TraceBatchQueueSize"] = "512",
            ["Scheduler:Telemetry:TraceBatchSize"] = "128",
            ["Scheduler:Telemetry:OtlpTimeoutMilliseconds"] = "500",
            ["Scheduler:Telemetry:MetricExportIntervalMilliseconds"] = "500",
            ["Scheduler:Telemetry:MetricExportTimeoutMilliseconds"] = "500",
        };

        if (overrides is not null)
        {
            foreach (var setting in overrides)
            {
                settings[setting.Key] = setting.Value;
            }
        }

        return CreateFactory(
            clock: new FakeTimeProvider(SqlServerContainerFixture.FixedClockUtc),
            configureServices: configureServices,
            overrides: settings);
    }

    private TelemetryTestQueries CreateQueries() =>
        new(_telemetry.PrometheusBaseUrl, _telemetry.TempoBaseUrl, _telemetry.GrafanaBaseUrl);

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, string traceId)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("traceparent", TraceParent(traceId));
        return request;
    }

    private static Task<HttpResponseMessage> SendGetAsync(
        HttpClient client,
        string url,
        string traceId,
        CancellationToken cancellationToken) =>
        client.SendAsync(CreateRequest(HttpMethod.Get, url, traceId), cancellationToken);

    private async Task<HttpResponseMessage> PostBookingAsync(
        HttpClient client,
        string startsAt,
        string traceId,
        CancellationToken cancellationToken)
    {
        var request = CreateRequest(
            HttpMethod.Post,
            AppointmentTestUrls.Collection(DealershipId),
            traceId);
        request.Content = JsonContent.Create(AppointmentRequests.ForSaigon(startsAt));
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("id", out var id)
                && id.TryGetGuid(out var appointmentId))
            {
                _createdAppointmentIds.Add(appointmentId);
            }
        }

        return response;
    }

    private static string TraceParent(string traceId) =>
        $"00-{traceId}-{Guid.NewGuid().ToString("N")[..16]}-01";

    private static string NewTraceId() => Guid.NewGuid().ToString("N");

    private static string CatalogUrl() =>
        $"/api/dealerships/{DealershipId}/service-types";

    private static string AvailabilityUrl(string startsAt) =>
        $"/api/dealerships/{DealershipId}/availability?serviceTypeId={ServiceTypeId}" +
        $"&startsAt={Uri.EscapeDataString(startsAt)}";

    private static async Task<JsonDocument> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

    private static async Task<LogEntry> WaitForCompletionAsync(
        CapturingLoggerProvider provider,
        string routeTemplate,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(10))
        {
            var entry = provider.Entries.FirstOrDefault(candidate =>
                candidate.Category.Contains("RequestLoggingMiddleware", StringComparison.Ordinal)
                && Equals(candidate.Properties.GetValueOrDefault("RouteTemplate"), routeTemplate));
            if (entry is not null)
            {
                return entry;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new InvalidOperationException(
            $"No RequestCompleted event with route template '{routeTemplate}' was logged within 10 seconds.");
    }

    private static async Task WaitForReadinessAsync(
        HttpClient client,
        string expectedStatus,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        while (started.Elapsed < TimeSpan.FromSeconds(10))
        {
            using var response = await client.GetAsync("/health/ready", cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (body.Contains(expectedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Readiness did not report '{expectedStatus}' within 10 seconds.");
    }

    private static void AssertAllowedLabels(IReadOnlyDictionary<string, string> labels)
    {
        string[] forbidden =
        [
            "appointment_id", "customer_id", "vehicle_id", "dealership_id",
            "service_type_id", "technician_id", "service_bay_id",
            "trace_id", "span_id", "url", "path", "query", "vin", "email",
        ];
        foreach (var key in labels.Keys)
        {
            Assert.DoesNotContain(key, forbidden, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void AssertNoSensitiveValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        string[] forbidden =
        [
            "SYNTHETICVIN123",
            SeedData.DealershipIds.Saigon.ToString("D"),
            SeedData.DealershipIds.London.ToString("D"),
            SeedData.CustomerIds.AnNguyen.ToString("D"),
            SeedData.VehicleIds.AnNguyenVehicle.ToString("D"),
            SeedData.ServiceTypeIds.SaigonOilChange.ToString("D"),
            SeedData.TechnicianIds.SaigonActive.ToString("D"),
            SeedData.ServiceBayIds.SaigonActive.ToString("D"),
        ];
        foreach (var candidate in forbidden)
        {
            Assert.DoesNotContain(candidate, value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
