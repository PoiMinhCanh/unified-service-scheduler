using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class AppointmentDstTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public AppointmentDstTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task LondonSpringForwardIntervalEndingAtTheBoundaryIsAccepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var shiftId = Guid.NewGuid();
        Guid? appointmentId = null;

        try
        {
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

            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.London),
                AppointmentRequests.ForLondon("2027-03-28T01:30:00Z"),
                cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            appointmentId = document.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(
                "2027-03-28T03:00:00Z",
                document.RootElement.GetProperty("endsAt").GetString());
            Assert.Equal(90, document.RootElement.GetProperty("durationMinutes").GetInt32());
        }
        finally
        {
            if (appointmentId is not null)
            {
                await TestData.DeleteAppointmentAsync(appointmentId.Value, CancellationToken.None);
            }

            await TestData.DeleteShiftAsync(shiftId, CancellationToken.None);
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.London, 0, new TimeOnly(2, 0), CancellationToken.None);
        }
    }

    [Fact]
    public async Task LondonFallBackIntervalUsesWallClockCoverageAndRejectsTheLaterStart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var shiftId = Guid.NewGuid();
        Guid? appointmentId = null;

        try
        {
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

            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();

            using (var accepted = await client.PostAsJsonAsync(
                       AppointmentTestUrls.Collection(SeedData.DealershipIds.London),
                       AppointmentRequests.ForLondon("2027-10-31T00:30:00Z"),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
                using var document = JsonDocument.Parse(
                    await accepted.Content.ReadAsStringAsync(cancellationToken));
                appointmentId = document.RootElement.GetProperty("id").GetGuid();
                Assert.Equal(
                    "2027-10-31T02:00:00Z",
                    document.RootElement.GetProperty("endsAt").GetString());
            }

            await TestData.DeleteAppointmentAsync(appointmentId.Value, CancellationToken.None);
            appointmentId = null;

            using var rejected = await client.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.London),
                AppointmentRequests.ForLondon("2027-10-31T03:00:00Z"),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            using var problem = JsonDocument.Parse(
                await rejected.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(
                "capacity_unavailable",
                problem.RootElement.GetProperty("code").GetString());
            Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            if (appointmentId is not null)
            {
                await TestData.DeleteAppointmentAsync(appointmentId.Value, CancellationToken.None);
            }

            await TestData.DeleteShiftAsync(shiftId, CancellationToken.None);
            await TestData.DeleteOpeningHoursAsync(
                SeedData.DealershipIds.London, 0, new TimeOnly(0, 0), CancellationToken.None);
        }
    }

    [Fact]
    public async Task EquivalentExplicitOffsetsAreNormalizedToTheSameUtcInstant()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid? appointmentId = null;

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();
            using var response = await client.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                AppointmentRequests.ForSaigon("2026-11-03T10:00:00+07:00"),
                cancellationToken);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            appointmentId = document.RootElement.GetProperty("id").GetGuid();
            Assert.Equal(
                "2026-11-03T03:00:00Z",
                document.RootElement.GetProperty("startsAt").GetString());
            Assert.Equal(
                "2026-11-03T04:00:00Z",
                document.RootElement.GetProperty("endsAt").GetString());
        }
        finally
        {
            if (appointmentId is not null)
            {
                await TestData.DeleteAppointmentAsync(appointmentId.Value, CancellationToken.None);
            }
        }
    }
}
