using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class AppointmentEligibilityTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private const string StartsAt = "2026-11-03T03:00:00Z";

    public AppointmentEligibilityTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task InactiveTechnicianReturnsCapacityUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await TestData.SetTechnicianActiveAsync(
                SeedData.TechnicianIds.SaigonActive, false, cancellationToken);
            await AssertCapacityAsync(cancellationToken);
        }
        finally
        {
            await TestData.SetTechnicianActiveAsync(
                SeedData.TechnicianIds.SaigonActive, true, CancellationToken.None);
        }
    }

    [Fact]
    public async Task InactiveBayReturnsCapacityUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await TestData.SetBayActiveAsync(
                SeedData.ServiceBayIds.SaigonActive, false, cancellationToken);
            await AssertCapacityAsync(cancellationToken);
        }
        finally
        {
            await TestData.SetBayActiveAsync(
                SeedData.ServiceBayIds.SaigonActive, true, CancellationToken.None);
        }
    }

    [Fact]
    public async Task UnqualifiedTechnicianReturnsCapacityUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        try
        {
            await TestData.DeleteQualificationAsync(
                SeedData.TechnicianIds.SaigonActive,
                SeedData.ServiceTypeIds.SaigonOilChange,
                cancellationToken);
            await AssertCapacityAsync(cancellationToken);
        }
        finally
        {
            await TestData.InsertQualificationAsync(
                SeedData.TechnicianIds.SaigonActive,
                SeedData.ServiceTypeIds.SaigonOilChange,
                SeedData.DealershipIds.Saigon,
                CancellationToken.None);
        }
    }

    [Fact]
    public async Task PartialShiftCoverageReturnsCapacityUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeTechnicianId = Guid.NewGuid();
        var probeShiftId = Guid.NewGuid();

        try
        {
            await TestData.SetTechnicianActiveAsync(
                SeedData.TechnicianIds.SaigonActive, false, cancellationToken);
            await TestData.InsertTechnicianAsync(
                probeTechnicianId,
                SeedData.DealershipIds.Saigon,
                "Probe Partial Shift Technician",
                true,
                cancellationToken);
            await TestData.InsertQualificationAsync(
                probeTechnicianId,
                SeedData.ServiceTypeIds.SaigonOilChange,
                SeedData.DealershipIds.Saigon,
                cancellationToken);
            await TestData.InsertShiftAsync(
                probeShiftId,
                probeTechnicianId,
                new DateTimeOffset(2026, 11, 3, 2, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero),
                cancellationToken);

            await AssertCapacityAsync(cancellationToken);
        }
        finally
        {
            await TestData.SetTechnicianActiveAsync(
                SeedData.TechnicianIds.SaigonActive, true, CancellationToken.None);
            await TestData.DeleteShiftAsync(probeShiftId, CancellationToken.None);
            await TestData.DeleteQualificationAsync(
                probeTechnicianId,
                SeedData.ServiceTypeIds.SaigonOilChange,
                CancellationToken.None);
            await TestData.DeleteTechnicianAsync(probeTechnicianId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task PartialOpeningCoverageReturnsCapacityUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await TestData.DeleteOpeningHoursAsync(
            SeedData.DealershipIds.Saigon, 2, new TimeOnly(9, 0), cancellationToken);
        try
        {
            await AssertCapacityAsync(cancellationToken);
        }
        finally
        {
            await TestData.InsertOpeningHoursAsync(
                SeedData.DealershipIds.Saigon,
                2,
                new TimeOnly(9, 0),
                new TimeOnly(17, 0),
                CancellationToken.None);
        }
    }

    private async Task AssertCapacityAsync(CancellationToken cancellationToken)
    {
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(StartsAt),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("capacity_unavailable", document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("traceId").GetString()));
        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }
}
