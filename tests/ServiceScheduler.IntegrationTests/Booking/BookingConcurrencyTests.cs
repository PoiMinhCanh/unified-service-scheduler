using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class BookingConcurrencyTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private const string StartsAt = "2026-11-03T03:00:00Z";

    public BookingConcurrencyTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task TwentyCoordinatedPostsProduceOneConfirmationAndNineteenConflicts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var firstFactory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var secondFactory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        var clients = new List<HttpClient>(20);

        try
        {
            for (var index = 0; index < 20; index++)
            {
                clients.Add((index % 2 == 0 ? firstFactory : secondFactory).CreateClient());
            }

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = clients.Select(async client =>
            {
                await gate.Task.ConfigureAwait(false);
                using var response = await client.PostAsJsonAsync(
                    AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                    AppointmentRequests.ForSaigon(StartsAt),
                    cancellationToken).ConfigureAwait(false);
                return response.StatusCode;
            }).ToArray();

            gate.SetResult();
            var statuses = await Task.WhenAll(requests);

            Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
            Assert.Equal(19, statuses.Count(status => status == HttpStatusCode.Conflict));
            Assert.DoesNotContain(statuses, status => status == HttpStatusCode.ServiceUnavailable);
            Assert.DoesNotContain(statuses, status => status == HttpStatusCode.InternalServerError);
            Assert.Equal(1, await TestData.CountAppointmentsAsync(cancellationToken));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }

            await DeleteAllAppointmentsAsync(cancellationToken);
        }
    }

    private async Task DeleteAllAppointmentsAsync(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<SchedulerDbContext>()
            .UseSqlServer(SetupConnectionString)
            .Options;
        await using var dbContext = new SchedulerDbContext(options);
        var appointmentIds = await dbContext.Appointments
            .AsNoTracking()
            .Select(appointment => appointment.Id)
            .ToListAsync(cancellationToken);

        foreach (var appointmentId in appointmentIds)
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
        }
    }
}
