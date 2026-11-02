using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Availability;

[Collection(SqlServerCollection.Name)]
public sealed class AvailabilityScopeTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

    public AvailabilityScopeTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task UnknownDealershipReturnsNotFound()
    {
        await AssertNotFoundAsync(Guid.NewGuid(), SeedData.ServiceTypeIds.SaigonOilChange);
    }

    [Fact]
    public async Task UnknownServiceReturnsNotFound()
    {
        await AssertNotFoundAsync(SeedData.DealershipIds.Saigon, Guid.NewGuid());
    }

    [Fact]
    public async Task ServiceFromAnotherDealershipReturnsNotFound()
    {
        await AssertNotFoundAsync(SeedData.DealershipIds.Saigon, SeedData.ServiceTypeIds.LondonAnnualService);
    }

    [Fact]
    public async Task ForeignDealershipResourcesNeverSatisfyAvailability()
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
            new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 3, 4, 0, 0, TimeSpan.Zero),
            cancellationToken);

        try
        {
            Assert.False(await RequestAvailabilityAsync(
                SeedData.DealershipIds.Saigon,
                SeedData.ServiceTypeIds.SaigonOilChange,
                cancellationToken));
        }
        finally
        {
            await TestData.DeleteAppointmentAsync(appointmentId, cancellationToken);
        }
    }

    private async Task AssertNotFoundAsync(Guid dealershipId, Guid serviceTypeId)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            AvailabilityTestUrls.For(dealershipId, serviceTypeId, "2026-11-03T03:00:00Z"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal("not_found", problem.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("traceId").GetString()));
    }

    private async Task<bool> RequestAvailabilityAsync(
        Guid dealershipId,
        Guid serviceTypeId,
        CancellationToken cancellationToken)
    {
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            AvailabilityTestUrls.For(dealershipId, serviceTypeId, "2026-11-03T03:00:00Z"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("available").GetBoolean();
    }
}
