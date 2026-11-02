using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ServiceScheduler.Api.Infrastructure.Persistence.Configurations;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Startup;

public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task UnknownRouteReturnsProblemDetailsWithCodeAndTraceId()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/unknown-route", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal(404, document.RootElement.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("title").GetString()));
        Assert.Equal("not_found", document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("StackTrace", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingAvailabilityParametersReturnValidationProblem()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var missingServiceType = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/availability?startsAt=2026-11-03T03%3A00%3A00Z",
            TestContext.Current.CancellationToken);
        using var missingStartsAt = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/availability?serviceTypeId={SeedData.ServiceTypeIds.SaigonOilChange}",
            TestContext.Current.CancellationToken);

        await AssertValidationProblemAsync(missingServiceType);
        await AssertValidationProblemAsync(missingStartsAt);
    }

    [Fact]
    public async Task MalformedIdentifiersReturnValidationProblem()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var malformedDealership = await client.GetAsync(
            "/api/dealerships/not-a-guid/service-types",
            TestContext.Current.CancellationToken);
        using var malformedServiceType = await client.GetAsync(
            $"/api/dealerships/{SeedData.DealershipIds.Saigon}/availability" +
            "?serviceTypeId=not-a-guid&startsAt=2026-11-03T03%3A00%3A00Z",
            TestContext.Current.CancellationToken);

        await AssertValidationProblemAsync(malformedDealership);
        await AssertValidationProblemAsync(malformedServiceType);
    }

    [Fact]
    public async Task AppointmentValidationAndDependencyFailuresUseProblemDetails()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();
        var cancellationToken = TestContext.Current.CancellationToken;

        using (var emptyBody = await client.PostAsync(
                   AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                   new StringContent(string.Empty, System.Text.Encoding.UTF8, "application/json"),
                   cancellationToken))
        {
            await AssertValidationProblemAsync(emptyBody);
        }

        using (var malformedRoute = await client.GetAsync(
                   $"/api/dealerships/{SeedData.DealershipIds.Saigon}/appointments/not-a-guid",
                   cancellationToken))
        {
            await AssertValidationProblemAsync(malformedRoute);
        }

        using (var dependency = await client.PostAsJsonAsync(
                   AppointmentTestUrls.Collection(SeedData.DealershipIds.Saigon),
                   AppointmentRequests.ForSaigon("2026-11-03T03:00:00Z"),
                   cancellationToken))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, dependency.StatusCode);
            Assert.Equal(
                "application/problem+json",
                dependency.Content.Headers.ContentType?.MediaType);
            var document = JsonDocument.Parse(
                await dependency.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal(
                "dependency_unavailable",
                document.RootElement.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(
                document.RootElement.GetProperty("traceId").GetString()));
            Assert.DoesNotContain(
                "StackTrace",
                document.RootElement.ToString(),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "Password",
                document.RootElement.ToString(),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task AssertValidationProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("validation_error", document.RootElement.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("StackTrace", document.RootElement.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
