using System.Diagnostics;
using ServiceScheduler.Api.Infrastructure.Telemetry;
using Xunit;

namespace ServiceScheduler.UnitTests.Telemetry;

public sealed class ProblemTraceIdTests
{
    [Fact]
    public void UsesThirtyTwoLowercaseHexWhenAnActivityExists()
    {
        using var listener = new ActivityTestListener();
        using var source = new ActivitySource("trace-id-test");
        using var activity = source.StartActivity("request");
        Assert.NotNull(activity);

        var traceId = ProblemTraceId.Resolve("fallback");

        Assert.Matches("^[0-9a-f]{32}$", traceId);
        Assert.Equal(activity!.TraceId.ToHexString(), traceId);
    }

    [Fact]
    public void FallsBackToTheRequestIdentifierWhenNoActivityExists()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            Assert.Equal("request-identifier", ProblemTraceId.Resolve("request-identifier"));
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public void RejectsAnEmptyFallback()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            Assert.Throws<ArgumentException>(() => ProblemTraceId.Resolve(" "));
        }
        finally
        {
            Activity.Current = previous;
        }
    }
}
