using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    internal static partial class SipEventsLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0, 
            EventName = "LoadPresenceException", 
            Level = LogLevel.Error, 
            Message = "Exception SIPEventPresence Load. {ErrorMessage}")]
        public static partial void LogLoadPresenceError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "LoadDialogInfoException", 
            Level = LogLevel.Error, 
            Message = "Exception SIPEventDialogInfo constructor. {ErrorMessage}")]
        public static partial void LogLoadDialogInfoError(
            this ILogger logger,
            string errorMessage,
            Exception exception);
    }
}
