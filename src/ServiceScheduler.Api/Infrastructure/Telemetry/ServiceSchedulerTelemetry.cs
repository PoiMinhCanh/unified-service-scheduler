using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Owns the application activity source and meter and exposes bounded recording helpers. All calls
/// are cheap no-ops when no provider or listener is registered, so instrumentation never changes
/// request, booking, or cache decisions.
/// </summary>
public static class ServiceSchedulerTelemetry
{
    public const string SourceName = "ServiceScheduler";

    public const string MeterName = "ServiceScheduler";

    public const string HttpRequestsInstrumentName = "scheduler.http.requests";

    public const string BookingOutcomesInstrumentName = "scheduler.booking.outcomes";

    public const string BookingProcedureAttemptsInstrumentName = "scheduler.booking.procedure.attempts";

    public const string CacheOperationsInstrumentName = "scheduler.cache.operations";

    public const string BookingAllocationDurationInstrumentName = "scheduler.booking.allocation.duration";

    public const string CacheName = "service_catalog";

    public static readonly ActivitySource ActivitySource = new(SourceName);

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> HttpRequests = Meter.CreateCounter<long>(
        HttpRequestsInstrumentName,
        unit: "1",
        description: "Number of completed non-health HTTP requests.");

    private static readonly Counter<long> BookingOutcomes = Meter.CreateCounter<long>(
        BookingOutcomesInstrumentName,
        unit: "1",
        description: "Final booking outcomes returned by the controller.");

    private static readonly Counter<long> BookingProcedureAttempts = Meter.CreateCounter<long>(
        BookingProcedureAttemptsInstrumentName,
        unit: "1",
        description: "Booking procedure attempts grouped by bounded result.");

    private static readonly Counter<long> CacheOperations = Meter.CreateCounter<long>(
        CacheOperationsInstrumentName,
        unit: "1",
        description: "Service catalog cache operations grouped by bounded operation and result.");

    private static readonly Histogram<double> BookingAllocationDuration = Meter.CreateHistogram<double>(
        BookingAllocationDurationInstrumentName,
        unit: "s",
        description: "Duration of a booking allocation attempt.");

    public static Activity? StartCacheActivity(string operation, int budgetMilliseconds)
    {
        var activity = ActivitySource.StartActivity($"cache.{operation}");
        if (activity is null)
        {
            return null;
        }

        activity.SetTag("cache.name", CacheName);
        activity.SetTag("cache.operation", operation);
        activity.SetTag("cache.budget_ms", budgetMilliseconds);
        return activity;
    }

    public static void CompleteCacheActivity(Activity? activity, string? result, bool isFailure)
    {
        if (activity is null)
        {
            return;
        }

        if (result is not null)
        {
            activity.SetTag("cache.result", result);
        }

        if (isFailure)
        {
            activity.SetStatus(ActivityStatusCode.Error, result ?? "failure");
        }
    }

    public static void RecordCacheOperation(string operation, string result) =>
        CacheOperations.Add(1, BuildTags(("operation", operation), ("result", result)));

    public static void RecordHttpRequest(string route, string method, string statusClass, string outcome) =>
        HttpRequests.Add(
            1,
            BuildTags(("route", route), ("method", method), ("status_class", statusClass), ("outcome", outcome)));

    public static void RecordBookingOutcome(string outcome) =>
        BookingOutcomes.Add(1, BuildTags(("outcome", outcome)));

    public static void RecordBookingProcedureAttempt(string result) =>
        BookingProcedureAttempts.Add(1, BuildTags(("result", result)));

    public static void RecordBookingAllocationDuration(string outcome, double seconds) =>
        BookingAllocationDuration.Record(seconds, BuildTags(("outcome", outcome)));

    public static string StatusClass(int statusCode) => $"{statusCode / 100}xx";

    private static TagList BuildTags(params (string Key, object? Value)[] tags)
    {
        var tagList = new TagList();
        foreach (var (key, value) in tags)
        {
            tagList.Add(key, value);
        }

        return tagList;
    }
}
