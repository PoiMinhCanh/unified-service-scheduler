using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace ServiceScheduler.Api.Infrastructure.Telemetry;

/// <summary>
/// Adds W3C correlation fields to every log event while an Activity exists. The trace ID matches
/// the Problem Details traceId and the Tempo trace ID; no trace ID is logged textually in a message.
/// </summary>
public sealed class ActivityLogEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        ArgumentNullException.ThrowIfNull(propertyFactory);

        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("TraceId", activity.TraceId.ToHexString()));
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("SpanId", activity.SpanId.ToHexString()));
        logEvent.AddPropertyIfAbsent(
            propertyFactory.CreateProperty("TraceFlags", activity.ActivityTraceFlags.ToString()));
    }
}
