using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Features.ServiceCatalog;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using StackExchange.Redis;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Resilience;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class RedisOutageTests : CacheSqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public RedisOutageTests(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task ClosedRedisEndpointFallsBackToSqlWithinTheBudgetAndLogsDegradation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var loggerProvider = new CapturingLoggerProvider();
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            services => services.AddSingleton<ILoggerProvider>(loggerProvider),
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Cache"] = SchedulerApiFactory.UnreachableCacheConnection,
            });
        using var client = factory.CreateClient();
        var stopwatch = Stopwatch.StartNew();

        using var catalog = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
            cancellationToken);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Contains(
            "Oil Change",
            await catalog.Content.ReadAsStringAsync(cancellationToken),
            StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"Catalog fallback took {stopwatch.Elapsed}.");

        using var availability = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "2026-11-03T03:00:00Z"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        var availabilityDocument = JsonDocument.Parse(
            await availability.Content.ReadAsStringAsync(cancellationToken));
        Assert.True(availabilityDocument.RootElement.GetProperty("available").GetBoolean());

        using var ready = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var readyDocument = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Degraded", readyDocument.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            readyDocument.RootElement.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("name").GetString() == "redis"
                     && check.GetProperty("status").GetString() == "Degraded");

        Assert.Contains(
            loggerProvider.Entries,
            entry => entry.Level == LogLevel.Warning
                     && entry.Message.Contains("Redis cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PausedRedisReturnsBoundedResponsesAndRecovers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();
        var cacheKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon);

        await PauseCacheAsync();
        try
        {
            var singleStopwatch = Stopwatch.StartNew();
            using (var single = await client.GetAsync(
                       $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, single.StatusCode);
            }

            singleStopwatch.Stop();
            Assert.True(
                singleStopwatch.Elapsed < TimeSpan.FromSeconds(3),
                $"A single stalled-cache request took {singleStopwatch.Elapsed}.");

            var stopwatch = Stopwatch.StartNew();
            var requests = Enumerable.Range(0, 5)
                .Select(_ => client.GetAsync(
                    $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                    cancellationToken))
                .ToArray();
            var responses = await Task.WhenAll(requests);
            stopwatch.Stop();

            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                    Assert.Contains(
                        "Oil Change",
                        await response.Content.ReadAsStringAsync(cancellationToken),
                        StringComparison.Ordinal);
                }
            }

            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Concurrent stalled-cache requests took {stopwatch.Elapsed}.");
        }
        finally
        {
            await UnpauseCacheAsync();
        }

        var recovered = await WaitForCatalogCacheAsync(client, cacheKey, cancellationToken);
        Assert.True(recovered, "The catalog cache did not repopulate after Redis recovered.");

        using var afterRecovery = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, afterRecovery.StatusCode);
    }

    private async Task<bool> WaitForCatalogCacheAsync(
        HttpClient client,
        string cacheKey,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (await Cache.GetStringAsync(cacheKey, cancellationToken) is not null)
                {
                    return true;
                }
            }
            catch (RedisException)
            {
                // The cache is still recovering; keep the bounded polling.
            }

            using (await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken))
            {
            }

            await Task.Delay(200, cancellationToken);
        }

        return false;
    }
}
