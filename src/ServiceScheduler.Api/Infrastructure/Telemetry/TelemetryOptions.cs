using System.Reflection;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Bounded OpenTelemetry export configuration. Telemetry is disabled by default so local runs and
/// tests never attempt to reach a Collector unless they explicitly enable it.
/// </summary>
public sealed class TelemetryOptions
{
    public const string SectionName = "Scheduler:Telemetry";

    public bool Enabled { get; set; }

    public string OtlpEndpoint { get; set; } = "http://127.0.0.1:4317";

    public double SamplingRatio { get; set; } = 1.0;

    public string ServiceName { get; set; } = "unified-service-scheduler-api";

    public string? ServiceVersion { get; set; }

    public string ServiceInstanceId { get; set; } = "scheduler-api";

    public int TraceBatchDelayMilliseconds { get; set; } = 1000;

    public int TraceExportTimeoutMilliseconds { get; set; } = 5000;

    public int TraceBatchQueueSize { get; set; } = 2048;

    public int TraceBatchSize { get; set; } = 512;

    public int OtlpTimeoutMilliseconds { get; set; } = 3000;

    public int MetricExportIntervalMilliseconds { get; set; } = 5000;

    public int MetricExportTimeoutMilliseconds { get; set; } = 3000;

    /// <summary>
    /// Uses the configured version, falling back to the assembly informational version so exported
    /// telemetry has a stable application identity even when no explicit version is configured.
    /// </summary>
    public string ResolveServiceVersion() =>
        !string.IsNullOrWhiteSpace(ServiceVersion)
            ? ServiceVersion
            : typeof(TelemetryOptions).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown";
}
