using System;
using System.Net;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net
{
    internal static partial class NetRtspLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "RtspServerListenerCreated",
            Level = LogLevel.Debug,
            Message = "RTSP server listener created {IpEndPoint}.")]
        public static partial void LogRtspServerListenerCreated(
            this ILogger logger,
            IPEndPoint ipEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspServerAcceptStarted",
            Level = LogLevel.Debug,
            Message = "RTSP server socket on {IpEndPoint} accept connections thread started.")]
        public static partial void LogRtspServerAcceptStarted(
            this ILogger logger,
            IPEndPoint ipEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspServerAcceptedConnection",
            Level = LogLevel.Debug, 
            Message = "RTSP server accepted connection from {RemoteEndPoint}.")]
        public static partial void LogRtspServerAcceptedConnection(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspServerAcceptHalted", 
            Level = LogLevel.Debug,
            Message = "RTSP server socket on {IpEndPoint} listening halted.")]
        public static partial void LogRtspServerAcceptHalted(
            this ILogger logger,
            IPEndPoint ipEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspServerSocketDisconnected",
            Level = LogLevel.Debug,
            Message = "RTSP client socket from {RemoteEndPoint} disconnected.")]
        public static partial void LogRtspServerSocketDisconnected(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspClientConnecting",
            Level = LogLevel.Debug,
            Message = "RTSP client connecting to {Hostname}, port {Port}.")]
        public static partial void LogRtspClientConnecting(
            this ILogger logger,
            string hostname,
            int port);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspClientConnectionClosing",
            Level = LogLevel.Debug,
            Message = "RTSP client, closing connection for {Url}.")]
        public static partial void LogRtspClientConnectionClosing(
            this ILogger logger,
            string url);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspResponseReceived",
            Level = LogLevel.Debug,
            Message = "RTSP Response received: {StatusCode} {Status} {ReasonPhrase}.")]
        public static partial void LogRtspResponseReceived(
            this ILogger logger,
            string statusCode,
            string status,
            string reasonPhrase);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspResponseSetup",
            Level = LogLevel.Debug,
            Message = "RTSP Response received to SETUP: {Status}, session ID {SessionId}, server RTP endpoint {ServerEndpoint}.")]
        public static partial void LogRtspResponseSetup(
            this ILogger logger,
            RTSPResponseStatusCodesEnum status,
            string sessionId,
            IPEndPoint serverEndpoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspResponseSetupError",
            Level = LogLevel.Warning,
            Message = "RTSP Response received to SETUP: {Status}.")]
        public static partial void LogRtspResponseSetupError(
            this ILogger logger,
            RTSPResponseStatusCodesEnum status);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspResponsePlay",
            Level = LogLevel.Debug,
            Message = "RTSP Response received to PLAY: {StatusCode} {Status} {ReasonPhrase}.")]
        public static partial void LogRtspResponsePlay(
            this ILogger logger,
            int statusCode,
            string status,
            string reasonPhrase);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspSendingTeardown",
            Level = LogLevel.Debug,
            Message = "RTSP client sending teardown request for {Url}.")]
        public static partial void LogRtspSendingTeardown(
            this ILogger logger,
            string url);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspNoTeardown",
            Level = LogLevel.Debug,
            Message = "RTSP client did not send teardown request for {Url}, the socket was closed.")]
        public static partial void LogRtspNoTeardown(
            this ILogger logger,
            string url);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspPruningInactiveConnection",
            Level = LogLevel.Debug,
            Message = "Pruning inactive RTSP connection on to remote end point {RemoteEndPoint}.")]
        public static partial void LogRtspPruningInactiveConnection(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspSessionInactivityClose",
            Level = LogLevel.Debug,
            Message = "Closing inactive RTSP session for session ID {SessionId} established from RTSP client on {RemoteEndPoint} (started at {StartedAt}, RTP last activity at {RtpLastActivity}, control last activity at {ControlLastActivity}, is closed {IsClosed}).")]
        public static partial void LogRtspSessionInactivityClose(
            this ILogger logger,
            string sessionId,
            IPEndPoint remoteEndPoint,
            DateTime startedAt,
            DateTime rtpLastActivity,
            DateTime controlLastActivity,
            bool isClosed);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspPruningStopped",
            Level = LogLevel.Debug,
            Message = "RTSPServer socket on {LocalEndPoint} pruning connections halted.")]
        public static partial void LogRtspPruningStopped(
            this ILogger logger,
            string localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspRtpSocketDisconnected",
            Level = LogLevel.Debug,
            Message = "The RTP socket for RTSP session {SessionId} was disconnected.")]
        public static partial void LogRtspRtpSocketDisconnected(
            this ILogger logger,
            string sessionId);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnknownRtspMethod",
            Level = LogLevel.Warning,
            Message = "Unknown RTSP method received {method}.")]
        public static partial void LogUnknownRtspMethod(
            this ILogger logger,
            RTSPMethodsEnum method);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspHeaderInvalid",
            Level = LogLevel.Error,
            Message = "Invalid RTSP header, ignoring. header={headerLine}.")]
        public static partial void LogRtspHeaderInvalid(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspHeaderInvalidContent",
            Level = LogLevel.Warning,
            Message = "Invalid RTSP header, the {headerName} was empty.")]
        public static partial void LogRtspHeaderInvalidContent(
            this ILogger logger,
            string headerName);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspRequestParseError",
            Level = LogLevel.Error,
            Message = "Exception parsing RTSP request. URI, {uri}. {errorMessage}")]
        public static partial void LogRtspRequestParseError(
            this ILogger logger,
            string uri,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspResponseParseError",
            Level = LogLevel.Error,
            Message = "Exception parsing RTSP response. {errorMessage}")]
        public static partial void LogRtspResponseParseError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspMessageParseError", 
            Level = LogLevel.Error,
            Message = "Exception ParseRTSPMessage. {errorMessage}\nRTSP Message={rtspMessage}.",
            SkipEnabledCheck = true)]
        private static partial void LogRtspMessageParseErrorUnchecked(
            this ILogger logger,
            string errorMessage,
            string rtspMessage,
            Exception exception);

        public static void LogRtspMessageParseError(
            this ILogger logger,
            string errorMessage,
            string rtspMessage,
            Exception exception)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                LogRtspMessageParseErrorUnchecked(logger, errorMessage, rtspMessage.Replace("\n", "LF").Replace("\r", "CR"), exception);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspMessageMaxLengthExceeded",
            Level = LogLevel.Error,
            Message = "RTSP message received that exceeded the maximum allowed message length, ignoring.")]
        public static partial void LogRtspMessageMaxLength(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspFrameDimensionsError",
            Level = LogLevel.Warning,
            Message = "ProcessMjpegFrame could not determine either the width or height of the jpeg frame (width={width}, height={height}).")]
        public static partial void LogRtspFrameDimensions(
            this ILogger logger,
            uint width,
            uint height);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspHeaderFieldUnrecognized",
            Level = LogLevel.Warning,
            Message = "An RTSP Transport header parameter was not recognised: {field}")]
        public static partial void LogRtspHeaderFieldUnrecognized(
            this ILogger logger,
            string field);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspUrlParseError",
            Level = LogLevel.Error,
            Message = "There was an exception parsing an RTSP URL. {errorMessage} url={url}")]
        public static partial void LogRtspUrlParseError(
            this ILogger logger,
            string errorMessage,
            string url,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspMethodError",
            Level = LogLevel.Error,
            Message = "Exception {methodName}. {errorMessage}")]
        public static partial void LogRtspMethodError(
            this ILogger logger,
            string methodName,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspMessageEolMissing",
            Level = LogLevel.Error,
            Message = "Error ParseRTSPMessage, there were no end of line characters in the string being parsed.")]
        public static partial void LogRtspMessageEolMissing(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspConnectionErrorParsingHeader",
            Level = LogLevel.Error,
            Message = "Error parsing RTSP header '{headerLine}'. {errorMessage}")]
        public static partial void LogRtspConnectionErrorParsingHeader(
            this ILogger logger,
            string headerLine,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspHeaderParseError",
            Level = LogLevel.Error,
            Message = "Exception ParseRTSPHeaders. {errorMessage}")]
        public static partial void LogRtspHeaderParseError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspHeaderInvalidInteger",
            Level = LogLevel.Warning,
            Message = "Invalid RTSP header, the {headerName} was not a valid 32 bit integer, {headerValue}.")]
        public static partial void LogRtspHeaderInvalidInteger(
            this ILogger logger,
            string headerName,
            string headerValue);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspUrlToStringError",
            Level = LogLevel.Error,
            Message = "Exception RTSPURL ToString. {errorMessage}")]
        public static partial void LogRtspUrlToStringError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspRequestConstructorError",
            Level = LogLevel.Error,
            Message = "Exception RTSPRequest Ctor. {errorMessage}")]
        public static partial void LogRtspRequestConstructorError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspResponseToStringError",
            Level = LogLevel.Error,
            Message = "Exception RTSPResponse ToString. {errorMessage}")]
        public static partial void LogRtspResponseToStringError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspRequestToStringError",
            Level = LogLevel.Error,
            Message = "Exception RTSPRequest ToString. {errorMessage}")]
        public static partial void LogRtspRequestToStringError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtspHeaderToStringError",
            Level = LogLevel.Error,
            Message = "Exception RTSPHeader ToString. {errorMessage}")]
        public static partial void LogRtspHeaderToStringError(
            this ILogger logger,
            string errorMessage, 
            Exception exception);
    }
}
