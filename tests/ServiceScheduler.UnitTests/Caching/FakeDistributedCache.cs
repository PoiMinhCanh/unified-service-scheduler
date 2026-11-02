using Microsoft.Extensions.Caching.Distributed;

namespace ServiceScheduler.UnitTests.Caching;

/// <summary>Configurable cache double for bounded-operation tests.</summary>
internal sealed class FakeDistributedCache : IDistributedCache
{
    public Func<string, CancellationToken, Task<byte[]?>>? GetAsyncHandler { get; set; }

    public Func<string, byte[], DistributedCacheEntryOptions, CancellationToken, Task>? SetAsyncHandler { get; set; }

    public Func<string, CancellationToken, Task>? RemoveAsyncHandler { get; set; }

    public byte[]? Get(string key) => throw new NotSupportedException();

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
        GetAsyncHandler?.Invoke(key, token) ?? Task.FromResult<byte[]?>(null);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
        throw new NotSupportedException();

    public Task SetAsync(
        string key,
        byte[] value,
        DistributedCacheEntryOptions options,
        CancellationToken token = default) =>
        SetAsyncHandler?.Invoke(key, value, options, token) ?? Task.CompletedTask;

    public void Refresh(string key) => throw new NotSupportedException();

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => throw new NotSupportedException();

    public Task RemoveAsync(string key, CancellationToken token = default) =>
        RemoveAsyncHandler?.Invoke(key, token) ?? Task.CompletedTask;
}
