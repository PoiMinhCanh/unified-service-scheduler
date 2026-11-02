using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using ServiceScheduler.Api.Features.ServiceCatalog;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Catalog;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class CatalogContractTests : CacheSqlServerTestBase
{
    public CatalogContractTests(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task EmptyCatalogIsCachedAndUnknownDealershipIsNot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dealershipId = Guid.NewGuid();
        await FlushCacheAsync();
        await TestData.InsertDealershipAsync(dealershipId, "Probe Empty Motors", "Asia/Ho_Chi_Minh", cancellationToken);

        try
        {
            using var factory = CreateFactory();
            using var client = factory.CreateClient();

            using var response = await client.GetAsync(
                $"/api/dealerships/{dealershipId}/service-types",
                cancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(0, document.RootElement.GetArrayLength());

            var cacheKey = ServiceCatalogCacheKeys.ForDealership(dealershipId);
            Assert.Equal("[]", await Cache.GetStringAsync(cacheKey, cancellationToken));

            var unknownDealershipId = Guid.NewGuid();
            using var unknown = await client.GetAsync(
                $"/api/dealerships/{unknownDealershipId}/service-types",
                cancellationToken);

            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            var problem = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal("not_found", problem.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
            Assert.Null(await Cache.GetStringAsync(
                ServiceCatalogCacheKeys.ForDealership(unknownDealershipId),
                cancellationToken));
        }
        finally
        {
            await TestData.DeleteDealershipAsync(dealershipId, cancellationToken);
        }
    }

    [Fact]
    public async Task MalformedDealershipIdReturnsValidationProblem()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/api/dealerships/not-a-guid/service-types",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("validation_error", problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task ValidUnknownDealershipReturnsNotFoundWithoutCaching()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();
        var unknownDealershipId = Guid.NewGuid();

        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/dealerships/{unknownDealershipId}/service-types",
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(await Cache.GetStringAsync(
            ServiceCatalogCacheKeys.ForDealership(unknownDealershipId),
            cancellationToken));
    }
}
