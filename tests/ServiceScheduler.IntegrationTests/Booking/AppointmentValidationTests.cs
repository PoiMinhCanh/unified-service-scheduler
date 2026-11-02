using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Booking;

[Collection(SqlServerCollection.Name)]
public sealed class AppointmentValidationTests : SqlServerTestBase
{
    private static readonly DateTimeOffset FixedClockUtc = new(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
    private const string StartsAt = "2026-11-03T03:00:00Z";

    public AppointmentValidationTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task MalformedBodyAndMissingFieldsReturnValidationProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();
        var url = AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon);

        using (var emptyBody = await client.PostAsync(
                   url,
                   new StringContent(string.Empty, Encoding.UTF8, "application/json"),
                   cancellationToken))
        {
            await AssertProblemAsync(
                emptyBody, HttpStatusCode.BadRequest, "validation_error", cancellationToken);
        }

        using (var emptyObject = await client.PostAsync(
                   url,
                   new StringContent("{}", Encoding.UTF8, "application/json"),
                   cancellationToken))
        {
            await AssertProblemAsync(
                emptyObject, HttpStatusCode.BadRequest, "validation_error", cancellationToken);
        }

        using (var malformedJson = await client.PostAsync(
                   url,
                   new StringContent("{", Encoding.UTF8, "application/json"),
                   cancellationToken))
        {
            await AssertProblemAsync(
                malformedJson, HttpStatusCode.BadRequest, "validation_error", cancellationToken);
        }

        var malformedUuid = AppointmentRequests.ForSaigon(StartsAt);
        malformedUuid["customerId"] = "not-a-guid";
        using (var response = await client.PostAsJsonAsync(url, malformedUuid, cancellationToken))
        {
            await AssertProblemAsync(
                response, HttpStatusCode.BadRequest, "validation_error", cancellationToken);
        }

        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Theory]
    [InlineData("2026-11-03T10:00:00")]          // offset-free
    [InlineData("2026-11-03")]                   // date-only
    [InlineData("not-a-date")]                   // unparseable
    [InlineData("2026-11-01T03:00:00Z")]         // past
    [InlineData("2026-11-02T00:00:00Z")]         // equal to the fixed clock
    [InlineData("9999-12-31T23:59:59Z")]         // checked-end overflow
    public async Task InvalidStartsAtValuesReturnValidationProblem(string startsAt)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.ForSaigon(startsAt),
            cancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.BadRequest, "validation_error", cancellationToken);
        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task UnknownEntitiesAndCrossDealershipServicesReturnNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using (var unknownDealership = await client.PostAsJsonAsync(
                   AppointmentTestUrls.Collection(Guid.NewGuid()),
                   AppointmentRequests.ForSaigon(StartsAt),
                   cancellationToken))
        {
            await AssertProblemAsync(
                unknownDealership, HttpStatusCode.NotFound, "not_found", cancellationToken);
        }

        using (var unknownCustomer = await client.PostAsJsonAsync(
                   AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                   AppointmentRequests.Create(
                       Guid.NewGuid(),
                       SeedData.VehicleIds.AnNguyenVehicle,
                       SeedData.ServiceTypeIds.SaigonOilChange,
                       StartsAt),
                   cancellationToken))
        {
            await AssertProblemAsync(
                unknownCustomer, HttpStatusCode.NotFound, "not_found", cancellationToken);
        }

        using (var unknownVehicle = await client.PostAsJsonAsync(
                   AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                   AppointmentRequests.Create(
                       SeedData.CustomerIds.AnNguyen,
                       Guid.NewGuid(),
                       SeedData.ServiceTypeIds.SaigonOilChange,
                       StartsAt),
                   cancellationToken))
        {
            await AssertProblemAsync(
                unknownVehicle, HttpStatusCode.NotFound, "not_found", cancellationToken);
        }

        using (var unknownService = await client.PostAsJsonAsync(
                   AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                   AppointmentRequests.Create(
                       SeedData.CustomerIds.AnNguyen,
                       SeedData.VehicleIds.AnNguyenVehicle,
                       Guid.NewGuid(),
                       StartsAt),
                   cancellationToken))
        {
            await AssertProblemAsync(
                unknownService, HttpStatusCode.NotFound, "not_found", cancellationToken);
        }

        using (var crossDealership = await client.PostAsJsonAsync(
                   AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                   AppointmentRequests.ForLondon(StartsAt),
                   cancellationToken))
        {
            await AssertProblemAsync(
                crossDealership, HttpStatusCode.NotFound, "not_found", cancellationToken);
        }

        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task OwnershipMismatchReturnsValidationProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
            AppointmentRequests.Create(
                SeedData.CustomerIds.OliverSmith,
                SeedData.VehicleIds.AnNguyenVehicle,
                SeedData.ServiceTypeIds.SaigonOilChange,
                StartsAt),
            cancellationToken);

        await AssertProblemAsync(
            response, HttpStatusCode.BadRequest, "validation_error", cancellationToken);
        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    [Fact]
    public async Task MalformedRouteGuidsReturnValidationProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = CreateFactory(new FakeTimeProvider(FixedClockUtc));
        using var client = factory.CreateClient();

        using (var create = await client.PostAsJsonAsync(
                   "/api/dealerships/not-a-guid/appointments",
                   AppointmentRequests.ForSaigon(StartsAt),
                   cancellationToken))
        {
            await AssertProblemAsync(
                create, HttpStatusCode.BadRequest, "validation_error", cancellationToken);
        }

        using (var retrieve = await client.GetAsync(
                   $"/api/dealerships/{SeedData.DealershipIds.Saigon}/appointments/not-a-guid",
                   cancellationToken))
        {
            await AssertProblemAsync(
                retrieve, HttpStatusCode.BadRequest, "validation_error", cancellationToken);
        }

        Assert.Equal(0, await TestData.CountAppointmentsAsync(cancellationToken));
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedCode,
        CancellationToken cancellationToken)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            document.RootElement.GetProperty("traceId").GetString()));
    }
}
