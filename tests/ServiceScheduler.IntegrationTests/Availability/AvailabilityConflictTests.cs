using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityConflictTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequestStart = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequestEnd = new(2026, 11, 3, 4, 0, 0, TimeSpan.Zero);

    public AvailabilityConflictTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task TechnicianConflictAloneMakesTheRequestUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alternateBayId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        await TestData.InsertBayAsync(alternateBayId, SeedData.DealershipIds.Saigon, "Probe Conflict Bay", true, cancellationToken);
        await TestData.InsertAppointmentAsync(
            appointmentId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            alternateBayId,
            RequestStart,
            RequestEnd,
            cancellationToken);

        try
        {
            Assert.False(await RequestAvailabilityAsync(cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
            await TestData.DeleteBayAsync(alternateBayId, cancellationToken);
        }
    }

    [Fact]
    public async Task BayConflictAloneMakesTheRequestUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var technicianId = Guid.NewGuid();
        var qualificationInserted = false;
        var shiftId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        await TestData.InsertTechnicianAsync(technicianId, SeedData.DealershipIds.Saigon, "Probe Bay Conflict Technician", true, cancellationToken);
        await TestData.InsertQualificationAsync(
            technicianId,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.DealershipIds.Saigon,
            cancellationToken);
        qualificationInserted = true;
        await TestData.InsertShiftAsync(
            shiftId,
            technicianId,
            new DateTimeOffset(2026, 11, 3, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            cancellationToken);
        await TestData.InsertAppointmentAsync(
            appointmentId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            technicianId,
            SeedData.ServiceBayIds.SaigonActive,
            RequestStart,
            RequestEnd,
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
        }
    }

    [Fact]
    public async Task AFreePairDifferentFromTheBookedResourcesIsAvailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alternateBayId = Guid.NewGuid();
        var technicianId = Guid.NewGuid();
        var qualificationInserted = false;
        var shiftId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        await TestData.InsertBayAsync(alternateBayId, SeedData.DealershipIds.Saigon, "Probe Free Pair Bay", true, cancellationToken);
        await TestData.InsertTechnicianAsync(technicianId, SeedData.DealershipIds.Saigon, "Probe Free Pair Technician", true, cancellationToken);
        await TestData.InsertQualificationAsync(
            technicianId,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.DealershipIds.Saigon,
            cancellationToken);
        qualificationInserted = true;
        await TestData.InsertShiftAsync(
            shiftId,
            technicianId,
            new DateTimeOffset(2026, 11, 3, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            cancellationToken);
        await TestData.InsertAppointmentAsync(
            appointmentId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            technicianId,
            alternateBayId,
            RequestStart,
            RequestEnd,
            cancellationToken);

        try
        {
            Assert.True(await RequestAvailabilityAsync(cancellationToken));
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
    public async Task AnAppointmentEndingAtTheRequestedStartIsAllowed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appointmentId = Guid.NewGuid();
        await TestData.InsertAppointmentAsync(
            appointmentId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            RequestStart.AddMinutes(-60),
            RequestStart,
            cancellationToken);

        try
        {
            Assert.True(await RequestAvailabilityAsync(cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
        }
    }

    [Theory]
    [InlineData(2, 30, 3, 30)]
    [InlineData(2, 0, 5, 0)]
    [InlineData(3, 0, 4, 0)]
    [InlineData(3, 15, 3, 45)]
    [InlineData(2, 30, 4, 30)]
    public async Task OverlappingAppointmentsMakeTheRequestUnavailable(
        int existingStartHour,
        int existingStartMinute,
        int existingEndHour,
        int existingEndMinute)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var appointmentId = Guid.NewGuid();
        var existingStart = new DateTimeOffset(2026, 11, 3, existingStartHour, existingStartMinute, 0, TimeSpan.Zero);
        var existingEnd = new DateTimeOffset(2026, 11, 3, existingEndHour, existingEndMinute, 0, TimeSpan.Zero);

        await TestData.InsertAppointmentAsync(
            appointmentId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            existingStart,
            existingEnd,
            cancellationToken);

        try
        {
            Assert.False(await RequestAvailabilityAsync(cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
        }
    }

    private async Task<bool> RequestAvailabilityAsync(CancellationToken cancellationToken)
    {
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            AvailabilityTestUrls.For(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                "2026-11-03T03:00:00Z"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("available").GetBoolean();
    }
}
