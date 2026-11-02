using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Infrastructure.Caching;
using ServiceScheduler.Api.Infrastructure.Telemetry;
using ServiceScheduler.UnitTests.Caching;
using StackExchange.Redis;
using Xunit;

namespace ServiceScheduler.UnitTests.Telemetry;

public sealed class CacheTelemetryTests
{
    [Fact]
    public async Task HitRecordsActivityAndCounterWithoutTheKey()
    {
        using var activities = new ActivityTestListener();
        using var metrics = new MetricTestListener();
        var cache = CreateCache(new FakeDistributedCache
        {
            GetAsyncHandler = static (_, _) => Task.FromResult<byte[]?>([1, 2, 3]),
        });

        var result = await cache.TryGetAsync("secret-key", TestContext.Current.CancellationToken);

        Assert.Equal(CacheReadStatus.Hit, result.Status);
        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("cache.get", activity.OperationName);
        Assert.Equal("hit", activity.GetTagItem("cache.result"));
        Assert.DoesNotContain("secret-key", activity.Tags.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            metrics.Measurements,
            measurement => measurement.Instrument == ServiceSchedulerTelemetry.CacheOperationsInstrumentName
                           && Equals(measurement.Tags["operation"], "get")
                           && Equals(measurement.Tags["result"], "hit"));
    }

    [Fact]
    public async Task MissRecordsMiss()
    {
        using var activities = new ActivityTestListener();
        using var metrics = new MetricTestListener();
        var cache = CreateCache(new FakeDistributedCache
        {
            GetAsyncHandler = static (_, _) => Task.FromResult<byte[]?>(null),
        });

        var result = await cache.TryGetAsync("secret-key", TestContext.Current.CancellationToken);

        Assert.Equal(CacheReadStatus.Miss, result.Status);
        Assert.Equal("miss", Assert.Single(activities.Stopped).GetTagItem("cache.result"));
        Assert.Contains(
            metrics.Measurements,
            measurement => Equals(measurement.Tags["operation"], "get")
                           && Equals(measurement.Tags["result"], "miss"));
    }

    [Fact]
    public async Task TimeoutRecordsFailureAndKeepsFallback()
    {
        using var activities = new ActivityTestListener();
        using var metrics = new MetricTestListener();
        var pending = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cache = CreateCache(
            new FakeDistributedCache { GetAsyncHandler = (_, _) => pending.Task },
            operationMilliseconds: 20);

        var result = await cache.TryGetAsync("secret-key", TestContext.Current.CancellationToken);

        Assert.Equal(CacheReadStatus.Unavailable, result.Status);
        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("failure", activity.GetTagItem("cache.result"));
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Contains(
            metrics.Measurements,
            measurement => Equals(measurement.Tags["result"], "failure"));
    }

    [Fact]
    public async Task RedisFailureRecordsFailure()
    {
        using var activities = new ActivityTestListener();
        using var metrics = new MetricTestListener();
        var cache = CreateCache(new FakeDistributedCache
        {
            SetAsyncHandler = static (_, _, _, _) => Task.FromException(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test")),
        });

        var result = await cache.SetAsync(
            "secret-key",
            [1],
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        Assert.False(result);
        var activity = Assert.Single(activities.Stopped);
        Assert.Equal("cache.set", activity.OperationName);
        Assert.Equal("failure", activity.GetTagItem("cache.result"));
        Assert.Contains(
            metrics.Measurements,
            measurement => Equals(measurement.Tags["operation"], "set")
                           && Equals(measurement.Tags["result"], "failure"));
    }

    [Fact]
    public async Task CancellationIsNotCountedAsFailure()
    {
        using var activities = new ActivityTestListener();
        using var metrics = new MetricTestListener();
        var cache = CreateCache(new FakeDistributedCache
        {
            GetAsyncHandler = static (_, token) => Task.FromCanceled<byte[]?>(token),
        });
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.TryGetAsync("secret-key", cancellation.Token));

        var activity = Assert.Single(activities.Stopped);
        Assert.Null(activity.GetTagItem("cache.result"));
        Assert.DoesNotContain(
            metrics.Measurements,
            measurement => Equals(measurement.Tags["result"], "failure"));
    }

    [Fact]
    public async Task HealthReadsAreNotRecorded()
    {
        using var activities = new ActivityTestListener();
        using var metrics = new MetricTestListener();
        var cache = CreateCache(new FakeDistributedCache
        {
            GetAsyncHandler = static (_, _) => Task.FromResult<byte[]?>(null),
        });

        await cache.TryGetAsync(
            "scheduler:v1:health",
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken,
            recordTelemetry: false);

        Assert.Empty(activities.Stopped);
        Assert.Empty(metrics.Measurements);
    }

    [Fact]
    public async Task SetSuccessAndRemoveSuccessAreRecorded()
    {
        using var activities = new ActivityTestListener();
        using var metrics = new MetricTestListener();
        var cache = CreateCache(new FakeDistributedCache());

        Assert.True(await cache.SetAsync(
            "secret-key",
            [1],
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));
        Assert.True(await cache.RemoveAsync("secret-key", TestContext.Current.CancellationToken));

        Assert.Equal(2, activities.Stopped.Count);
        Assert.Contains(
            metrics.Measurements,
            measurement => Equals(measurement.Tags["operation"], "set")
                           && Equals(measurement.Tags["result"], "success"));
        Assert.Contains(
            metrics.Measurements,
            measurement => Equals(measurement.Tags["operation"], "remove")
                           && Equals(measurement.Tags["result"], "success"));
    }

    private static BoundedCache CreateCache(
        FakeDistributedCache fake,
        int operationMilliseconds = 250) =>
        new(
            fake,
            Options.Create(new CacheOptions
            {
                OperationTimeout = TimeSpan.FromMilliseconds(operationMilliseconds),
            }),
            NullLogger<BoundedCache>.Instance);
}
