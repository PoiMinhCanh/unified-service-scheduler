using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Startup;

/// <summary>
/// Verifies Serilog remains one provider among test-added providers and that the completion event
/// carries a route template and bounded fields without raw paths, query strings, or entity IDs.
/// </summary>
[Collection(SqlServerAndRedisCollection.Name)]
public sealed class RequestLogContractTests
{
    private static readonly Guid DealershipId = Guid.Parse("8f1c2d3e-4a5b-6c7d-8e9f-0a1b2c3d4e5f");

    [Fact]
    public async Task CompletionEventCarriesRouteStatusAndOutcomeWithoutRawRequestData()
    {
        var loggerProvider = new CapturingLoggerProvider();
        using var factory = new SchedulerApiFactory
        {
            ConfigureTestServices = services => services.AddSingleton<ILoggerProvider>(loggerProvider),
        };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            $"/api/dealerships/{DealershipId}/service-types?vin=SYNTHETICVIN123",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var entry = await WaitForCompletionAsync(
            loggerProvider,
            "api/dealerships/{dealershipId}/service-types",
            TestContext.Current.CancellationToken);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("RequestCompleted", entry.Properties["EventName"]);
        Assert.Equal("GET", entry.Properties["RequestMethod"]);
        Assert.Equal("api/dealerships/{dealershipId}/service-types", entry.Properties["RouteTemplate"]);
        Assert.Equal(503, entry.Properties["StatusCode"]);
        Assert.Equal("5xx", entry.Properties["StatusClass"]);
        Assert.Equal("dependency_unavailable", entry.Properties["Outcome"]);
        Assert.DoesNotContain(DealershipId.ToString("D"), entry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SYNTHETICVIN123", entry.Message, StringComparison.Ordinal);

        var dependencyEntry = Assert.Single(
            loggerProvider.Entries,
            candidate => candidate.Category.Contains("SqlDependencyExceptionHandler", StringComparison.Ordinal));
        Assert.Equal("DependencyUnavailable", dependencyEntry.Properties["EventName"]);
        Assert.Equal("api/dealerships/{dealershipId}/service-types", dependencyEntry.Properties["RouteTemplate"]);
        Assert.DoesNotContain(DealershipId.ToString("D"), dependencyEntry.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SYNTHETICVIN123", dependencyEntry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthProbeDoesNotEmitACompletionEvent()
    {
        var loggerProvider = new CapturingLoggerProvider();
        using var factory = new SchedulerApiFactory
        {
            ConfigureTestServices = services => services.AddSingleton<ILoggerProvider>(loggerProvider),
        };
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(
            "/health/live",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(
            loggerProvider.Entries,
            candidate => candidate.Category.Contains("RequestLoggingMiddleware", StringComparison.Ordinal));
    }

    private static async Task<LogEntry> WaitForCompletionAsync(
        CapturingLoggerProvider provider,
        string routeTemplate,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var entry = provider.Entries.FirstOrDefault(candidate =>
                candidate.Category.Contains("RequestLoggingMiddleware", StringComparison.Ordinal)
                && Equals(candidate.Properties.GetValueOrDefault("RouteTemplate"), routeTemplate));
            if (entry is not null)
            {
                return entry;
            }

            await Task.Delay(50, cancellationToken);
        }

        Assert.Fail($"No RequestCompleted event with route template '{routeTemplate}' was logged.");
        return null!;
    }
}
