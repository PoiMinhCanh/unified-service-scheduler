using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Startup;

public sealed class StartupValidationTests
{
    [Fact]
    public void MissingRuntimeConnectionFailsStartup()
    {
        using var factory = new SchedulerApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Runtime"] = string.Empty,
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Runtime", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedRuntimeConnectionFailsStartup()
    {
        using var factory = new SchedulerApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Runtime"] = "this-is-not-a-connection-string",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Runtime", exception.ToString(), StringComparison.Ordinal);
    }
}
