using System;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    internal static partial class NetRtcpLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "RtcpSessionStart",
            Level = LogLevel.Debug,
            Message = "Starting RTCP session for {CNameOrSsrc}.",
            SkipEnabledCheck = true)]
        private static partial void LogRtcpSessionStartUnchecked(
            this ILogger logger,
            string cnameOrSsrc);

        public static void LogRtcpSessionStart(
            this ILogger logger,
            string cname,
            uint ssrc)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogRtcpSessionStartUnchecked(logger, !string.IsNullOrWhiteSpace(cname) ? cname : ssrc.ToString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcpSessionRemovingReport",
            Level = LogLevel.Debug,
            Message = "RTCP session removing reception report for remote ssrc {Ssrc}.")]
        public static partial void LogRtcpSessionRemovingReport(
            this ILogger logger,
            uint ssrc);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcpSessionNoActivity",
            Level = LogLevel.Warning,
            Message = "RTCP session for local ssrc {Ssrc} has not had any activity for over {NoActivityTimeoutSeconds} seconds.",
            SkipEnabledCheck = true)]
        private static partial void LogRtcpSessionNoActivityUnchecked(
            this ILogger logger,
            uint ssrc,
            int noActivityTimeoutSeconds);

        public static void LogRtcpSessionNoActivity(
            this ILogger logger,
            uint ssrc,
            int noActivityTimeoutMilliseconds)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                LogRtcpSessionNoActivityUnchecked(logger, ssrc, noActivityTimeoutMilliseconds / 1000);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcpSessionAlreadyStarted",
            Level = LogLevel.Warning,
            Message = "Start was called on RTCP session for {CNameOrSsrc} but it has already been started.",
            SkipEnabledCheck = true)]
        private static partial void LogRtcpSessionAlreadyStartedUnchecked(
            this ILogger logger,
            string cnameOrSsrc);

        public static void LogRtcpSessionAlreadyStarted(
            this ILogger logger,
            string cname,
            uint ssrc)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                LogRtcpSessionAlreadyStartedUnchecked(logger, !string.IsNullOrWhiteSpace(cname) ? cname : ssrc.ToString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcpSessionSendReportError", 
            Level = LogLevel.Error,
            Message = "Exception RTCPSession.SendReportTimerCallback. {errorMessage}")]
        public static partial void LogRtcpSessionSendReportError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcpSessionReportReceiveError",
            Level = LogLevel.Error,
            Message = "Exception RTCPSession.ReportReceived. {errorMessage}")]
        public static partial void LogRtcpSessionReportReceiveError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RTCPCompoundPacketUnrecognizedType", 
            Level = LogLevel.Warning, 
            Message = "RTCPCompoundPacket did not recognise packet type ID {packetTypeId}. {packet}",
            SkipEnabledCheck = true)]
        private static partial void LogRtcpCompoundPacketUnrecognizedTypeUnchecked(
            this ILogger logger,
            byte packetTypeId,
            string packet);

        public static void LogRtcpCompoundPacketUnrecognizedType(
            this ILogger logger,
            byte packetTypeId,
            byte[] packet)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogRtcpCompoundPacketUnrecognizedTypeUnchecked(packetTypeId, packet.HexStr());
            }
        }
    }
}
