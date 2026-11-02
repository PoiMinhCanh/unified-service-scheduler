using StackExchange.Redis;

namespace ServiceScheduler.Api.Infrastructure.Caching;

/// <summary>
/// Creates bounded StackExchange.Redis options. The multiplexer may be returned while disconnected
/// and recover later; commands fail fast instead of queuing an unbounded backlog.
/// </summary>
public static class RedisCacheConfiguration
{
    public static ConfigurationOptions Create(CacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException($"{CacheOptions.ConnectionStringKey} is required.");
        }

        var configuration = ConfigurationOptions.Parse(options.ConnectionString);
        configuration.AbortOnConnectFail = false;
        configuration.ConnectTimeout = ToMilliseconds(options.ConnectTimeout);
        configuration.SyncTimeout = ToMilliseconds(options.CommandTimeout);
        configuration.AsyncTimeout = ToMilliseconds(options.CommandTimeout);
        configuration.ConnectRetry = options.ConnectRetry;
        configuration.BacklogPolicy = BacklogPolicy.FailFast;
        configuration.AllowAdmin = false;
        return configuration;
    }

    private static int ToMilliseconds(TimeSpan value) => checked((int)value.TotalMilliseconds);
}
