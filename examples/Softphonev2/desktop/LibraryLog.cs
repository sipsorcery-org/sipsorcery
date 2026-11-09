// SIPSorcery's own warnings and errors, into the softphone's log. The library logs through
// Microsoft.Extensions.Logging and is silent until it's given a logger factory; this one passes
// on warnings and up, at most once every 10 s for each message (a failing SRTP context, say,
// would otherwise write a line per packet), with how many were left out.

using Microsoft.Extensions.Logging;

namespace SIPSorcery.Softphone;

internal sealed class LibraryLog : ILoggerFactory
{
    /// <summary>Lines to log, already formatted. Raised on the library's threads.</summary>
    public static event Action<string>? Line;

    private static readonly Dictionary<string, (DateTime Last, int Skipped)> _recent = new();

    /// <summary>Sets this as the library's logger factory. Before anything else uses the library: its loggers are made once, per class.</summary>
    public static void Install() => SIPSorcery.LogFactory.Set(new LibraryLog());

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName[(categoryName.LastIndexOf('.') + 1)..]);
    public void AddProvider(ILoggerProvider provider) { }
    public void Dispose() { }

    private static void Write(string category, LogLevel level, string message, Exception? ex)
    {
        // Rate limited by the message with its numbers taken out, so "result -4" and "result -5" count apart but SSRCs don't.
        var key = category + System.Text.RegularExpressions.Regex.Replace(message, @"\d{4,}", "#");
        int skipped;
        lock (_recent)
        {
            var now = DateTime.UtcNow;
            if (_recent.TryGetValue(key, out var r) && now - r.Last < TimeSpan.FromSeconds(10)) { _recent[key] = (r.Last, r.Skipped + 1); return; }
            skipped = r.Skipped;
            _recent[key] = (now, 0);
        }
        Line?.Invoke($"sipsorcery {level.ToString().ToLowerInvariant()}: {category}: {message}{(ex != null ? $" ({ex.GetType().Name}: {ex.Message})" : "")}{(skipped > 0 ? $" (and {skipped} more like it)" : "")}");
    }

    private sealed class Logger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? ex, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) Write(category, level, formatter(state, ex), ex);
        }
    }
}
