using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ServiceScheduler.IntegrationTests.Fixtures;

/// <summary>Captures application log entries for assertions without writing to the console.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider, string categoryName) : ILogger
    {
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
            if (formatter is null)
            {
                return;
            }

            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?>(StringComparer.Ordinal);

            provider._entries.Enqueue(
                new LogEntry(logLevel, categoryName, formatter(state, exception), properties));
        }
    }
}

/// <summary>One captured log entry with its structured state.</summary>
public sealed record LogEntry(
    LogLevel Level,
    string Category,
    string Message,
    IReadOnlyDictionary<string, object?> Properties);
