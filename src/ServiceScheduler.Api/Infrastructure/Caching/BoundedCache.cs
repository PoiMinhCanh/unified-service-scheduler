using System.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Infrastructure.Telemetry;
using StackExchange.Redis;

namespace ServiceScheduler.Api.Infrastructure.Caching;

/// <summary>
/// Bounds every cache operation with a caller-side budget and degrades to SQL Server on recoverable
/// Redis failures. Instrumentation is local and synchronous; it never adds I/O or extends a budget.
/// Health probes use the non-recording path so they do not affect service-catalog ratios.
/// </summary>
public sealed class BoundedCache(
    IDistributedCache cache,
    IOptions<CacheOptions> cacheOptions,
    ILogger<BoundedCache> logger)
{
    public Task<CacheReadResult> TryGetAsync(string key, CancellationToken cancellationToken) =>
        TryGetAsync(key, cacheOptions.Value.OperationTimeout, cancellationToken, recordTelemetry: true);

    /// <summary>
    /// Reads with an explicit caller budget. The health check uses a slightly larger budget because
    /// the first operation may include establishing the Redis multiplexer; request paths keep the
    /// configured 250 ms budget.
    /// </summary>
    public Task<CacheReadResult> TryGetAsync(
        string key,
        TimeSpan operationBudget,
        CancellationToken cancellationToken) =>
        TryGetAsync(key, operationBudget, cancellationToken, recordTelemetry: true);

    /// <summary>
    /// Reads with an explicit budget and telemetry switch. The health check disables recording so
    /// its probe does not enter the catalog hit-ratio denominator.
    /// </summary>
    public async Task<CacheReadResult> TryGetAsync(
        string key,
        TimeSpan operationBudget,
        CancellationToken cancellationToken,
        bool recordTelemetry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var activity = recordTelemetry
            ? ServiceSchedulerTelemetry.StartCacheActivity("get", (int)operationBudget.TotalMilliseconds)
            : null;
        var operation = cache.GetAsync(key, cancellationToken);
        try
        {
            var value = await operation
                .WaitAsync(operationBudget, cancellationToken)
                .ConfigureAwait(false);

            if (value is null)
            {
                Complete(activity, "get", "miss", isFailure: false, recordTelemetry);
                return new CacheReadResult(CacheReadStatus.Miss, null);
            }

            Complete(activity, "get", "hit", isFailure: false, recordTelemetry);
            return new CacheReadResult(CacheReadStatus.Hit, value);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Observe(operation);
            ServiceSchedulerTelemetry.CompleteCacheActivity(activity, result: null, isFailure: false);
            throw;
        }
        catch (TimeoutException exception)
        {
            Observe(operation);
            Complete(activity, "get", "failure", isFailure: true, recordTelemetry);
            LogDegradation(exception, "get", operationBudget);
            return new CacheReadResult(CacheReadStatus.Unavailable, null);
        }
        catch (RedisException exception)
        {
            Complete(activity, "get", "failure", isFailure: true, recordTelemetry);
            LogDegradation(exception, "get", operationBudget);
            return new CacheReadResult(CacheReadStatus.Unavailable, null);
        }
    }

    public async Task<bool> SetAsync(
        string key,
        byte[] value,
        TimeSpan absoluteTtl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        var entryOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = absoluteTtl,
        };
        var operation = cache.SetAsync(key, value, entryOptions, cancellationToken);
        return await CompleteAsync(operation, "set", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RemoveAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var operation = cache.RemoveAsync(key, cancellationToken);
        return await CompleteAsync(operation, "remove", cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> CompleteAsync(
        Task operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        var budget = cacheOptions.Value.OperationTimeout;
        using var activity = ServiceSchedulerTelemetry.StartCacheActivity(
            operationName,
            (int)budget.TotalMilliseconds);
        try
        {
            await operation
                .WaitAsync(budget, cancellationToken)
                .ConfigureAwait(false);
            Complete(activity, operationName, "success", isFailure: false, recordTelemetry: true);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Observe(operation);
            ServiceSchedulerTelemetry.CompleteCacheActivity(activity, result: null, isFailure: false);
            throw;
        }
        catch (TimeoutException exception)
        {
            Observe(operation);
            Complete(activity, operationName, "failure", isFailure: true, recordTelemetry: true);
            LogDegradation(exception, operationName, budget);
            return false;
        }
        catch (RedisException exception)
        {
            Complete(activity, operationName, "failure", isFailure: true, recordTelemetry: true);
            LogDegradation(exception, operationName, budget);
            return false;
        }
    }

    private static void Complete(
        Activity? activity,
        string operation,
        string result,
        bool isFailure,
        bool recordTelemetry)
    {
        ServiceSchedulerTelemetry.CompleteCacheActivity(activity, result, isFailure);
        if (recordTelemetry)
        {
            ServiceSchedulerTelemetry.RecordCacheOperation(operation, result);
        }
    }

    private void LogDegradation(Exception exception, string operationName, TimeSpan operationBudget) =>
        logger.LogWarning(
            exception,
            "Redis cache {CacheOperation} failed within the configured {BudgetMilliseconds} ms budget; falling back to SQL Server. EventName {EventName} CacheName {CacheName} CacheResult {CacheResult}",
            operationName,
            (int)operationBudget.TotalMilliseconds,
            "CacheOperation",
            ServiceSchedulerTelemetry.CacheName,
            "failure");

    /// <summary>
    /// Stops an abandoned operation from becoming an unobserved task exception after the caller
    /// stops waiting. The underlying command remains bounded by the Redis command timeout.
    /// </summary>
    private static void Observe(Task operation) =>
        _ = operation.ContinueWith(
            static faulted => _ = faulted.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
