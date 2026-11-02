using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using ServiceScheduler.Api.Features.ServiceCatalog;
using ServiceScheduler.Api.Features.ServiceCatalog.Contracts;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Catalog;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class CatalogCacheTests : CacheSqlServerTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CatalogCacheTests(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task MissPopulatesTheExactKeyWithTheOrderedPayload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();

        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.Equal("Brake Service", document.RootElement[0].GetProperty("name").GetString());
        Assert.Equal(120, document.RootElement[0].GetProperty("durationMinutes").GetInt32());
        Assert.Equal("Oil Change", document.RootElement[1].GetProperty("name").GetString());
        Assert.Equal(60, document.RootElement[1].GetProperty("durationMinutes").GetInt32());

        var cacheKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon);
        var cachedJson = await Cache.GetStringAsync(cacheKey, cancellationToken);
        Assert.NotNull(cachedJson);

        var cached = JsonSerializer.Deserialize<ServiceTypeResponse[]>(cachedJson, JsonOptions);
        Assert.NotNull(cached);
        Assert.Equal(["Brake Service", "Oil Change"], cached.Select(service => service.Name));
        Assert.Equal([120, 60], cached.Select(service => service.DurationMinutes));
    }

    [Fact]
    public async Task HitReturnsTheCachedPayloadWithoutSqlServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();

        var cachedId = Guid.NewGuid();
        var cachedPayload = JsonSerializer.SerializeToUtf8Bytes(
            new[] { new ServiceTypeResponse(cachedId, "Cached Only", 45) },
            JsonOptions);
        await Cache.SetAsync(
            ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon),
            cachedPayload,
            cancellationToken);

        using var factory = CreateFactory(overrides: new Dictionary<string, string?>
        {
            ["ConnectionStrings:Runtime"] = SchedulerApiFactory.UnreachableRuntimeConnection,
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(1, document.RootElement.GetArrayLength());
        Assert.Equal(cachedId, document.RootElement[0].GetProperty("id").GetGuid());
        Assert.Equal("Cached Only", document.RootElement[0].GetProperty("name").GetString());
        Assert.Equal(45, document.RootElement[0].GetProperty("durationMinutes").GetInt32());
    }

    [Fact]
    public async Task CorruptCachedPayloadFallsBackToSqlAndRepairsTheKey()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();
        var cacheKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon);
        await Cache.SetStringAsync(cacheKey, "{not-valid-json", cancellationToken);

        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            "Oil Change",
            await response.Content.ReadAsStringAsync(cancellationToken),
            StringComparison.Ordinal);

        var repaired = await Cache.GetStringAsync(cacheKey, cancellationToken);
        Assert.NotNull(repaired);
        var repairedDocument = JsonDocument.Parse(repaired);
        Assert.Equal(2, repairedDocument.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ExpiryCausesAFreshDatabaseRead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceTypeId = Guid.NewGuid();
        await FlushCacheAsync();
        await TestData.InsertServiceTypeAsync(
            serviceTypeId,
            SeedData.DealershipIds.Saigon,
            "Probe Expiry Service",
            30,
            cancellationToken);

        try
        {
            using var factory = CreateFactory(overrides: new Dictionary<string, string?>
            {
                ["Scheduler:Cache:ServiceCatalog:AbsoluteTtl"] = "00:00:01",
            });
            using var client = factory.CreateClient();

            using var first = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Contains(
                "Probe Expiry Service",
                await first.Content.ReadAsStringAsync(cancellationToken),
                StringComparison.Ordinal);

            var cacheKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon);
            await WaitForExpiryAsync(cacheKey, cancellationToken);

            await TestData.UpdateServiceTypeNameAsync(
                serviceTypeId,
                "Probe Expiry Renamed",
                cancellationToken);

            using var second = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var body = await second.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("Probe Expiry Renamed", body, StringComparison.Ordinal);
            Assert.NotNull(await Cache.GetStringAsync(cacheKey, cancellationToken));
        }
        finally
        {
            await TestData.DeleteServiceTypeAsync(serviceTypeId, cancellationToken);
        }
    }

    [Fact]
    public async Task ExplicitInvalidationCausesAFreshDatabaseRead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceTypeId = Guid.NewGuid();
        await FlushCacheAsync();
        await TestData.InsertServiceTypeAsync(
            serviceTypeId,
            SeedData.DealershipIds.Saigon,
            "Probe Invalidation Service",
            30,
            cancellationToken);

        try
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient();
            var cacheKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon);

            using var first = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Contains(
                "Probe Invalidation Service",
                await first.Content.ReadAsStringAsync(cancellationToken),
                StringComparison.Ordinal);

            await TestData.UpdateServiceTypeNameAsync(
                serviceTypeId,
                "Probe Invalidation Renamed",
                cancellationToken);
            await Cache.RemoveAsync(cacheKey, cancellationToken);

            using var second = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
            var body = await second.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("Probe Invalidation Renamed", body, StringComparison.Ordinal);
            Assert.NotNull(await Cache.GetStringAsync(cacheKey, cancellationToken));
        }
        finally
        {
            await TestData.DeleteServiceTypeAsync(serviceTypeId, cancellationToken);
        }
    }

    [Fact]
    public async Task DealershipCacheKeysAreIsolated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var serviceTypeId = Guid.NewGuid();
        await FlushCacheAsync();
        await TestData.InsertServiceTypeAsync(
            serviceTypeId,
            SeedData.DealershipIds.Saigon,
            "Probe Isolation Service",
            30,
            cancellationToken);

        try
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient();
            var saigonKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon);
            var londonKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.London);
            Assert.NotEqual(saigonKey, londonKey);

            using var saigon = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            using var london = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.London}/service-types",
                cancellationToken);

            Assert.Equal(HttpStatusCode.OK, saigon.StatusCode);
            Assert.Equal(HttpStatusCode.OK, london.StatusCode);
            Assert.NotNull(await Cache.GetStringAsync(saigonKey, cancellationToken));
            Assert.NotNull(await Cache.GetStringAsync(londonKey, cancellationToken));

            await TestData.UpdateServiceTypeNameAsync(serviceTypeId, "Probe Isolation Renamed", cancellationToken);
            await Cache.RemoveAsync(saigonKey, cancellationToken);

            using var refreshedSaigon = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            var saigonBody = await refreshedSaigon.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("Probe Isolation Renamed", saigonBody, StringComparison.Ordinal);

            using var cachedLondon = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.London}/service-types",
                cancellationToken);
            var londonBody = await cachedLondon.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("Annual Service", londonBody, StringComparison.Ordinal);
            Assert.DoesNotContain("Probe Isolation", londonBody, StringComparison.Ordinal);
        }
        finally
        {
            await TestData.DeleteServiceTypeAsync(serviceTypeId, cancellationToken);
        }
    }

    private async Task WaitForExpiryAsync(string cacheKey, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await Cache.GetStringAsync(cacheKey, cancellationToken) is null)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        Assert.Fail($"The cache key '{cacheKey}' did not expire within the bounded deadline.");
    }
}
