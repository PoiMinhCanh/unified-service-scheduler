using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityCoverageTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public AvailabilityCoverageTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PartialShiftDoesNotCoverTheFullInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alternateBayId = Guid.NewGuid();
        var technicianId = Guid.NewGuid();
        var qualificationInserted = false;
        var shiftId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        await TestData.InsertBayAsync(alternateBayId, SeedData.DealershipIds.Saigon, "Probe Shift Bay", true, cancellationToken);
        await TestData.InsertTechnicianAsync(technicianId, SeedData.DealershipIds.Saigon, "Probe Partial Shift", true, cancellationToken);
        await TestData.InsertQualificationAsync(
            technicianId,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.DealershipIds.Saigon,
            cancellationToken);
        qualificationInserted = true;
        await TestData.InsertShiftAsync(
            shiftId,
            technicianId,
            new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero),
            cancellationToken);
        await TestData.InsertAppointmentAsync(
            appointmentId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            alternateBayId,
            new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero),
            cancellationToken);

        try
        {
            Assert.False(await RequestAvailabilityAsync(cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            if (qualificationInserted)
            {
                await TestData.DeleteQualificationAsync(
                    technicianId,
                    SeedData.ServiceTypeIds.SaigonOilChange,
                    cancellationToken);
            }

            await TestData.DeleteTechnicianAsync(technicianId, cancellationToken);
            await TestData.DeleteBayAsync(alternateBayId, cancellationToken);
        }
    }

    [Fact]
    public async Task PartialOpeningHoursDoNotCoverTheFullInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var technicianId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var qualificationInserted = false;

        await TestData.InsertTechnicianAsync(technicianId, SeedData.DealershipIds.Saigon, "Probe Sunday Technician", true, cancellationToken);
        await TestData.InsertQualificationAsync(
            technicianId,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.DealershipIds.Saigon,
            cancellationToken);
        qualificationInserted = true;
        await TestData.InsertShiftAsync(
            shiftId,
            technicianId,
            new DateTimeOffset(2026, 11, 8, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero),
            cancellationToken);
        await TestData.InsertOpeningHoursAsync(
            SeedData.DealershipIds.Saigon,
            0,
            new TimeOnly(9, 0),
            new TimeOnly(10, 30),
            cancellationToken);

        try
        {
            Assert.False(await RequestAvailabilityAsync(cancellationToken, "2026-11-08T03:00:00Z"));
        }
        finally
        {
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.Saigon,
                0,
                new TimeOnly(9, 0),
                cancellationToken);
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            if (qualificationInserted)
            {
                await TestData.DeleteQualificationAsync(
                    technicianId,
                    SeedData.ServiceTypeIds.SaigonOilChange,
                    cancellationToken);
            }

            await TestData.DeleteTechnicianAsync(technicianId, cancellationToken);
        }
    }

    [Fact]
    public async Task AdjacentOpeningRowsAreNotCombined()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var technicianId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var qualificationInserted = false;

        await TestData.InsertTechnicianAsync(technicianId, SeedData.DealershipIds.Saigon, "Probe Adjacent Technician", true, cancellationToken);
        await TestData.InsertQualificationAsync(
            technicianId,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.DealershipIds.Saigon,
            cancellationToken);
        qualificationInserted = true;
        await TestData.InsertShiftAsync(
            shiftId,
            technicianId,
            new DateTimeOffset(2026, 11, 8, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero),
            cancellationToken);
        await TestData.InsertOpeningHoursAsync(
            SeedData.DealershipIds.Saigon,
            0,
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            cancellationToken);
        await TestData.InsertOpeningHoursAsync(
            SeedData.DealershipIds.Saigon,
            0,
            new TimeOnly(10, 0),
            new TimeOnly(11, 0),
            cancellationToken);

        try
        {
            Assert.False(await RequestAvailabilityAsync(cancellationToken, "2026-11-08T02:30:00Z"));
        }
        finally
        {
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.Saigon,
                0,
                new TimeOnly(9, 0),
                cancellationToken);
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.Saigon,
                0,
                new TimeOnly(10, 0),
                cancellationToken);
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            if (qualificationInserted)
            {
                await TestData.DeleteQualificationAsync(
                    technicianId,
                    SeedData.ServiceTypeIds.SaigonOilChange,
                    cancellationToken);
            }

            await TestData.DeleteTechnicianAsync(technicianId, cancellationToken);
        }
    }

    [Fact]
    public async Task ShiftStartingAfterTheRequestStartDoesNotCoverTheInterval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alternateBayId = Guid.NewGuid();
        var technicianId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        await TestData.InsertBayAsync(alternateBayId, SeedData.DealershipIds.Saigon, "Probe Shift Start Bay", true, cancellationToken);
        await TestData.InsertTechnicianAsync(technicianId, SeedData.DealershipIds.Saigon, "Probe Shift Start Technician", true, cancellationToken);
        await TestData.InsertQualificationAsync(
            technicianId,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.DealershipIds.Saigon,
            cancellationToken);
        await TestData.InsertShiftAsync(
            shiftId,
            technicianId,
            new DateTimeOffset(2026, 11, 3, 3, 30, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 5, 0, 0, TimeSpan.Zero),
            cancellationToken);
        await TestData.InsertAppointmentAsync(
            appointmentId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            alternateBayId,
            new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero),
            cancellationToken);

        try
        {
            Assert.False(await RequestAvailabilityAsync(cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
            await TestData.DeleteQualificationAsync(
                technicianId,
                SeedData.ServiceTypeIds.SaigonOilChange,
                cancellationToken);
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            await TestData.DeleteTechnicianAsync(technicianId, cancellationToken);
            await TestData.DeleteBayAsync(alternateBayId, cancellationToken);
        }
    }

    private async Task<bool> RequestAvailabilityAsync(
        CancellationToken cancellationToken,
        string startsAt = "2026-11-03T03:00:00Z")
    {
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                startsAt),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("available").GetBoolean();
    }
}
