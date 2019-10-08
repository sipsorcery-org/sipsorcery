///----------------------------------------------------------------------------
/// File Name: Log.cs
/// 
/// Description: 
/// The Log class holds static application configuration settings for 
/// objects requiring configuration information. Log provides a one stop
/// shop for settings rather then have configuration functions in separate 
/// classes.
/// 
/// History:
/// 04 Nov 2004	Aaron Clauson	Created.
/// 14 Sep 2019 Aaron Clauson   Added NetStandard support.
///
/// License:
/// Public Domain.
///----------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    public class Log
    {
        public const string LOG_CATEGORY = "sipsorcery";

        private static ILoggerFactory _loggerFactory;
        public static ILoggerFactory LoggerFactory
        {
            set 
            {
                _loggerFactory = value;
                _logger = null;
            }

        }

        private static ILogger _logger;
        public static ILogger Logger
        {
            get
            {
                if (_logger == null && _loggerFactory != null)
                {
                    _logger = _loggerFactory.CreateLogger(LOG_CATEGORY);
                }

                return _logger ?? NullLogger.Instance;
            }
        }
    }

    /// <summary>
    /// Minimalistic logger that does nothing.
    /// </summary>
    public class NullLogger : ILogger
    {
        public static NullLogger Instance { get; } = new NullLogger();

        private NullLogger()
        {
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return false;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
        }
    }

    /// <summary>
    /// An empty scope without any logic
    /// </summary>
    public class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
