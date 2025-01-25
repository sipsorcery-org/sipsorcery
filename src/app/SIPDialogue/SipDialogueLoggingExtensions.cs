using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP.App
{
    internal static partial class SipDialogueLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "DialogueStart",
            Level = LogLevel.Debug,
            Message = "Starting dialogue for {CallId}")]
        public static partial void LogDialogueStart(
            this ILogger logger,
            string callId);

        [LoggerMessage(
            EventId = 0,
            EventName = "DialogueInProgress",
            Level = LogLevel.Debug,
            Message = "Dialogue is now in progress for {CallId}")]
        public static partial void LogDialogueInProgress(
            this ILogger logger,
            string callId);

        [LoggerMessage(
            EventId = 0,
            EventName = "DialogueHangup",
            Level = LogLevel.Debug,
            Message = "Hanging up dialogue for {CallId}")]
        public static partial void LogDialogueHangup(
            this ILogger logger,
            string callId);
    }
}
