using Microsoft.Extensions.Options;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Validates telemetry configuration only when telemetry is enabled. An unreachable backend is a
/// runtime condition, not a configuration error; invalid bounds or an invalid endpoint are.
/// </summary>
public sealed class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>
{
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (!Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{TelemetryOptions.SectionName}:OtlpEndpoint must be an absolute HTTP(S) URI.");
        }

        if (double.IsNaN(options.SamplingRatio) || options.SamplingRatio is < 0 or > 1)
        {
            failures.Add($"{TelemetryOptions.SectionName}:SamplingRatio must be between 0 and 1.");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceName))
        {
            failures.Add($"{TelemetryOptions.SectionName}:ServiceName is required when telemetry is enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceInstanceId))
        {
            failures.Add($"{TelemetryOptions.SectionName}:ServiceInstanceId is required when telemetry is enabled.");
        }

        RequirePositive(failures, options.TraceBatchDelayMilliseconds, nameof(options.TraceBatchDelayMilliseconds));
        RequirePositive(failures, options.TraceExportTimeoutMilliseconds, nameof(options.TraceExportTimeoutMilliseconds));
        RequirePositive(failures, options.TraceBatchQueueSize, nameof(options.TraceBatchQueueSize));
        RequirePositive(failures, options.TraceBatchSize, nameof(options.TraceBatchSize));
        RequirePositive(failures, options.OtlpTimeoutMilliseconds, nameof(options.OtlpTimeoutMilliseconds));
        RequirePositive(failures, options.MetricExportIntervalMilliseconds, nameof(options.MetricExportIntervalMilliseconds));
        RequirePositive(failures, options.MetricExportTimeoutMilliseconds, nameof(options.MetricExportTimeoutMilliseconds));

        if (options.TraceBatchSize > options.TraceBatchQueueSize)
        {
            failures.Add(
                $"{TelemetryOptions.SectionName}:TraceBatchSize must be less than or equal to TraceBatchQueueSize.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(List<string> failures, int value, string propertyName)
    {
        if (value <= 0)
        {
            failures.Add($"{TelemetryOptions.SectionName}:{propertyName} must be positive.");
        }
    }
}
