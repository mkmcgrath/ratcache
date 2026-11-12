using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RatCache.Tests;

public record LogEntry(string Category, LogLevel Level, string Text);

// Keeps every log line the app writes during a test so tests can look at what was logged
// (NFR-1.4: no passwords or paste content; NFR-4.4: server errors carry the request ID).
// Text holds the formatted message plus the full exception text, if any.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    // A snapshot, safe to enumerate while the app keeps logging.
    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<LogEntry> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            if (exception is not null) text += Environment.NewLine + exception;
            sink.Enqueue(new LogEntry(category, logLevel, text));
        }
    }
}