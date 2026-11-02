using System.Net;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Health;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task LivenessIsAvailableWhenDatabaseIsUnreachable()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ReadinessFailsWhenDatabaseIsUnreachable()
    {
        using var factory = new SchedulerApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
