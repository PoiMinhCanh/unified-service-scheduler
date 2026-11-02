using System.Collections.Concurrent;
using Testcontainers.Redis;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>
/// Starts one pinned Redis container per combined collection. Each test class receives a distinct
/// Redis database index so keys are isolated without changing the production key format.
/// </summary>
public sealed class RedisContainerFixture : IAsyncLifetime
{
    public const string ImageReference =
        "redis:7.4.12-alpine@sha256:8da19c7399dca900c6822a1cf30f9d2285585e2c4b457ff4095b0f4500054d3f";

    private const int RedisPort = 6379;

    private readonly ConcurrentDictionary<Type, int> _databaseIndexes = new();
    private RedisContainer? _container;
    private int _nextDatabaseIndex;

    public async ValueTask InitializeAsync()
    {
        _container = new RedisBuilder(ImageReference)
            .WithPortBinding(RedisPort, true)
            .Build();

        await _container.StartAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    public string GetCacheConnection(Type testClass)
    {
        ArgumentNullException.ThrowIfNull(testClass);

        if (_container is null)
        {
            throw new InvalidOperationException("The Redis container has not started.");
        }

        var databaseIndex = _databaseIndexes.GetOrAdd(
            testClass,
            _ => Interlocked.Increment(ref _nextDatabaseIndex));

        if (databaseIndex > 15)
        {
            throw new InvalidOperationException("The Redis test database indexes are exhausted.");
        }

        return $"{_container.Hostname}:{_container.GetMappedPublicPort(RedisPort)},defaultDatabase={databaseIndex}";
    }

    public Task PauseAsync() => _container?.PauseAsync() ?? throw new InvalidOperationException("The Redis container has not started.");

    public Task UnpauseAsync() => _container?.UnpauseAsync() ?? throw new InvalidOperationException("The Redis container has not started.");
}
