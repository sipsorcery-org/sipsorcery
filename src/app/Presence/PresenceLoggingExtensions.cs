using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP.App
{
    internal static partial class PresenceLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "PresenceAgentError",
            Level = LogLevel.Error,
            Message = "Presence agent error: {ErrorMessage}")]
        public static partial void LogPresenceAgentError(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "PresenceStateChange",
            Level = LogLevel.Debug,
            Message = "Presence state changed to {State} for {Uri}")]
        public static partial void LogPresenceStateChange(
            this ILogger logger,
            string state,
            string uri);
    }
}
