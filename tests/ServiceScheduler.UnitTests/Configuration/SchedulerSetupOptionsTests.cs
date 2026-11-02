using ServiceScheduler.Api.Infrastructure.Setup;
using Xunit;

namespace ServiceScheduler.UnitTests.Configuration;

public sealed class SchedulerSetupOptionsTests
{
    private const string ValidSetupConnection =
        "Server=127.0.0.1,1433;Database=Scheduler;User Id=sa;Password=TestOnly1!;Connect Timeout=3";

    [Fact]
    public void MissingSetupConnectionIsRejected()
    {
        var errors = SchedulerSetupOptions.Validate(null, "Runtime1!");

        Assert.Contains(
            errors,
            error => error.Contains(SchedulerSetupOptions.SetupConnectionKey, StringComparison.Ordinal));
    }

    [Fact]
    public void MalformedSetupConnectionIsRejected()
    {
        var errors = SchedulerSetupOptions.Validate("not-a-connection-string", "Runtime1!");

        Assert.Contains(
            errors,
            error => error.Contains(SchedulerSetupOptions.SetupConnectionKey, StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeIdentityCannotBeUsedForSetup()
    {
        var errors = SchedulerSetupOptions.Validate(
            "Server=127.0.0.1,1433;Database=Scheduler;User Id=scheduler_app;Password=TestOnly1!;Connect Timeout=3",
            "Runtime1!");

        Assert.Contains(
            errors,
            error => error.Contains("runtime identity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingRuntimePasswordIsRejected()
    {
        var errors = SchedulerSetupOptions.Validate(ValidSetupConnection, null);

        Assert.Contains(
            errors,
            error => error.Contains(SchedulerSetupOptions.RuntimePasswordKey, StringComparison.Ordinal));
    }

    [Fact]
    public void ShortRuntimePasswordIsRejected()
    {
        var errors = SchedulerSetupOptions.Validate(ValidSetupConnection, "short");

        Assert.Contains(
            errors,
            error => error.Contains(SchedulerSetupOptions.RuntimePasswordKey, StringComparison.Ordinal));
    }

    [Fact]
    public void ValidSetupConfigurationIsAccepted()
    {
        var errors = SchedulerSetupOptions.Validate(ValidSetupConnection, "Runtime1!");

        Assert.Empty(errors);
    }
}
