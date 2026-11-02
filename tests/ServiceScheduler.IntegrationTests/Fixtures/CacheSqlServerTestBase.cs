using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>
/// Gives a test class one initialized SQL database and an isolated Redis database index. Tests
/// flush their own Redis database and remove only their own SQL probe rows.
/// </summary>
public abstract class CacheSqlServerTestBase : IAsyncLifetime
{
    private readonly RedisContainerFixture _redisFixture;
    private ServiceProvider? _cacheProvider;
    private ConnectionMultiplexer? _multiplexer;
    private IDatabase? _redisDatabase;

    protected CacheSqlServerTestBase(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
    {
        Fixture = fixture;
        _redisFixture = redisFixture;
    }

    protected SqlServerContainerFixture Fixture { get; }

    protected SqlTestDatabase Database { get; private set; } = null!;

    protected string SetupConnectionString => Database.SetupConnectionString;

    protected string RuntimeConnectionString => Database.RuntimeConnectionString;

    protected string CacheConnectionString { get; private set; } = string.Empty;

    protected IDistributedCache Cache { get; private set; } = null!;

    protected SchedulingTestData TestData => new(SetupConnectionString);

    public async ValueTask InitializeAsync()
    {
        Database = await Fixture.GetDatabaseAsync(GetType()).ConfigureAwait(false);
        CacheConnectionString = _redisFixture.GetCacheConnection(GetType());

        var services = new ServiceCollection();
        services.AddStackExchangeRedisCache(options => options.Configuration = CacheConnectionString);
        _cacheProvider = services.BuildServiceProvider();
        Cache = _cacheProvider.GetRequiredService<IDistributedCache>();

        _multiplexer = await ConnectionMultiplexer.ConnectAsync(CacheConnectionString).ConfigureAwait(false);
        _redisDatabase = _multiplexer.GetDatabase();
    }

    public virtual async ValueTask DisposeAsync()
    {
        await FlushCacheAsync().ConfigureAwait(false);

        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync().ConfigureAwait(false);
        }

        if (_cacheProvider is not null)
        {
            await _cacheProvider.DisposeAsync().ConfigureAwait(false);
        }
    }

    protected Task FlushCacheAsync() =>
        _redisDatabase is null
            ? throw new InvalidOperationException("The Redis connection has not started.")
            : _redisDatabase.ExecuteAsync("FLUSHDB");

    protected Task PauseCacheAsync() => _redisFixture.PauseAsync();

    protected Task UnpauseCacheAsync() => _redisFixture.UnpauseAsync();

    protected SchedulerApiFactory CreateFactory(
        TimeProvider? clock = null,
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Runtime"] = RuntimeConnectionString,
            ["ConnectionStrings:Cache"] = CacheConnectionString,
        };

        if (overrides is not null)
        {
            foreach (var setting in overrides)
            {
                settings[setting.Key] = setting.Value;
            }
        }

        return new SchedulerApiFactory(settings)
        {
            TimeProvider = clock,
            ConfigureTestServices = configureServices,
        };
    }
}
