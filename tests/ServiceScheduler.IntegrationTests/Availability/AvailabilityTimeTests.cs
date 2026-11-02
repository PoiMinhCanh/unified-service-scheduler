using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityTimeTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public AvailabilityTimeTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task EquivalentExplicitOffsetsReturnTheSameUtcInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var zulu = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "2026-11-03T03:00:00Z"),
            cancellationToken);
        using var offset = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "2026-11-03T10:00:00+07:00"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, zulu.StatusCode);
        Assert.Equal(HttpStatusCode.OK, offset.StatusCode);

        var zuluDocument = JsonDocument.Parse(await zulu.Content.ReadAsStringAsync(cancellationToken));
        var offsetDocument = JsonDocument.Parse(await offset.Content.ReadAsStringAsync(cancellationToken));

        Assert.Equal(
            zuluDocument.RootElement.GetProperty("startsAt").GetString(),
            offsetDocument.RootElement.GetProperty("startsAt").GetString());
        Assert.Equal(
            zuluDocument.RootElement.GetProperty("endsAt").GetString(),
            offsetDocument.RootElement.GetProperty("endsAt").GetString());
        Assert.EndsWith("Z", zuluDocument.RootElement.GetProperty("startsAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", zuluDocument.RootElement.GetProperty("endsAt").GetString(), StringComparison.Ordinal);
        Assert.EndsWith("Z", zuluDocument.RootElement.GetProperty("evaluatedAt").GetString(), StringComparison.Ordinal);
        Assert.Equal("2026-11-02T00:00:00Z", zuluDocument.RootElement.GetProperty("evaluatedAt").GetString());
        Assert.Equal("2026-11-03T04:00:00Z", zuluDocument.RootElement.GetProperty("endsAt").GetString());
    }

    [Theory]
    [InlineData("2026-11-03T10:00:00")]
    [InlineData("2026-11-03")]
    [InlineData("not-a-timestamp")]
    [InlineData("2026-11-01T00:00:00Z")]
    [InlineData("2026-11-02T00:00:00Z")]
    public async Task InvalidOrNonFutureStartsReturnValidationProblem(string startsAt)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                startsAt),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("validation_error", problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task MissingQueryParametersReturnValidationProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var missingServiceType = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/availability?startsAt=2026-11-03T03%3A00%3A00Z",
            cancellationToken);
        using var missingStartsAt = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/availability?serviceTypeId={SeedData.ServiceTypeIds.SaigonOilChange}",
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, missingServiceType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingStartsAt.StatusCode);
        Assert.Equal(
            "validation_error",
            JsonDocument.Parse(await missingServiceType.Content.ReadAsStringAsync(cancellationToken))
                .RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "validation_error",
            JsonDocument.Parse(await missingStartsAt.Content.ReadAsStringAsync(cancellationToken))
                .RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ComputedEndOverflowReturnsValidationProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "9999-12-31T23:00:00Z"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("validation_error", problem.RootElement.GetProperty("code").GetString());
    }
}
