using System.Net;
using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Health;

[Collection(SqlServerCollection.Name)]
public sealed class InitializedReadinessTests : SqlServerTestBase
{
    public InitializedReadinessTests(SqlServerContainerFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task ReadinessReportsHealthyForAnInitializedDatabaseUsingTheRuntimeIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var factory = new SchedulerApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Runtime"] = RuntimeConnectionString,
        });
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        using var ready = await client.GetAsync("/health/ready", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }
}
