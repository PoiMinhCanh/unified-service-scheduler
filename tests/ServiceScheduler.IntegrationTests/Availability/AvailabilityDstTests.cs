using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityDstTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public AvailabilityDstTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task SpringForwardAfterTheGapUsesWallClockOpeningComparisons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var shiftId = Guid.NewGuid();
        await TestData.InsertOpeningHoursAsync(
            SeedData.DealershipIds.London,
            0,
            new TimeOnly(2, 0),
            new TimeOnly(4, 0),
            cancellationToken);
        await TestData.InsertShiftAsync(
            shiftId,
            SeedData.TechnicianIds.LondonActive,
            new DateTimeOffset(2027, 3, 28, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 3, 28, 3, 0, 0, TimeSpan.Zero),
            cancellationToken);

        try
        {
            Assert.True(await RequestAvailabilityAsync(cancellationToken, "2027-03-28T01:30:00Z"));
        }
        finally
        {
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.London,
                0,
                new TimeOnly(2, 0),
                cancellationToken);
        }
    }

    [Fact]
    public async Task NonexistentSpringForwardOpeningBoundaryIsComparedAsStoredWallClock()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var shiftId = Guid.NewGuid();
        await TestData.InsertOpeningHoursAsync(
            SeedData.DealershipIds.London,
            0,
            new TimeOnly(1, 0),
            new TimeOnly(4, 0),
            cancellationToken);
        await TestData.InsertShiftAsync(
            shiftId,
            SeedData.TechnicianIds.LondonActive,
            new DateTimeOffset(2027, 3, 28, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 3, 28, 3, 0, 0, TimeSpan.Zero),
            cancellationToken);

        try
        {
            Assert.True(await RequestAvailabilityAsync(cancellationToken, "2027-03-28T01:30:00Z"));
        }
        finally
        {
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.London,
                0,
                new TimeOnly(1, 0),
                cancellationToken);
        }
    }

    [Fact]
    public async Task FallBackRepeatedHourIsAvailableOnlyWhileTheShiftCoversTheUtcInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var shiftId = Guid.NewGuid();
        await TestData.InsertOpeningHoursAsync(
            SeedData.DealershipIds.London,
            0,
            new TimeOnly(0, 0),
            new TimeOnly(4, 0),
            cancellationToken);
        await TestData.InsertShiftAsync(
            shiftId,
            SeedData.TechnicianIds.LondonActive,
            new DateTimeOffset(2027, 10, 31, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 10, 31, 2, 0, 0, TimeSpan.Zero),
            cancellationToken);

        try
        {
            Assert.True(await RequestAvailabilityAsync(cancellationToken, "2027-10-31T00:30:00Z"));
            Assert.False(await RequestAvailabilityAsync(cancellationToken, "2027-10-31T03:00:00Z"));
        }
        finally
        {
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.London,
                0,
                new TimeOnly(0, 0),
                cancellationToken);
        }
    }

    private async Task<bool> RequestAvailabilityAsync(
        CancellationToken cancellationToken,
        string startsAt)
    {
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.London,
                SeedData.ServiceTypeIds.LondonAnnualService,
                startsAt),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("available").GetBoolean();
    }
}
