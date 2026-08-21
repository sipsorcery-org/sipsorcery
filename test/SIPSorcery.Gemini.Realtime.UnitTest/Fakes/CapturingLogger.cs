using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Gemini.Realtime.UnitTests;

public class CapturingLogger : ILogger
{
    public sealed record Entry(LogLevel Level, string Message, Exception? Exception);

    private readonly List<Entry> _entries = new();

    public IReadOnlyList<Entry> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_entries)
        {
            _entries.Add(new Entry(logLevel, formatter(state, exception), exception));
        }
    }

    public bool Contains(LogLevel level, string substring)
        => Entries.Any(e => e.Level == level && e.Message.Contains(substring, StringComparison.OrdinalIgnoreCase));

    public bool ContainsAnywhere(string substring)
        => Entries.Any(e => e.Message.Contains(substring, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Typed variant, for the constructors that take an <see cref="ILogger{TCategoryName}"/>.
/// </summary>
public class CapturingLogger<T> : CapturingLogger, ILogger<T>
{
}
