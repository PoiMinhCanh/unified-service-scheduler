using System.Net;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Features.ServiceCatalog;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class AvailabilityReadOnlyTests : CacheSqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public AvailabilityReadOnlyTests(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task AvailabilityCreatesNoReservationAndTouchesNoCache()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();
        var appointmentsBefore = await TestData.CountAppointmentsAsync(cancellationToken);

        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "2026-11-03T03:00:00Z"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(appointmentsBefore, await TestData.CountAppointmentsAsync(cancellationToken));
        Assert.Null(await Cache.GetStringAsync(
            ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon),
            cancellationToken));
    }
}
