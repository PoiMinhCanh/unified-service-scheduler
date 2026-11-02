using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Health;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class CacheHealthTests : CacheSqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public CacheHealthTests(SqlServerContainerFixture fixture, RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task ReadinessIsHealthyWhenSqlAndRedisAreHealthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var ready = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", FindCheckStatus(body.RootElement, "database"));
        Assert.Equal("Healthy", FindCheckStatus(body.RootElement, "redis"));
    }

    [Fact]
    public async Task RedisDownDegradesReadinessButKeepsSqlBackedRequestsWorking()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            overrides: new Dictionary<string, string?>
            {
                ["ConnectionStrings:Cache"] = SchedulerApiFactory.UnreachableCacheConnection,
            });
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var ready = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Degraded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Healthy", FindCheckStatus(body.RootElement, "database"));
        Assert.Equal("Degraded", FindCheckStatus(body.RootElement, "redis"));

        using var catalog = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);

        using var availability = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "2026-11-03T03:00:00Z"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
    }

    [Fact]
    public async Task SqlDownMakesReadinessUnhealthyRegardlessOfRedis()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            overrides: new Dictionary<string, string?>
            {
                ["ConnectionStrings:Runtime"] = SchedulerApiFactory.UnreachableRuntimeConnection,
            });
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var ready = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("Unhealthy", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Unhealthy", FindCheckStatus(body.RootElement, "database"));
    }

    [Fact]
    public async Task BothDependenciesDownMakesReadinessUnhealthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            overrides: new Dictionary<string, string?>
            {
                ["ConnectionStrings:Runtime"] = SchedulerApiFactory.UnreachableRuntimeConnection,
                ["ConnectionStrings:Cache"] = SchedulerApiFactory.UnreachableCacheConnection,
            });
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);

        using var live = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private static string? FindCheckStatus(JsonElement body, string name) =>
        body.GetProperty("checks")
            .EnumerateArray()
            .Where(check => check.GetProperty("name").GetString() == name)
            .Select(check => check.GetProperty("status").GetString())
            .FirstOrDefault();
}
