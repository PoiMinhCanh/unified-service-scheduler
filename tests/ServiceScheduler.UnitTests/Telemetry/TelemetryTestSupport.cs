using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Events;
using ServiceScheduler.Api.Infrastructure.Telemetry;

namespace ServiceScheduler.UnitTests.Telemetry;

internal sealed class TestLogSink : ILogEventSink
{
    private readonly ConcurrentQueue<LogEvent> _events = new();

    public IReadOnlyCollection<LogEvent> Events => _events;

    public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
}

internal sealed class ActivityTestListener : IDisposable
{
    private readonly ActivityListener _listener;

    public ActivityTestListener()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = static _ => true,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => Stopped.Add(activity),
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public List<Activity> Stopped { get; } = [];

    public void Dispose() => _listener.Dispose();
}

internal sealed class MetricTestListener : IDisposable
{
    private readonly MeterListener _listener = new();

    public MetricTestListener()
    {
        _listener.InstrumentPublished = static (instrument, listener) =>
        {
            if (instrument.Meter.Name == ServiceSchedulerTelemetry.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, value, tags, _) => Measurements.Add(ToMeasurement(instrument, value, tags)));
        _listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) => Measurements.Add(ToMeasurement(instrument, value, tags)));
        _listener.Start();
    }

    public List<Measurement> Measurements { get; } = [];

    public void Dispose() => _listener.Dispose();

    private static Measurement ToMeasurement(
        Instrument instrument,
        double value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            dictionary[tag.Key] = tag.Value;
        }

        return new Measurement(instrument.Name, value, dictionary);
    }
}

internal sealed record Measurement(
    string Instrument,
    double Value,
    IReadOnlyDictionary<string, object?> Tags);

internal sealed record LogRecord(
    LogLevel Level,
    string Message,
    IReadOnlyDictionary<string, object?> Properties,
    Exception? Exception);

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<LogRecord> Records { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);

        Records.Add(new LogRecord(logLevel, formatter(state, exception), properties, exception));
    }
}
