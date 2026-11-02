using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class AppointmentDurationTests : CacheSqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private const string StartsAt = "2026-11-03T03:00:00Z";

    public AppointmentDurationTests(
        SqlServerContainerFixture fixture,
        RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task StaleCatalogCannotChangeBookingDurationAndRetrievalKeepsTheSnapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid? appointmentId = null;
        await FlushCacheAsync();

        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        try
        {
            using (var catalog = await client.GetAsync(
                       $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
            }

            await TestData.UpdateServiceTypeDurationAsync(
                SeedData.ServiceTypeIds.SaigonOilChange, 90, cancellationToken);

            using (var staleCatalog = await client.GetAsync(
                       $"/api/dealerships/{SeedData.DealershipIds.Saigon}/service-types",
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, staleCatalog.StatusCode);
                using var staleDocument = JsonDocument.Parse(
                    await staleCatalog.Content.ReadAsStringAsync(cancellationToken));
                var staleOilChange = staleDocument.RootElement
                    .EnumerateArray()
                    .Single(element =>
                        element.GetProperty("id").GetGuid() == SeedData.ServiceTypeIds.SaigonOilChange);
                Assert.Equal(60, staleOilChange.GetProperty("durationMinutes").GetInt32());
            }

            using (var create = await client.PostAsJsonAsync(
                       AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                       AppointmentRequests.ForSaigon(StartsAt),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.Created, create.StatusCode);
                using var document = JsonDocument.Parse(
                    await create.Content.ReadAsStringAsync(cancellationToken));
                appointmentId = document.RootElement.GetProperty("id").GetGuid();
                Assert.Equal(90, document.RootElement.GetProperty("durationMinutes").GetInt32());
                Assert.Equal(
                    "2026-11-03T04:30:00Z",
                    document.RootElement.GetProperty("endsAt").GetString());
            }

            await TestData.UpdateServiceTypeDurationAsync(
                SeedData.ServiceTypeIds.SaigonOilChange, 120, cancellationToken);

            using (var retrieve = await client.GetAsync(
                       AppointmentTestUrls.For(SeedData.DealershipIds.Saigon, appointmentId.Value),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, retrieve.StatusCode);
                using var document = JsonDocument.Parse(
                    await retrieve.Content.ReadAsStringAsync(cancellationToken));
                Assert.Equal(90, document.RootElement.GetProperty("durationMinutes").GetInt32());
                Assert.Equal(
                    "2026-11-03T04:30:00Z",
                    document.RootElement.GetProperty("endsAt").GetString());
            }

            using (var overlap = await client.GetAsync(
                       AvailabilityTestUrls.For(
                           SeedData.DealershipIds.Saigon,
                           SeedData.ServiceTypeIds.SaigonOilChange,
                           "2026-11-03T03:30:00Z"),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, overlap.StatusCode);
                using var document = JsonDocument.Parse(
                    await overlap.Content.ReadAsStringAsync(cancellationToken));
                Assert.False(document.RootElement.GetProperty("available").GetBoolean());
            }

            using (var laterSlot = await client.GetAsync(
                       AvailabilityTestUrls.For(
                           SeedData.DealershipIds.Saigon,
                           SeedData.ServiceTypeIds.SaigonOilChange,
                           "2026-11-03T05:00:00Z"),
                       cancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, laterSlot.StatusCode);
                using var document = JsonDocument.Parse(
                    await laterSlot.Content.ReadAsStringAsync(cancellationToken));
                Assert.True(document.RootElement.GetProperty("available").GetBoolean());
                Assert.Equal(
                    "2026-11-03T07:00:00Z",
                    document.RootElement.GetProperty("endsAt").GetString());
            }
        }
        finally
        {
            await TestData.UpdateServiceTypeDurationAsync(
                SeedData.ServiceTypeIds.SaigonOilChange, 60, CancellationToken.None);
            if (appointmentId is not null)
            {
                await TestData.DeleteAppointmentAsync(appointmentId.Value, CancellationToken.None);
            }

            await FlushCacheAsync();
        }
    }
}
