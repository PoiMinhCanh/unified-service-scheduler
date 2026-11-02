using System.Diagnostics;
using ServiceScheduler.Api.Infrastructure.Telemetry;
using Xunit;

namespace ServiceScheduler.UnitTests.Telemetry;

public sealed class ServiceSchedulerTelemetryTests
{
    [Fact]
    public void CacheActivityCarriesBoundedAttributes()
    {
        using var listener = new ActivityTestListener();

        using (var activity = ServiceSchedulerTelemetry.StartCacheActivity("get", 250))
        {
            Assert.NotNull(activity);
            Assert.Equal("cache.get", activity!.OperationName);
            Assert.Equal("service_catalog", activity.GetTagItem("cache.name"));
            Assert.Equal("get", activity.GetTagItem("cache.operation"));
            Assert.Equal(250, activity.GetTagItem("cache.budget_ms"));
            ServiceSchedulerTelemetry.CompleteCacheActivity(activity, "hit", isFailure: false);
        }

        var stopped = Assert.Single(listener.Stopped);
        Assert.Equal("hit", stopped.GetTagItem("cache.result"));
        Assert.NotEqual(ActivityStatusCode.Error, stopped.Status);
    }

    [Fact]
    public void CacheFailureMarksTheActivityAsError()
    {
        using var listener = new ActivityTestListener();

        using (var activity = ServiceSchedulerTelemetry.StartCacheActivity("set", 250))
        {
            Assert.NotNull(activity);
            ServiceSchedulerTelemetry.CompleteCacheActivity(activity, "failure", isFailure: true);
        }

        var stopped = Assert.Single(listener.Stopped);
        Assert.Equal("failure", stopped.GetTagItem("cache.result"));
        Assert.Equal(ActivityStatusCode.Error, stopped.Status);
    }

    [Fact]
    public void CountersUseTheDeclaredNamesAndBoundedLabels()
    {
        using var listener = new MetricTestListener();

        ServiceSchedulerTelemetry.RecordHttpRequest(
            "api/dealerships/{dealershipId}/appointments",
            "POST",
            "4xx",
            "capacity_unavailable");
        ServiceSchedulerTelemetry.RecordBookingOutcome("created");
        ServiceSchedulerTelemetry.RecordBookingProcedureAttempt("technician_conflict");
        ServiceSchedulerTelemetry.RecordCacheOperation("get", "miss");
        ServiceSchedulerTelemetry.RecordBookingAllocationDuration("succeeded", 0.125);

        Assert.Contains(
            listener.Measurements,
            measurement => measurement.Instrument == ServiceSchedulerTelemetry.HttpRequestsInstrumentName
                           && Equals(measurement.Tags["route"], "api/dealerships/{dealershipId}/appointments")
                           && Equals(measurement.Tags["method"], "POST")
                           && Equals(measurement.Tags["status_class"], "4xx")
                           && Equals(measurement.Tags["outcome"], "capacity_unavailable"));
        Assert.Contains(
            listener.Measurements,
            measurement => measurement.Instrument == ServiceSchedulerTelemetry.BookingOutcomesInstrumentName
                           && Equals(measurement.Tags["outcome"], "created"));
        Assert.Contains(
            listener.Measurements,
            measurement => measurement.Instrument == ServiceSchedulerTelemetry.BookingProcedureAttemptsInstrumentName
                           && Equals(measurement.Tags["result"], "technician_conflict"));
        Assert.Contains(
            listener.Measurements,
            measurement => measurement.Instrument == ServiceSchedulerTelemetry.CacheOperationsInstrumentName
                           && Equals(measurement.Tags["operation"], "get")
                           && Equals(measurement.Tags["result"], "miss"));
        Assert.Contains(
            listener.Measurements,
            measurement => measurement.Instrument == ServiceSchedulerTelemetry.BookingAllocationDurationInstrumentName
                           && Equals(measurement.Tags["outcome"], "succeeded")
                           && measurement.Value > 0);
    }

    [Theory]
    [InlineData(99, "0xx")]
    [InlineData(200, "2xx")]
    [InlineData(404, "4xx")]
    [InlineData(503, "5xx")]
    public void StatusClassIsDerivedFromTheNumericStatus(int statusCode, string expected)
    {
        Assert.Equal(expected, ServiceSchedulerTelemetry.StatusClass(statusCode));
    }
}
