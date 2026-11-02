using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceScheduler.Api.Infrastructure.Caching;
using StackExchange.Redis;
using Xunit;

namespace ServiceScheduler.UnitTests.Caching;

public sealed class BoundedCacheTests
{
    [Fact]
    public async Task ReadReturnsHitWhenValueExists()
    {
        var fake = new FakeDistributedCache
        {
            GetAsyncHandler = (_, _) => Task.FromResult<byte[]?>([1, 2, 3]),
        };
        var cache = CreateCache(fake);

        var result = await cache.TryGetAsync("key", TestContext.Current.CancellationToken);

        Assert.Equal(CacheReadStatus.Hit, result.Status);
        Assert.Equal([1, 2, 3], result.Value);
    }

    [Fact]
    public async Task ReadReturnsMissWhenValueIsAbsent()
    {
        var fake = new FakeDistributedCache
        {
            GetAsyncHandler = (_, _) => Task.FromResult<byte[]?>(null),
        };
        var cache = CreateCache(fake);

        var result = await cache.TryGetAsync("key", TestContext.Current.CancellationToken);

        Assert.Equal(CacheReadStatus.Miss, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ReadReturnsUnavailableWhenTheBudgetExpires()
    {
        var pending = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fake = new FakeDistributedCache
        {
            GetAsyncHandler = (_, _) => pending.Task,
        };
        var cache = CreateCache(fake, operationMilliseconds: 20);
        var stopwatch = Stopwatch.StartNew();

        var result = await cache.TryGetAsync("key", TestContext.Current.CancellationToken);

        stopwatch.Stop();
        Assert.Equal(CacheReadStatus.Unavailable, result.Status);
        Assert.InRange(
            stopwatch.Elapsed,
            TimeSpan.FromMilliseconds(15),
            TimeSpan.FromMilliseconds(750));
    }

    [Fact]
    public async Task RequestCancellationPropagates()
    {
        var fake = new FakeDistributedCache
        {
            GetAsyncHandler = (_, token) => Task.FromCanceled<byte[]?>(token),
        };
        var cache = CreateCache(fake);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cache.TryGetAsync("key", cancellation.Token));
    }

    [Fact]
    public async Task UnexpectedExceptionIsNotSwallowed()
    {
        var fake = new FakeDistributedCache
        {
            GetAsyncHandler = (_, _) => Task.FromException<byte[]?>(new InvalidOperationException("programming error")),
        };
        var cache = CreateCache(fake);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.TryGetAsync("key", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RedisConnectionFailureIsRecoverable()
    {
        var fake = new FakeDistributedCache
        {
            GetAsyncHandler = (_, _) => Task.FromException<byte[]?>(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test")),
        };
        var cache = CreateCache(fake);

        var result = await cache.TryGetAsync("key", TestContext.Current.CancellationToken);

        Assert.Equal(CacheReadStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task WriteSuccessReturnsTrue()
    {
        var fake = new FakeDistributedCache
        {
            SetAsyncHandler = (_, _, _, _) => Task.CompletedTask,
        };
        var cache = CreateCache(fake);

        Assert.True(await cache.SetAsync(
            "key",
            [1],
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WriteFailureIsRecoverable()
    {
        var fake = new FakeDistributedCache
        {
            SetAsyncHandler = (_, _, _, _) => Task.FromException(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test")),
        };
        var cache = CreateCache(fake);

        Assert.False(await cache.SetAsync(
            "key",
            [1],
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RemoveFailureIsRecoverable()
    {
        var fake = new FakeDistributedCache
        {
            RemoveAsyncHandler = (_, _) => Task.FromException(
                new RedisConnectionException(ConnectionFailureType.UnableToConnect, "test")),
        };
        var cache = CreateCache(fake);

        Assert.False(await cache.RemoveAsync("key", TestContext.Current.CancellationToken));
    }

    private static BoundedCache CreateCache(FakeDistributedCache fake, int operationMilliseconds = 250) =>
        new(
            fake,
            Options.Create(new CacheOptions
            {
                OperationTimeout = TimeSpan.FromMilliseconds(operationMilliseconds),
            }),
            NullLogger<BoundedCache>.Instance);
}
