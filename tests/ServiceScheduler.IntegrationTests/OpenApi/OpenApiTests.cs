using System.Net;
using System.Text.Json;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.OpenApi;

public sealed class OpenApiTests
{
    [Fact]
    public async Task OpenApiDocumentIsAvailable()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.True(document.RootElement.TryGetProperty("openapi", out _));
        Assert.True(document.RootElement.TryGetProperty("info", out _));
        Assert.True(document.RootElement.TryGetProperty("paths", out _));
    }

    [Fact]
    public async Task SwaggerUiIsAvailableAndReferencesTheOpenApiDocument()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/index.html", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("index.js", html, StringComparison.Ordinal);

        using var initializerResponse = await client.GetAsync(
            "/swagger/index.js",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, initializerResponse.StatusCode);
        var initializer = await initializerResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("/openapi/v1.json", initializer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TicketTwoOperationsAndErrorContractsAreDocumented()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");

        var discovery = paths.GetProperty("/api/dealerships").GetProperty("get");
        Assert.Contains(
            "ordered by name",
            discovery.GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(discovery.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(discovery.GetProperty("responses").TryGetProperty("503", out _));

        var catalog = paths.GetProperty("/api/dealerships/{dealershipId}/service-types").GetProperty("get");
        Assert.Contains(
            "ordered by name",
            catalog.GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(catalog.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(catalog.GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(catalog.GetProperty("responses").TryGetProperty("503", out _));

        var availability = paths.GetProperty("/api/dealerships/{dealershipId}/availability").GetProperty("get");
        Assert.Contains(
            "advisory",
            availability.GetProperty("description").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.True(availability.GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(availability.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(availability.GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(availability.GetProperty("responses").TryGetProperty("503", out _));

        var startsAt = FindParameter(availability, "startsAt");
        Assert.NotNull(startsAt);
        Assert.Contains("%2B", startsAt.Value.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.Contains("offset", startsAt.Value.GetProperty("description").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(startsAt.Value.GetProperty("required").GetBoolean());

        var serviceTypeId = FindParameter(availability, "serviceTypeId");
        Assert.NotNull(serviceTypeId);
        Assert.True(serviceTypeId.Value.GetProperty("required").GetBoolean());

        var dealershipParameter = FindParameter(availability, "dealershipId");
        Assert.NotNull(dealershipParameter);
        Assert.True(dealershipParameter.Value.GetProperty("required").GetBoolean());

        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("DealershipResponse", out _));
        Assert.True(schemas.TryGetProperty("ServiceTypeResponse", out _));
        Assert.True(schemas.TryGetProperty("AvailabilityResponse", out _));

        var problemDetails = schemas.GetProperty("ProblemDetails");
        var properties = problemDetails.GetProperty("properties");
        Assert.True(properties.TryGetProperty("code", out _));
        Assert.True(properties.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task TicketThreeAppointmentOperationsAndCapacityContractAreDocumented()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/openapi/v1.json",
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        using var document = JsonDocument.Parse(body);
        var paths = document.RootElement.GetProperty("paths");

        var create = paths
            .GetProperty("/api/dealerships/{dealershipId}/appointments")
            .GetProperty("post");
        Assert.Contains(
            "appointment",
            create.GetProperty("summary").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Location",
            create.GetProperty("responses").GetProperty("201").GetProperty("description").GetString(),
            StringComparison.Ordinal);
        foreach (var status in new[] { "201", "400", "404", "409", "503", "500" })
        {
            Assert.True(
                create.GetProperty("responses").TryGetProperty(status, out _),
                $"Expected POST response {status}.");
        }

        Assert.Contains(
            "capacity_unavailable",
            create.GetProperty("responses").GetProperty("409").GetProperty("description").GetString(),
            StringComparison.Ordinal);

        var retrieve = paths
            .GetProperty("/api/dealerships/{dealershipId}/appointments/{appointmentId}")
            .GetProperty("get");
        var requestBody = create.GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());
        var example = requestBody.GetProperty("content").GetProperty("application/json").GetProperty("example");
        Assert.Equal(JsonValueKind.Object, example.ValueKind);
        foreach (var field in new[] { "customerId", "vehicleId", "serviceTypeId" })
        {
            Assert.NotEqual(Guid.Empty, example.GetProperty(field).GetGuid());
        }

        Assert.EndsWith("+07:00", example.GetProperty("startsAt").GetString(), StringComparison.Ordinal);
        Assert.Contains("future", requestBody.GetProperty("description").GetString(), StringComparison.Ordinal);
        foreach (var status in new[] { "200", "400", "404", "503", "500" })
        {
            Assert.True(
                retrieve.GetProperty("responses").TryGetProperty(status, out _),
                $"Expected GET response {status}.");
        }

        Assert.False(retrieve.GetProperty("responses").TryGetProperty("409", out _));

        var dealershipParameter = FindParameter(create, "dealershipId");
        Assert.NotNull(dealershipParameter);
        Assert.True(dealershipParameter.Value.GetProperty("required").GetBoolean());
        var appointmentParameter = FindParameter(retrieve, "appointmentId");
        Assert.NotNull(appointmentParameter);
        Assert.True(appointmentParameter.Value.GetProperty("required").GetBoolean());

        var schemas = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("CreateAppointmentRequest", out _));
        Assert.True(schemas.TryGetProperty("AppointmentResponse", out _));
        Assert.Contains(
            "capacity_unavailable",
            schemas
                .GetProperty("ProblemDetails")
                .GetProperty("properties")
                .GetProperty("code")
                .GetProperty("description")
                .GetString(),
            StringComparison.Ordinal);
    }

    private static JsonElement? FindParameter(JsonElement operation, string name)
    {
        foreach (var parameter in operation.GetProperty("parameters").EnumerateArray())
        {
            if (string.Equals(parameter.GetProperty("name").GetString(), name, StringComparison.Ordinal))
            {
                return parameter;
            }
        }

        return null;
    }
}
