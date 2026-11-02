using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerAndRedisCollection.Name)]
public sealed class BookingOutageTests : CacheSqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private const string StartsAt = "2026-11-03T03:00:00Z";

    public BookingOutageTests(
        SqlServerContainerFixture fixture,
        RedisContainerFixture redisFixture)
        : base(fixture, redisFixture)
    {
    }

    [Fact]
    public async Task BookingAndRetrievalRemainCorrectDuringARedisOutage()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Guid? appointmentId = null;
        await FlushCacheAsync();
        await PauseCacheAsync();

        try
        {
            using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
            using var client = factory.CreateClient();

            using var create = await client.PostAsJsonAsync(
                AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                AppointmentRequests.ForSaigon(StartsAt),
                cancellationToken);
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            using var document = JsonDocument.Parse(
                await create.Content.ReadAsStringAsync(cancellationToken));
            appointmentId = document.RootElement.GetProperty("id").GetGuid();

            using var retrieve = await client.GetAsync(
                AppointmentTestUrls.For(SeedData.DealershipIds.Saigon, appointmentId.Value),
                cancellationToken);
            Assert.Equal(HttpStatusCode.OK, retrieve.StatusCode);
        }
        finally
        {
            await UnpauseCacheAsync();
            if (appointmentId is not null)
            {
                await TestData.DeleteAppointmentAsync(appointmentId.Value, CancellationToken.None);
            }

            await FlushCacheAsync();
        }
    }

    [Fact]
    public async Task SqlOutagePreventsConfirmationAndFailsReadiness()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(
            new FakeTimeProvider(FixedClockUtc),
            overrides: new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Runtime"] = SchedulerApiFactory.UnreachableRuntimeConnection,
            });
        using var client = factory.CreateClient();

        using var create = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(StartsAt),
            cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
        using var problem = JsonDocument.Parse(
            await create.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            "dependency_unavailable",
            problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            problem.RootElement.GetProperty("traceId").GetString()));

        using var readiness = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readiness.StatusCode);

        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }
}
