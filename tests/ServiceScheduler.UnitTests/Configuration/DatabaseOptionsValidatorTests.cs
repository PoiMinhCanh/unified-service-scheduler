using ServiceScheduler.Api.Infrastructure.Configuration;
using Xunit;

namespace ServiceScheduler.UnitTests.Configuration;

public sealed class DatabaseOptionsValidatorTests
{
    private readonly DatabaseOptionsValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingRuntimeConnectionIsRejected(string? runtime)
    {
        var result = _validator.Validate(null, new DatabaseOptions { Runtime = runtime });

        Assert.True(result.Failed);
        Assert.Contains(DatabaseOptionsValidator.RuntimeKey, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedConnectionStringIsRejected()
    {
        var result = _validator.Validate(null, new DatabaseOptions { Runtime = "not-a-connection-string" });

        Assert.True(result.Failed);
        Assert.Contains(DatabaseOptionsValidator.RuntimeKey, result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Database=Scheduler;User Id=scheduler_app;Password=TestOnly1!;Connect Timeout=3", "server")]
    [InlineData("Server=127.0.0.1,1433;User Id=scheduler_app;Password=TestOnly1!;Connect Timeout=3", "database")]
    [InlineData("Server=127.0.0.1,1433;Database=Scheduler;Password=TestOnly1!;Connect Timeout=3", "SQL user")]
    [InlineData("Server=127.0.0.1,1433;Database=Scheduler;User Id=scheduler_app;Connect Timeout=3", "password")]
    public void MissingConnectionComponentIsRejected(string runtime, string expectedText)
    {
        var result = _validator.Validate(null, new DatabaseOptions { Runtime = runtime });

        Assert.True(result.Failed);
        Assert.Contains(expectedText, result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrivilegedSaIdentityIsRejected()
    {
        var result = _validator.Validate(null, new DatabaseOptions
        {
            Runtime = "Server=127.0.0.1,1433;Database=Scheduler;User Id=sa;Password=TestOnly1!;Connect Timeout=3",
        });

        Assert.True(result.Failed);
        Assert.Contains("sa", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void InfiniteConnectTimeoutIsRejected()
    {
        var result = _validator.Validate(null, new DatabaseOptions
        {
            Runtime = "Server=127.0.0.1,1433;Database=Scheduler;User Id=scheduler_app;Password=TestOnly1!;Connect Timeout=0",
        });

        Assert.True(result.Failed);
        Assert.Contains("timeout", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidRuntimeConnectionIsAcceptedWithoutOpeningAConnection()
    {
        var result = _validator.Validate(null, new DatabaseOptions
        {
            Runtime = "Server=127.0.0.1,1;Database=Scheduler;User Id=scheduler_app;Password=TestOnly1!;Encrypt=True;TrustServerCertificate=True;Connect Timeout=1",
        });

        Assert.True(result.Succeeded);
    }
}
