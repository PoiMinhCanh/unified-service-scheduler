using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServiceScheduler.Api.Infrastructure.Caching;

/// <summary>
/// Reports the optional cache dependency. A reachable Redis instance is healthy even when the
/// health key is absent; recoverable Redis failures or budget expiry degrade readiness without
/// failing it.
/// </summary>
public sealed class CacheHealthCheck(BoundedCache cache) : IHealthCheck
{
    public const string Name = "redis";

    private const string HealthKey = "scheduler:v1:health";

    // The first health operation may include establishing the multiplexer. Keep this inside the
    // 2-second health-check registration timeout while request operations retain the 250 ms budget.
    private static readonly TimeSpan HealthOperationTimeout = TimeSpan.FromSeconds(1);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await cache
            .TryGetAsync(HealthKey, HealthOperationTimeout, cancellationToken, recordTelemetry: false)
            .ConfigureAwait(false);

        return result.Status == CacheReadStatus.Unavailable
            ? HealthCheckResult.Degraded("The service catalog cache is unavailable.")
            : HealthCheckResult.Healthy("The service catalog cache is reachable.");
    }
}
