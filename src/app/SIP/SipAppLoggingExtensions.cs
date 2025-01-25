using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP.App
{
    internal static partial class SipAppLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0, 
            EventName = "SIPAppError", 
            Level = LogLevel.Error, 
            Message = "SIP application error: {ErrorMessage}")]
        public static partial void LogSIPAppError(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SIPAppWarning", 
            Level = LogLevel.Warning, 
            Message = "SIP application warning: {WarningMessage}")]
        public static partial void LogSIPAppWarning(
            this ILogger logger,
            string warningMessage);
    }
}
