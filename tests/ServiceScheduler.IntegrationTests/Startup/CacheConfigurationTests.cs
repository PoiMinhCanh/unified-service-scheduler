using ServiceScheduler.IntegrationTests.Fixtures;
using Xunit;

namespace ServiceScheduler.IntegrationTests.Startup;

public sealed class CacheConfigurationTests
{
    [Fact]
    public void MissingCacheConnectionFailsStartup()
    {
        using var factory = new SchedulerApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Cache"] = string.Empty,
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Cache", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedCacheConnectionFailsStartup()
    {
        using var factory = new SchedulerApiFactory(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Cache"] = "127.0.0.1:not-a-port",
        });

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ConnectionStrings:Cache", exception.ToString(), StringComparison.Ordinal);
    }
}
