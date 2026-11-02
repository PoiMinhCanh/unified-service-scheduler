using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Options;

namespace ServiceScheduler.Api.Infrastructure.Caching;

/// <summary>Applies the validated cache budgets to the Redis distributed-cache provider.</summary>
public sealed class RedisCacheOptionsSetup(IOptions<CacheOptions> cacheOptions) : IConfigureOptions<RedisCacheOptions>
{
    public void Configure(RedisCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.ConfigurationOptions = RedisCacheConfiguration.Create(cacheOptions.Value);
    }
}
