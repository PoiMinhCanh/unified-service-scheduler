using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class AppointmentApiTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private const string StartsAt = "2026-11-03T03:00:00Z";

    public AppointmentApiTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ValidCreateReturnsCompletePersistedResponseAndLocation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid appointmentId = Guid.Empty;

        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        try
        {
            using var response = await client.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                AppointmentRequests.ForSaigon(StartsAt),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.NotNull(response.Headers.Location);

            var body = await ReadJsonAsync(response, cancellationToken);
            appointmentId = body.GetProperty("id").GetGuid();
            Assert.NotEqual(Guid.Empty, appointmentId);
            Assert.Equal(SeedData.DealershipIds.Saigon, body.GetProperty("dealershipId").GetGuid());
            Assert.Equal(SeedData.CustomerIds.AnNguyen, body.GetProperty("customerId").GetGuid());
            Assert.Equal(SeedData.VehicleIds.AnNguyenVehicle, body.GetProperty("vehicleId").GetGuid());
            Assert.Equal(SeedData.ServiceTypeIds.SaigonOilChange, body.GetProperty("serviceTypeId").GetGuid());
            Assert.NotEqual(Guid.Empty, body.GetProperty("technicianId").GetGuid());
            Assert.NotEqual(Guid.Empty, body.GetProperty("serviceBayId").GetGuid());
            Assert.Equal(StartsAt, body.GetProperty("startsAt").GetString());
            Assert.Equal("2026-11-03T04:00:00Z", body.GetProperty("endsAt").GetString());
            Assert.Equal(60, body.GetProperty("durationMinutes").GetInt32());
            Assert.Equal("Confirmed", body.GetProperty("status").GetString());
            Assert.EndsWith("Z", body.GetProperty("createdAt").GetString(), StringComparison.Ordinal);

            using var retrieval = await client.GetAsync(response.Headers.Location, cancellationToken);
            Assert.Equal(HttpStatusCode.OK, retrieval.StatusCode);
            var retrieved = await ReadJsonAsync(retrieval, cancellationToken);
            AssertAppointmentEquals(body, retrieved);
        }
        finally
        {
            if (appointmentId != Guid.Empty)
            {
                await TestData.DeleteAppointmentAsync(appointmentId, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task RetrievalIsDealershipScopedAndSurvivesANewHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid appointmentId = Guid.Empty;

        using (var firstFactory = CreateFactory(new FakeTimeProvider(FixedClockUtc)))
        using (var firstClient = firstFactory.CreateClient())
        {
            using var response = await firstClient.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                AppointmentRequests.ForSaigon(StartsAt),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            appointmentId = (await ReadJsonAsync(response, cancellationToken)).GetProperty("id").GetGuid();
        }

        try
        {
            using var secondFactory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var secondClient = secondFactory.CreateClient();

            using var retrieval = await secondClient.GetAsync(
                AppointmentTestUrls.For(SeedData.DealershipIds.Saigon, appointmentId),
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, retrieval.StatusCode);
            Assert.Equal(
                appointmentId,
                (await ReadJsonAsync(retrieval, cancellationToken)).GetProperty("id").GetGuid());

            using var otherDealership = await secondClient.GetAsync(
                AppointmentTestUrls.For(SeedData.DealershipIds.London, appointmentId),
                cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, otherDealership.StatusCode);
            await AssertProblemCodeAsync(otherDealership, "not_found", cancellationToken);

            using var missing = await secondClient.GetAsync(
                AppointmentTestUrls.For(SeedData.DealershipIds.Saigon, Guid.NewGuid()),
                cancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            await AssertProblemCodeAsync(missing, "not_found", cancellationToken);
        }
        finally
        {
            if (appointmentId != Guid.Empty)
            {
                await TestData.DeleteAppointmentAsync(appointmentId, CancellationToken.None);
            }
        }
    }

    [Fact]
    public async Task RepeatedPostIsANewAttemptAndDoesNotDuplicateTheAppointment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid appointmentId = Guid.Empty;

        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        try
        {
            using var first = await client.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                AppointmentRequests.ForSaigon(StartsAt),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
            appointmentId = (await ReadJsonAsync(first, cancellationToken)).GetProperty("id").GetGuid();

            using var second = await client.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                AppointmentRequests.ForSaigon(StartsAt),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
            await AssertProblemCodeAsync(second, "capacity_unavailable", cancellationToken);

            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            if (appointmentId != Guid.Empty)
            {
                await TestData.DeleteAppointmentAsync(appointmentId, CancellationToken.None);
            }
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.Clone();
    }

    private static async Task AssertProblemCodeAsync(
        HttpResponseMessage response,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        var body = await ReadJsonAsync(response, cancellationToken);
        Assert.Equal(expectedCode, body.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("traceId").GetString()));
    }

    private static void AssertAppointmentEquals(JsonElement expected, JsonElement actual)
    {
        foreach (var property in new[]
                 {
                     "id",
                     "dealershipId",
                     "customerId",
                     "vehicleId",
                     "serviceTypeId",
                     "technicianId",
                     "serviceBayId",
                     "startsAt",
                     "endsAt",
                     "durationMinutes",
                     "status",
                     "createdAt",
                 })
        {
            Assert.Equal(expected.GetProperty(property).GetRawText(), actual.GetProperty(property).GetRawText());
        }
    }
}
