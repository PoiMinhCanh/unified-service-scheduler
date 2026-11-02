using System.Net;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Resilience;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class CombinedOutageTests : CacheSqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public CombinedOutageTests(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task BothDependenciesDownFailSafelyAndARecoveredHostServesRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();

        using (var outageFactory = CreateFactory(
                   new FakeTimeProvider(FixedClockUtc),
                   overrides: new Dictionary<string, string?>
                   {
                       ["ConnectionStrings:Runtime"] = SchedulerApiFactory.UnreachableRuntimeConnection,
                       ["ConnectionStrings:Cache"] = SchedulerApiFactory.UnreachableCacheConnection,
                   }))
        {
            using var outageClient = outageFactory.CreateClient();

            using var live = await outageClient.GetAsync("/health/live", cancellationToken);
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);

            using var ready = await outageClient.GetAsync("/health/ready", cancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);

            using var catalog = await outageClient.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, catalog.StatusCode);
        }

        using var recoveredFactory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var recoveredClient = recoveredFactory.CreateClient();

        using var recoveredReady = await recoveredClient.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, recoveredReady.StatusCode);

        using var recoveredCatalog = await recoveredClient.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, recoveredCatalog.StatusCode);

        using var recoveredAvailability = await recoveredClient.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "2026-11-03T03:00:00Z"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, recoveredAvailability.StatusCode);
    }
}
