using System.Diagnostics;
using Serilog;
using Serilog.Events;
using ServiceScheduler.Api.Infrastructure.Telemetry;
using Xunit;

namespace ServiceScheduler.UnitTests.Telemetry;

public sealed class ActivityLogEnricherTests
{
    [Fact]
    public void AddsTraceSpanAndFlagsWhenActivityExists()
    {
        using var listener = new ActivityTestListener();
        using var source = new ActivitySource("enricher-test");
        using var activity = source.StartActivity("request");
        Assert.NotNull(activity);

        var sink = new TestLogSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new ActivityLogEnricher())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("hello");

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(
            activity!.TraceId.ToHexString(),
            ((ScalarValue)logEvent.Properties["TraceId"]).Value);
        Assert.Equal(
            activity.SpanId.ToHexString(),
            ((ScalarValue)logEvent.Properties["SpanId"]).Value);
        Assert.Equal(
            activity.ActivityTraceFlags.ToString(),
            ((ScalarValue)logEvent.Properties["TraceFlags"]).Value);
        Assert.Matches("^[0-9a-f]{32}$", activity.TraceId.ToHexString());
        Assert.Matches("^[0-9a-f]{16}$", activity.SpanId.ToHexString());
    }

    [Fact]
    public void AddsNoCorrelationFieldsWithoutAnActivity()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var sink = new TestLogSink();
            using var logger = new LoggerConfiguration()
                .Enrich.With(new ActivityLogEnricher())
                .WriteTo.Sink(sink)
                .CreateLogger();

            logger.Information("hello");

            var logEvent = Assert.Single(sink.Events);
            Assert.False(logEvent.Properties.ContainsKey("TraceId"));
            Assert.False(logEvent.Properties.ContainsKey("SpanId"));
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void FrameworkScopePropertiesAreRemoved()
    {
        var sink = new TestLogSink();
        using var logger = new LoggerConfiguration()
            .Enrich.With(new ScopePropertyFilter())
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger
            .ForContext("RequestPath", "/api/dealerships/raw-id")
            .ForContext("RequestId", "request-identifier")
            .ForContext("ConnectionId", "connection-identifier")
            .Information("hello");

        var logEvent = Assert.Single(sink.Events);
        Assert.False(logEvent.Properties.ContainsKey("RequestPath"));
        Assert.False(logEvent.Properties.ContainsKey("RequestId"));
        Assert.False(logEvent.Properties.ContainsKey("ConnectionId"));
    }

    [Fact]
    public void ApplicationIdentityPropertiesAreSerializable()
    {
        var sink = new TestLogSink();
        using var logger = new LoggerConfiguration()
            .Enrich.WithProperty("Application", "unified-service-scheduler-api")
            .Enrich.WithProperty("ApplicationVersion", "1.0.0")
            .Enrich.WithProperty("Environment", "Development")
            .WriteTo.Sink(sink)
            .CreateLogger();

        logger.Information("hello");

        var logEvent = Assert.Single(sink.Events);
        Assert.Equal(
            "unified-service-scheduler-api",
            ((ScalarValue)logEvent.Properties["Application"]).Value);
        Assert.IsType<ScalarValue>(logEvent.Properties["ApplicationVersion"]);
        Assert.Equal(
            "Development",
            ((ScalarValue)logEvent.Properties["Environment"]).Value);
    }
}
