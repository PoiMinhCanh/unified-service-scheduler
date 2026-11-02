using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Registers the telemetry provider hosted service. Providers are built at host start from the
/// final configuration, so test/Compose overrides are honored, and nothing is created when
/// telemetry is disabled. Exporters are asynchronous and bounded and never gate requests.
/// </summary>
public static class TelemetryRegistration
{
    public static void AddSchedulerTelemetry(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddHostedService<TelemetryProviderHostedService>();
    }
}

/// <summary>Builds and disposes the OpenTelemetry SDK providers when telemetry is enabled.</summary>
public sealed class TelemetryProviderHostedService(
    IOptions<TelemetryOptions> options,
    IHostEnvironment environment,
    ILogger<TelemetryProviderHostedService> logger) : IHostedService, IDisposable
{
    private static readonly double[] HttpDurationBoundaries =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    private static readonly double[] AllocationDurationBoundaries =
        [0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 20];

    private TracerProvider? _tracerProvider;
    private MeterProvider? _meterProvider;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return Task.CompletedTask;
        }

        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                settings.ServiceName,
                serviceVersion: settings.ResolveServiceVersion(),
                serviceInstanceId: settings.ServiceInstanceId)
            .AddAttributes(
            [
                new KeyValuePair<string, object>("deployment.environment.name", environment.EnvironmentName),
            ]);
        var endpoint = new Uri(settings.OtlpEndpoint);

        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(settings.SamplingRatio)))
            .AddAspNetCoreInstrumentation(aspNetCore =>
            {
                aspNetCore.Filter = context => !IsHealthRequest(context.Request.Path);
                aspNetCore.RecordException = true;
                aspNetCore.EnrichWithHttpResponse = (activity, _) => SanitizeServerSpan(activity);
            })
            .AddSqlClientInstrumentation(sqlClient => sqlClient.RecordException = true)
            .AddSource(ServiceSchedulerTelemetry.SourceName)
            .AddOtlpExporter(exporter =>
            {
                ConfigureExporter(exporter, endpoint, settings);
                exporter.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                {
                    ScheduledDelayMilliseconds = settings.TraceBatchDelayMilliseconds,
                    ExporterTimeoutMilliseconds = settings.TraceExportTimeoutMilliseconds,
                    MaxQueueSize = settings.TraceBatchQueueSize,
                    MaxExportBatchSize = settings.TraceBatchSize,
                };
            })
            .Build();

        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter(ServiceSchedulerTelemetry.MeterName)
            .AddView(
                "http.server.request.duration",
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = HttpDurationBoundaries,
                    TagKeys = ["http.request.method", "http.route", "http.response.status_code"],
                })
            .AddView(
                ServiceSchedulerTelemetry.BookingAllocationDurationInstrumentName,
                new ExplicitBucketHistogramConfiguration
                {
                    Boundaries = AllocationDurationBoundaries,
                    TagKeys = ["outcome"],
                })
            .AddView(
                ServiceSchedulerTelemetry.HttpRequestsInstrumentName,
                new MetricStreamConfiguration
                {
                    TagKeys = ["route", "method", "status_class", "outcome"],
                })
            .AddView(
                ServiceSchedulerTelemetry.BookingOutcomesInstrumentName,
                new MetricStreamConfiguration { TagKeys = ["outcome"] })
            .AddView(
                ServiceSchedulerTelemetry.BookingProcedureAttemptsInstrumentName,
                new MetricStreamConfiguration { TagKeys = ["result"] })
            .AddView(
                ServiceSchedulerTelemetry.CacheOperationsInstrumentName,
                new MetricStreamConfiguration { TagKeys = ["operation", "result"] })
            .AddOtlpExporter((exporter, reader) =>
            {
                ConfigureExporter(exporter, endpoint, settings);
                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                    settings.MetricExportIntervalMilliseconds;
                reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds =
                    settings.MetricExportTimeoutMilliseconds;
            })
            .Build();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        DisposeProvider(_meterProvider, "metrics");
        DisposeProvider(_tracerProvider, "traces");
        _meterProvider = null;
        _tracerProvider = null;
    }

    private void DisposeProvider(IDisposable? provider, string signal)
    {
        if (provider is null)
        {
            return;
        }

        try
        {
            provider.Dispose();
        }
        catch (Exception exception)
        {
            // Telemetry is best-effort and must never fail application shutdown.
            logger.LogWarning(
                exception,
                "Telemetry {Signal} provider shutdown failed; application shutdown continues.",
                signal);
        }
    }

    private static void ConfigureExporter(
        OtlpExporterOptions exporter,
        Uri endpoint,
        TelemetryOptions settings)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.Grpc;
        exporter.TimeoutMilliseconds = settings.OtlpTimeoutMilliseconds;
    }

    private static void SanitizeServerSpan(Activity activity)
    {
        activity.SetTag("url.path", null);
        activity.SetTag("url.query", null);
        activity.SetTag("user_agent.original", null);
        activity.SetTag("server.address", null);
        activity.SetTag("server.port", null);
        activity.SetTag("http.request.method_original", null);
    }

    private static bool IsHealthRequest(PathString path) =>
        path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
}
