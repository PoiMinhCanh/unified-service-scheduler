using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Features.ServiceCatalog;
using ServiceScheduler.Api.Features.ServiceCatalog.Contracts;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class AvailabilityDurationTests : CacheSqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public AvailabilityDurationTests(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task AvailabilityUsesSqlDurationEvenWhenTheCatalogCacheIsStale()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();
        var cacheKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon);
        var staleCatalog = JsonSerializer.Serialize(
            new[]
            {
                new ServiceTypeResponse(SeedData.ServiceTypeIds.SaigonOilChange, "Oil Change", 999),
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Cache.SetStringAsync(cacheKey, staleCatalog, cancellationToken);

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();

            using var availability = await client.GetAsync(
                AvailabilityTestUrls.For(
                    SeedData.DealershipIds.Saigon,
                    SeedData.ServiceTypeIds.SaigonOilChange,
                    "2026-11-03T03:00:00Z"),
                cancellationToken);

            Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
            var document = JsonDocument.Parse(await availability.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal("2026-11-03T04:00:00Z", document.RootElement.GetProperty("endsAt").GetString());

            using var catalog = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            var catalogBody = await catalog.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("999", catalogBody, StringComparison.Ordinal);
        }
        finally
        {
            await Cache.RemoveAsync(cacheKey, cancellationToken);
        }
    }
}
