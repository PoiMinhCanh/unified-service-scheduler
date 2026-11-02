using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityEligibilityTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequestStart = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset RequestEnd = new(2026, 11, 3, 4, 0, 0, TimeSpan.Zero);

    public AvailabilityEligibilityTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task SeededQualifiedTechnicianAndActiveBayAreAvailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
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
        Assert.True(document.RootElement.GetProperty("available").GetBoolean());
        Assert.Equal("2026-11-03T04:00:00Z", document.RootElement.GetProperty("endsAt").GetString());
    }

    [Fact]
    public async Task UnqualifiedTechnicianNeverSatisfiesAvailability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alternateBayId = Guid.NewGuid();
        var unqualifiedTechnicianId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        await TestData.InsertBayAsync(
            alternateBayId,
            SeedData.DealershipIds.Saigon,
            "Probe Alternate Bay",
            true,
            cancellationToken);
        await TestData.InsertTechnicianAsync(
            unqualifiedTechnicianId,
            SeedData.DealershipIds.Saigon,
            "Probe Unqualified Technician",
            true,
            cancellationToken);
        await TestData.InsertQualificationAsync(
            unqualifiedTechnicianId,
            SeedData.ServiceTypeIds.SaigonBrakeService,
            SeedData.DealershipIds.Saigon,
            cancellationToken);
        await TestData.InsertShiftAsync(
            shiftId,
            unqualifiedTechnicianId,
            new DateTimeOffset(2026, 11, 3, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            cancellationToken);
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
            Assert.False(document.RootElement.GetProperty("available").GetBoolean());
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
            await TestData.DeleteQualificationAsync(
                unqualifiedTechnicianId,
                SeedData.ServiceTypeIds.SaigonBrakeService,
                cancellationToken);
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            await TestData.DeleteTechnicianAsync(unqualifiedTechnicianId, cancellationToken);
            await TestData.DeleteBayAsync(alternateBayId, cancellationToken);
        }
    }

    [Fact]
    public async Task InactiveTechnicianWithACoveringShiftNeverSatisfiesAvailability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alternateBayId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        await TestData.InsertBayAsync(
            alternateBayId,
            SeedData.DealershipIds.Saigon,
            "Probe Inactive Technician Bay",
            true,
            cancellationToken);
        await TestData.InsertShiftAsync(
            shiftId,
            SeedData.TechnicianIds.SaigonInactive,
            new DateTimeOffset(2026, 11, 3, 2, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            cancellationToken);
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
            Assert.False(document.RootElement.GetProperty("available").GetBoolean());
            Assert.Equal(
                SeedData.DealershipIds.Saigon,
                document.RootElement.GetProperty("dealershipId").GetGuid());
            Assert.Equal(
                SeedData.ServiceTypeIds.SaigonOilChange,
                document.RootElement.GetProperty("serviceTypeId").GetGuid());
            Assert.Equal("2026-11-03T03:00:00Z", document.RootElement.GetProperty("startsAt").GetString());
            Assert.Equal("2026-11-03T04:00:00Z", document.RootElement.GetProperty("endsAt").GetString());
            Assert.EndsWith(
                "Z",
                document.RootElement.GetProperty("evaluatedAt").GetString(),
                StringComparison.Ordinal);
            Assert.False(document.RootElement.TryGetProperty("technicianId", out _));
            Assert.False(document.RootElement.TryGetProperty("serviceBayId", out _));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
            await TestData.DeleteShiftAsync(shiftId, cancellationToken);
            await TestData.DeleteBayAsync(alternateBayId, cancellationToken);
        }
    }

    [Fact]
    public async Task InactiveBayNeverSatisfiesAvailability()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var technicianId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        await TestData.InsertTechnicianAsync(
            technicianId,
            SeedData.DealershipIds.Saigon,
            "Probe Inactive Bay Technician",
            true,
            cancellationToken);
        await TestData.InsertQualificationAsync(
            technicianId,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.DealershipIds.Saigon,
            cancellationToken);
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
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            RequestStart,
            RequestEnd,
            cancellationToken);

        try
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
            Assert.False(document.RootElement.GetProperty("available").GetBoolean());
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
        }
    }
}
