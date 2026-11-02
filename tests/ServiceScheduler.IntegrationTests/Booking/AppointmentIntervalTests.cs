using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class AppointmentIntervalTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExistingStart = new(2026, 11, 3, 3, 0, 0, TimeSpan.Zero);

    public AppointmentIntervalTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task AdjacentIntervalsSucceedAndOverlapsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var existingId = Guid.NewGuid();
        Guid? beforeId = null;
        Guid? afterId = null;

        await TestData.InsertAppointmentAsync(
            existingId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            ExistingStart,
            ExistingStart.AddMinutes(60),
            cancellationToken);

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();

            using (var before = await client.PostAsJsonAsync(
                       AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                       AppointmentRequests.ForSaigon("2026-11-03T02:00:00Z"),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.Created, before.StatusCode);
                beforeId = (await ReadIdAsync(before, cancellationToken));
            }

            using (var after = await client.PostAsJsonAsync(
                       AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                       AppointmentRequests.ForSaigon("2026-11-03T04:00:00Z"),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.Created, after.StatusCode);
                afterId = await ReadIdAsync(after, cancellationToken);
            }

            await AssertConflictAsync(client, "2026-11-03T03:30:00Z", cancellationToken);
            await AssertConflictAsync(client, "2026-11-03T03:15:00Z", cancellationToken);
            await AssertConflictAsync(client, "2026-11-03T03:00:00Z", cancellationToken);

            Assert.Equal(3, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            if (beforeId is not null)
            {
                await TestData.DeleteAppointmentAsync(beforeId.Value, CancellationToken.None);
            }

            if (afterId is not null)
            {
                await TestData.DeleteAppointmentAsync(afterId.Value, CancellationToken.None);
            }

            await TestData.DeleteAppointmentAsync(existingId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task TechnicianOnlyConflictReturnsCapacityUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var alternateBayId = Guid.NewGuid();
        var existingId = Guid.NewGuid();

        await TestData.InsertBayAsync(
            alternateBayId,
            SeedData.DealershipIds.Saigon,
            "Probe Alternate Bay",
            true,
            cancellationToken);
        await TestData.InsertAppointmentAsync(
            existingId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            alternateBayId,
            ExistingStart,
            ExistingStart.AddMinutes(60),
            cancellationToken);

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();

            await AssertConflictAsync(client, "2026-11-03T03:00:00Z", cancellationToken);
            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(existingId, CancellationToken.None);
            await TestData.DeleteBayAsync(alternateBayId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task BayOnlyConflictReturnsCapacityUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeTechnicianId = Guid.NewGuid();
        var probeShiftId = Guid.NewGuid();
        var existingId = Guid.NewGuid();

        await TestData.InsertTechnicianAsync(
            probeTechnicianId,
            SeedData.DealershipIds.Saigon,
            "Probe Technician",
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
            new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            cancellationToken);
        await TestData.InsertAppointmentAsync(
            existingId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            probeTechnicianId,
            SeedData.ServiceBayIds.SaigonActive,
            ExistingStart,
            ExistingStart.AddMinutes(60),
            cancellationToken);

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();

            await AssertConflictAsync(client, "2026-11-03T03:00:00Z", cancellationToken);
            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(existingId, CancellationToken.None);
            await TestData.DeleteShiftAsync(probeShiftId, CancellationToken.None);
            await TestData.DeleteQualificationAsync(
                probeTechnicianId,
                SeedData.ServiceTypeIds.SaigonOilChange,
                CancellationToken.None);
            await TestData.DeleteTechnicianAsync(probeTechnicianId, CancellationToken.None);
        }
    }

    [Fact]
    public async Task AlternativePairIsSelectedWhenTheFirstPairIsBlocked()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeTechnicianId = Guid.NewGuid();
        var probeShiftId = Guid.NewGuid();
        var probeBayId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        Guid? createdId = null;

        await TestData.InsertTechnicianAsync(
            probeTechnicianId,
            SeedData.DealershipIds.Saigon,
            "Zed Technician",
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
            new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero),
            cancellationToken);
        await TestData.InsertBayAsync(
            probeBayId,
            SeedData.DealershipIds.Saigon,
            "Zed Bay",
            true,
            cancellationToken);
        await TestData.InsertAppointmentAsync(
            existingId,
            SeedData.DealershipIds.Saigon,
            SeedData.CustomerIds.AnNguyen,
            SeedData.VehicleIds.AnNguyenVehicle,
            SeedData.ServiceTypeIds.SaigonOilChange,
            SeedData.TechnicianIds.SaigonActive,
            SeedData.ServiceBayIds.SaigonActive,
            ExistingStart,
            ExistingStart.AddMinutes(60),
            cancellationToken);

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();

            using var response = await client.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                AppointmentRequests.ForSaigon("2026-11-03T03:00:00Z"),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            createdId = document.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(probeTechnicianId, document.RootElement.GetProperty("technicianId").GetGuid());
            Assert.Equal(probeBayId, document.RootElement.GetProperty("serviceBayId").GetGuid());
            Assert.Equal(2, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            if (createdId is not null)
            {
                await TestData.DeleteAppointmentAsync(createdId.Value, CancellationToken.None);
            }

            await TestData.DeleteAppointmentAsync(existingId, CancellationToken.None);
            await TestData.DeleteShiftAsync(probeShiftId, CancellationToken.None);
            await TestData.DeleteQualificationAsync(
                probeTechnicianId,
                SeedData.ServiceTypeIds.SaigonOilChange,
                CancellationToken.None);
            await TestData.DeleteTechnicianAsync(probeTechnicianId, CancellationToken.None);
            await TestData.DeleteBayAsync(probeBayId, CancellationToken.None);
        }
    }

    private static async Task<Guid> ReadIdAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private async Task AssertConflictAsync(
        HttpClient client,
        string startsAt,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(startsAt),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("capacity_unavailable", document.RootElement.GetProperty("code").GetString());
    }
}
