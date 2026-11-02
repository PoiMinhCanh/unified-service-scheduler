using ServiceScheduler.Api.Infrastructure.Telemetry;
using Xunit;

namespace ServiceScheduler.UnitTests.Telemetry;

public sealed class TelemetryOptionsValidatorTests
{
    private readonly TelemetryOptionsValidator _validator = new();

    [Fact]
    public void DisabledDefaultsPass()
    {
        var result = _validator.Validate(null, new TelemetryOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ValidEnabledConfigurationPasses()
    {
        var options = new TelemetryOptions
        {
            Enabled = true,
            OtlpEndpoint = "http://127.0.0.1:4317",
            SamplingRatio = 0.25,
            TraceBatchSize = 128,
            TraceBatchQueueSize = 2048,
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("ftp://collector:4317")]
    [InlineData("not a uri")]
    public void InvalidEndpointFails(string endpoint)
    {
        var options = Enabled();
        options.OtlpEndpoint = endpoint;

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("OtlpEndpoint", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void InvalidSamplingRatioFails(double ratio)
    {
        var options = Enabled();
        options.SamplingRatio = ratio;

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("SamplingRatio", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(TelemetryOptions.TraceBatchDelayMilliseconds))]
    [InlineData(nameof(TelemetryOptions.TraceExportTimeoutMilliseconds))]
    [InlineData(nameof(TelemetryOptions.TraceBatchQueueSize))]
    [InlineData(nameof(TelemetryOptions.TraceBatchSize))]
    [InlineData(nameof(TelemetryOptions.OtlpTimeoutMilliseconds))]
    [InlineData(nameof(TelemetryOptions.MetricExportIntervalMilliseconds))]
    [InlineData(nameof(TelemetryOptions.MetricExportTimeoutMilliseconds))]
    public void NonPositiveBoundFails(string propertyName)
    {
        var options = Enabled();
        typeof(TelemetryOptions).GetProperty(propertyName)!.SetValue(options, 0);

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(propertyName, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchLargerThanQueueFails()
    {
        var options = Enabled();
        options.TraceBatchSize = 100;
        options.TraceBatchQueueSize = 10;

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("TraceBatchSize", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyServiceIdentityFails()
    {
        var options = Enabled();
        options.ServiceName = " ";
        options.ServiceInstanceId = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains("ServiceName", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("ServiceInstanceId", result.FailureMessage, StringComparison.Ordinal);
    }

    private static TelemetryOptions Enabled() => new()
    {
        Enabled = true,
    };
}
