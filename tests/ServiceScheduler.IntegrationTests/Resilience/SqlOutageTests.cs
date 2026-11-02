using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Features.ServiceCatalog;
using ServiceScheduler.Api.Features.ServiceCatalog.Contracts;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Resilience;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class SqlOutageTests : CacheSqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, string?> SqlDown =
        new(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Runtime"] = SchedulerApiFactory.UnreachableRuntimeConnection,
        };

    public SqlOutageTests(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task SqlDependentReadsReturnControlledFailureInsteadOfFalseAvailability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc), overrides: SqlDown);
        using var client = factory.CreateClient();

        using var discovery = await client.GetAsync("/api/dealerships", cancellationToken);
        await AssertProblemAsync(discovery, HttpStatusCode.ServiceUnavailable, "dependency_unavailable");

        using var availability = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "2026-11-03T03:00:00Z"),
            cancellationToken);
        await AssertProblemAsync(availability, HttpStatusCode.ServiceUnavailable, "dependency_unavailable");

        using var catalogMiss = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
            cancellationToken);
        await AssertProblemAsync(catalogMiss, HttpStatusCode.ServiceUnavailable, "dependency_unavailable");
        Assert.Null(await Cache.GetStringAsync(
            ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon),
            cancellationToken));

        using var ready = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);

        using var live = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task CatalogHitReturnsCachedDataWhileSqlIsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await FlushCacheAsync();
        var cacheKey = ServiceCatalogCacheKeys.ForDealership(SeedData.DealershipIds.Saigon);
        var cachedPayload = JsonSerializer.Serialize(
            new[] { new ServiceTypeResponse(Guid.NewGuid(), "Cached During Outage", 45) },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Cache.SetStringAsync(cacheKey, cachedPayload, cancellationToken);

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc), overrides: SqlDown);
            using var client = factory.CreateClient();

            using var catalogHit = await client.GetAsync(
                $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, catalogHit.StatusCode);
            Assert.Contains(
                "Cached During Outage",
                await catalogHit.Content.ReadAsStringAsync(cancellationToken),
                StringComparison.Ordinal);

            using var discovery = await client.GetAsync("/api/dealerships", cancellationToken);
            await AssertProblemAsync(discovery, HttpStatusCode.ServiceUnavailable, "dependency_unavailable");

            using var availability = await client.GetAsync(
                AvailabilityTestUrls.For(
                    SeedData.DealershipIds.Saigon,
                    SeedData.ServiceTypeIds.SaigonOilChange,
                    "2026-11-03T03:00:00Z"),
                cancellationToken);
            await AssertProblemAsync(availability, HttpStatusCode.ServiceUnavailable, "dependency_unavailable");

            using var ready = await client.GetAsync("/health/ready", cancellationToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        }
        finally
        {
            await Cache.RemoveAsync(cacheKey, cancellationToken);
        }
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("StackTrace", problem.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", problem.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
