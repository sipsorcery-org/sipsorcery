using System;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net;

internal static partial class NetRtspLoggingExtensions
{
    [LoggerMessage(
        EventId = 0,
        EventName = "RtspUnrecognisedHeaderParameter",
        Level = LogLevel.Warning,
        Message = "An RTSP Transport header parameter was not recognised: {Field}")]
    public static partial void LogRtspUnrecognisedHeaderParameterUnchecked(
        this ILogger logger,
        string field);

    public static void LogRtspUnrecognisedHeaderParameter(
        this ILogger logger,
        ReadOnlySpan<char> field)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogRtspUnrecognisedHeaderParameterUnchecked(logger, field.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspBlockedSend",
        Level = LogLevel.Error,
        Message = "The RTSPServer blocked Send to {DstEndPoint} as it was identified as a locally hosted TCP socket.")]
    public static partial void LogRtspBlockedSendUnchecked(
        this ILogger logger,
        string dstEndPoint);

    public static void LogRtspBlockedSend(
        this ILogger logger,
        IPEndPoint dstEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Error))
        {
            LogRtspBlockedSendUnchecked(logger, IPSocket.GetSocketString(dstEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspPruningConnections",
        Level = LogLevel.Debug,
        Message = "RTSPServer socket on {LocalIPEndPoint} pruning connections halted.")]
    public static partial void LogRtspPruningConnectionsUnchecked(
        this ILogger logger,
        string localIPEndPoint);

    public static void LogRtspPruningConnections(
        this ILogger logger,
        IPEndPoint localIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspPruningConnectionsUnchecked(logger, IPSocket.GetSocketString(localIPEndPoint));
        }
    }

    // ========== New extension methods for RTSP logging conversion ==========

    #region Server listener / accept

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspServerListenerCreated",
        Level = LogLevel.Debug,
        Message = "RTSP server listener created {LocalEndpoint}.")]
    public static partial void LogRtspServerListenerCreatedUnchecked(
        this ILogger logger,
        string localEndpoint);

    public static void LogRtspServerListenerCreated(
        this ILogger logger,
        IPEndPoint localIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspServerListenerCreatedUnchecked(logger, IPSocket.GetSocketString(localIPEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspAcceptSocketWarning",
        Level = LogLevel.Warning,
        Message = "Exception RTSPServer  accepting socket (Exception {ExceptionType}). {ErrorMessage}")]
    public static partial void LogRtspAcceptSocketWarning(
        this ILogger logger,
        string exceptionType,
        string errorMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspServerListenThreadStopped",
        Level = LogLevel.Debug,
        Message = "RTSP server socket on {LocalEndpoint} listening halted.")]
    public static partial void LogRtspServerListenThreadStoppedUnchecked(
        this ILogger logger,
        string localEndpoint);

    public static void LogRtspServerListenThreadStopped(
        this ILogger logger,
        IPEndPoint localIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspServerListenThreadStoppedUnchecked(logger, IPSocket.GetSocketString(localIPEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspCloseSession",
        Level = LogLevel.Debug,
        Message = "The RTP socket for RTSP session {SessionId} was disconnected.")]
    public static partial void LogRtspCloseSession(
        this ILogger logger,
        string sessionId);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspPruningConnection",
        Level = LogLevel.Debug,
        Message = "Pruning inactive RTSP connection on to remote end point {RemoteEndPoint}.")]
    public static partial void LogRtspPruningConnectionUnchecked(
        this ILogger logger,
        string remoteEndPoint);

    public static void LogRtspPruningConnection(
        this ILogger logger,
        IPEndPoint remoteEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspPruningConnectionUnchecked(logger, IPSocket.GetSocketString(remoteEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspClosedInactiveSession",
        Level = LogLevel.Debug,
        Message = "Closing inactive RTSP session for session ID {SessionId} established from RTSP client on {RemoteEndPoint} (started at {StartedAt}, RTP last activity at {RtpLastActivity}, control last activity at {ControlLastActivity}, is closed {IsClosed}).")]
    public static partial void LogRtspClosedInactiveSession(
        this ILogger logger,
        string sessionId,
        string remoteEndPoint,
        DateTime startedAt,
        DateTime rtpLastActivity,
        DateTime controlLastActivity,
        bool isClosed);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspPruningConnectionsHalted",
        Level = LogLevel.Debug,
        Message = "RTSPServer socket on {LocalIPEndPoint} pruning connections halted.")]
    public static partial void LogRtspPruningConnectionsHaltedUnchecked(
        this ILogger logger,
        string localIPEndPoint);

    public static void LogRtspPruningConnectionsHalted(
        this ILogger logger,
        IPEndPoint localIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspPruningConnectionsHaltedUnchecked(logger, IPSocket.GetSocketString(localIPEndPoint));
        }
    }

    #endregion

    #region Server initialize / listen errors

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspServerInitializeError",
        Level = LogLevel.Error,
        Message = "Exception RTSPServer Initialise. {ErrorMessage}")]
    public static partial void LogRtspServerInitializeError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspServerListenError",
        Level = LogLevel.Error,
        Message = "Exception RTSPServer Listen. {ErrorMessage}")]
    public static partial void LogRtspServerListenError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    #endregion

    #region Server accept / disconnect

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspConnectionAccepted",
        Level = LogLevel.Debug,
        Message = "RTSP server accepted connection from {RemoteEndPoint}.")]
    public static partial void LogRtspConnectionAcceptedUnchecked(
        this ILogger logger,
        string remoteEndPoint);

    public static void LogRtspConnectionAccepted(
        this ILogger logger,
        IPEndPoint remoteEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspConnectionAcceptedUnchecked(logger, IPSocket.GetSocketString(remoteEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspClientDisconnected",
        Level = LogLevel.Debug,
        Message = "RTSP client socket from {RemoteEndPoint} disconnected.")]
    public static partial void LogRtspClientDisconnectedUnchecked(
        this ILogger logger,
        string remoteEndPoint);

    public static void LogRtspClientDisconnected(
        this ILogger logger,
        IPEndPoint remoteEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspClientDisconnectedUnchecked(logger, IPSocket.GetSocketString(remoteEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspAcceptThreadStarted",
        Level = LogLevel.Debug,
        Message = "RTSP server socket on {LocalIPEndPoint} accept connections thread started.")]
    public static partial void LogRtspAcceptThreadStartedUnchecked(
        this ILogger logger,
        string localIPEndPoint);

    public static void LogRtspAcceptThreadStarted(
        this ILogger logger,
        IPEndPoint localIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspAcceptThreadStartedUnchecked(logger, IPSocket.GetSocketString(localIPEndPoint));
        }
    }

    #endregion

    #region Server send errors

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspBlockedSendWarning",
        Level = LogLevel.Error,
        Message = "The RTSPServer blocked Send to {RemoteEndPoint} as it was identified as a locally hosted TCP socket.")]
    public static partial void LogRtspBlockedSendError(
        this ILogger logger,
        string remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSocketSendWarnNoConnection",
        Level = LogLevel.Warning,
        Message = "Could not send RTSP packet to TCP {RemoteEndPoint} as there was no current connection to the client, dropping message.")]
    public static partial void LogRtspSocketSendWarnNoConnectionUnchecked(
        this ILogger logger,
        string remoteEndPoint);

    public static void LogRtspSocketSendWarnNoConnection(
        this ILogger logger,
        IPEndPoint remoteEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogRtspSocketSendWarnNoConnectionUnchecked(logger, IPSocket.GetSocketString(remoteEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSocketSendWarning",
        Level = LogLevel.Warning,
        Message = "RTSPServer could not send to TCP socket {RemoteEndPoint}, closing and removing.")]
    public static partial void LogRtspSocketSendWarningUnchecked(
        this ILogger logger,
        string remoteEndPoint);

    public static void LogRtspSocketSendWarning(
        this ILogger logger,
        IPEndPoint remoteEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            LogRtspSocketSendWarningUnchecked(logger, IPSocket.GetSocketString(remoteEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSendToWarning",
        Level = LogLevel.Warning,
        Message = "ApplicationException RTSPServer Send (sendto=>{RemoteEndPoint}). {ErrorMessage}")]
    public static partial void LogRtspSendToWarning(
        this ILogger logger,
        string remoteEndPoint,
        string errorMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSendError",
        Level = LogLevel.Error,
        Message = "Exception RTSPServer Send (sendto=>{RemoteEndPoint}). {ErrorMessage}")]
    public static partial void LogRtspSendError(
        this ILogger logger,
        string remoteEndPoint,
        string errorMessage);

    #endregion

    #region Server close / end send

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspEndSendError",
        Level = LogLevel.Error,
        Message = "Exception RTSPServer EndSend. {ErrorMessage}")]
    public static partial void LogRtspEndSendError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspCloseDebug",
        Level = LogLevel.Debug,
        Message = "Closing RTSP server socket {LocalIPEndPoint}.")]
    public static partial void LogRtspCloseDebugUnchecked(
        this ILogger logger,
        string localIPEndPoint);

    public static void LogRtspCloseDebug(
        this ILogger logger,
        IPEndPoint localIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRtspCloseDebugUnchecked(logger, IPSocket.GetSocketString(localIPEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspCloseListenerWarning",
        Level = LogLevel.Warning,
        Message = "Exception RTSPServer Close (shutting down listener). {ErrorMessage}")]
    public static partial void LogRtspCloseListenerWarning(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspCloseConnectionWarning",
        Level = LogLevel.Warning,
        Message = "Exception RTSPServer Close (shutting down connection to {RemoteEndPoint}). {ErrorMessage}")]
    public static partial void LogRtspCloseConnectionWarning(
        this ILogger logger,
        string remoteEndPoint,
        string errorMessage);

    #endregion

    #region Server prune / dispose

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspPruneConnectionsError",
        Level = LogLevel.Error,
        Message = "Exception RTSPServer PruneConnections (pruning). {ErrorMessage}")]
    public static partial void LogRtspPruneConnectionsError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspPruneInactiveSessionError",
        Level = LogLevel.Error,
        Message = "Exception RTSPServer checking for inactive RTSP sessions. {ErrorMessage}")]
    public static partial void LogRtspPruneInactiveSessionError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspPruneAllConnectionsError",
        Level = LogLevel.Error,
        Message = "Exception RTSPServer PruneConnections. {ErrorMessage}")]
    public static partial void LogRtspPruneConnectionsAllError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspDisposeError",
        Level = LogLevel.Error,
        Message = "Exception Disposing RTSPServer. {ErrorMessage}")]
    public static partial void LogRtspDisposeError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspReceiveCallbackWarning",
        Level = LogLevel.Warning,
        Message = "Exception RTSPServer ReceiveCallback. {ErrorMessage}")]
    public static partial void LogRtspReceiveCallbackWarning(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    #endregion

    #region Client logging

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspClientConnectingHostname",
        Level = LogLevel.Debug,
        Message = "RTSP Client connecting to {Hostname}.")]
    public static partial void LogRtspClientConnectingHostname(
        this ILogger logger,
        string hostname);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspClientConnectingHostPort",
        Level = LogLevel.Debug,
        Message = "RTSP client connecting to {Hostname}, port {Port}.")]
    public static partial void LogRtspClientConnectingHostPort(
        this ILogger logger,
        string hostname,
        int port);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSetupResponseDebug",
        Level = LogLevel.Debug,
        Message = "RTSP Response received to SETUP: {Status}, session ID {SessionId}, server RTP endpoint {RemoteEndPoint}.")]
    public static partial void LogRtspSetupResponseDebug(
        this ILogger logger,
        string status,
        string sessionId,
        string remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspPlayResponseDebug",
        Level = LogLevel.Debug,
        Message = "RTSP Response received to PLAY: {StatusCode} {Status} {ReasonPhrase}.")]
    public static partial void LogRtspPlayResponseDebug(
        this ILogger logger,
        int statusCode,
        string status,
        string reasonPhrase);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspClientTeardownUrl",
        Level = LogLevel.Debug,
        Message = "RTSP client sending teardown request for {Url}.")]
    public static partial void LogRtspClientTeardownUrl(
        this ILogger logger,
        string url);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspClientTeardownClosedDebug",
        Level = LogLevel.Debug,
        Message = "RTSP client did not send teardown request for {Url}, the socket was closed.")]
    public static partial void LogRtspClientTeardownClosedDebug(
        this ILogger logger,
        string url);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspCloseConnectionDebug",
        Level = LogLevel.Debug,
        Message = "RTSP client, closing connection for {Url}.")]
    public static partial void LogRtspCloseConnectionDebug(
        this ILogger logger,
        string url);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSetupResponseWarning",
        Level = LogLevel.Warning,
        Message = "RTSP Response received to SETUP: {Status}.")]
    public static partial void LogRtspSetupResponseWarning(
        this ILogger logger,
        string status);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspRtpQueueFull",
        Level = LogLevel.Warning,
        Message = "RTSPCient.RTPQueueFull purging frames list.")]
    public static partial void LogRtspRtpQueueFull(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspDiscardingOldFrameWarning",
        Level = LogLevel.Warning,
        Message = "Discarding old frame for timestamp {Timestamp}.")]
    public static partial void LogRtspDiscardingOldFrameWarning(
        this ILogger logger,
        long timestamp);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSessionNoRtpTimeoutWarning",
        Level = LogLevel.Warning,
        Message = "No RTP packets were received on RTSP session {SessionId} for {TimeoutSeconds}. The session will now be closed.")]
    public static partial void LogRtspSessionNoRtpTimeoutWarning(
        this ILogger logger,
        string sessionId,
        int timeoutSeconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspKeepAliveWarning",
        Level = LogLevel.Warning,
        Message = "Zero bytes were read from the RTSP client socket in response to an OPTIONS keep-alive request.")]
    public static partial void LogRtspKeepAliveWarning(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspStreamDescriptionSocketClosed",
        Level = LogLevel.Warning,
        Message = "Socket closed prematurely in {Method} for {Url}.")]
    public static partial void LogRtspStreamDescriptionSocketClosed(
        this ILogger logger,
        string method,
        string url);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspResponseReceivedDebug",
        Level = LogLevel.Debug,
        Message = "RTSP Response received: {StatusCode} {Status} {ReasonPhrase}.")]
    public static partial void LogRtspResponseReceivedDebug(
        this ILogger logger,
        int statusCode,
        string status,
        string reasonPhrase);

    #endregion

    #region Exception-based logging (exception as first template param)

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspResponseParseError",
        Level = LogLevel.Error,
        Message = "Exception parsing RTSP response. {ErrorMessage}")]
    public static partial void LogRtspResponseParseError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspResponseToStringError",
        Level = LogLevel.Error,
        Message = "Exception RTSPResponse ToString. {ErrorMessage}")]
    public static partial void LogRtspResponseToStringError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspRequestParseError",
        Level = LogLevel.Error,
        Message = "Exception parsing RTSP request. URI, {Uri}. {ErrorMessage}")]
    public static partial void LogRtspRequestParseError(
        this ILogger logger,
        string uri,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspRequestToStringError",
        Level = LogLevel.Error,
        Message = "Exception RTSPRequest ToString. {ErrorMessage}")]
    public static partial void LogRtspRequestToStringError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspHeaderParseError",
        Level = LogLevel.Error,
        Message = "Error parsing RTSP header '{HeaderLine}'. {ErrorMessage}")]
    public static partial void LogRtspHeaderParseError(
        this ILogger logger,
        string headerLine,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspParseHeadersError",
        Level = LogLevel.Error,
        Message = "Exception ParseRTSPHeaders. {ErrorMessage}")]
    public static partial void LogRtspParseHeadersError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspHeaderToStringError",
        Level = LogLevel.Error,
        Message = "Exception RTSPHeader ToString. {ErrorMessage}")]
    public static partial void LogRtspHeaderToStringError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspUrlToStringError",
        Level = LogLevel.Error,
        Message = "Exception RTSPURL ToString. {ErrorMessage}")]
    public static partial void LogRtspUrlToStringError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspMessageParseBodyError",
        Level = LogLevel.Error,
        Message = "Exception ParseRTSPMessage. {ErrorMessage}\nRTSP Message={RtpMessage}.")]
    public static partial void LogRtspMessageParseBodyError(
        this ILogger logger,
        string errorMessage,
        string rtpMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspMessageParseError",
        Level = LogLevel.Error,
        Message = "Exception ParseRTSPMessage. {ErrorMessage}\nRTSP Message={RtpMessage}.")]
    public static partial void LogRtspMessageParseError(
        this ILogger logger,
        string rtpMessage,
        string errorMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspExceptionMethod",
        Level = LogLevel.Error,
        Message = "Exception in {Method}.")]
    public static partial void LogRtspExceptionMethod(
        this ILogger logger,
        string method,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspFrameReadyError",
        Level = LogLevel.Error,
        Message = "Exception in {Method} OnFrameReady.")]
    public static partial void LogRtspFrameReadyError(
        this ILogger logger,
        string method,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSendTypeError",
        Level = LogLevel.Error,
        Message = "Exception ({ExceptionType}) RTSPServer Send (sendto=>{RemoteEndPoint}). {ErrorMessage}")]
    public static partial void LogRtspSendTypeError(
        this ILogger logger,
        string exceptionType,
        string remoteEndPoint,
        string errorMessage);

    #endregion

    #region Connection logging

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspConnectionCloseWarning",
        Level = LogLevel.Warning,
        Message = "Exception closing socket in RTSPConnection Close. {ErrorMessage}")]
    public static partial void LogRtspConnectionCloseWarning(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspSocketReadCompletedError",
        Level = LogLevel.Error,
        Message = "Exception RTSPConnection SocketReadCompleted. {ErrorMessage}")]
    public static partial void LogRtspSocketReadCompletedError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    #endregion

    #region Header validation warnings

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspInvalidHeaderIgnoring",
        Level = LogLevel.Error,
        Message = "Invalid RTSP header, ignoring. header={HeaderLine}.")]
    public static partial void LogRtspInvalidHeaderIgnoring(
        this ILogger logger,
        string headerLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspFullInvalidHeaders",
        Level = LogLevel.Error,
        Message = "Full Invalid Headers: {ErrorHeaders}.")]
    public static partial void LogRtspFullInvalidHeaders(
        this ILogger logger,
        string errorHeaders);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspContentLengthEmptyWarning",
        Level = LogLevel.Warning,
        Message = "Invalid RTSP header, the {HeaderName} was empty.")]
    public static partial void LogRtspContentLengthEmptyWarning(
        this ILogger logger,
        string headerName);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspContentLengthNotIntWarning",
        Level = LogLevel.Warning,
        Message = "Invalid RTSP header, the {HeaderName} was not a valid 32 bit integer, {HeaderValue}.")]
    public static partial void LogRtspContentLengthNotIntWarning(
        this ILogger logger,
        string headerName,
        string headerValue);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspCseqEmptyWarning",
        Level = LogLevel.Warning,
        Message = "Invalid RTSP header, the {HeaderName} was empty.")]
    public static partial void LogRtspCseqEmptyWarning(
        this ILogger logger,
        string headerName);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspCseqNotIntWarning",
        Level = LogLevel.Warning,
        Message = "Invalid SIP header, the {HeaderName} was not a valid 32 bit integer, {HeaderValue}.")]
    public static partial void LogRtspCseqNotIntWarning(
        this ILogger logger,
        string headerName,
        string headerValue);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspUnknownMethodWarning",
        Level = LogLevel.Warning,
        Message = "Unknown RTSP method received {Method}.")]
    public static partial void LogRtspUnknownMethodWarning(
        this ILogger logger,
        RTSPMethodsEnum method);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspMaxMessageLength",
        Level = LogLevel.Error,
        Message = "RTSP message received that exceeded the maximum allowed message length, ignoring.")]
    public static partial void LogRtspMaxMessageLength(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspNoEOL",
        Level = LogLevel.Error,
        Message = "Error ParseRTSPMessage, there were no end of line characters in the string being parsed.")]
    public static partial void LogRtspNoEOL(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ProcessMjpegFrameWarning",
        Level = LogLevel.Warning,
        Message = "ProcessMjpegFrame could not determine either the width or height of the jpeg frame (width={Width}, height={Height}).")]
    public static partial void LogRtspMjpegFrameWarning(
        this ILogger logger,
        uint width,
        uint height);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspCloseRtpStreamError",
        Level = LogLevel.Error,
        Message = "Exception in {Method} closing RTP stream.")]
    public static partial void LogRtspCloseRtpStreamError(
        this ILogger logger,
        string method,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtspDebugResponseReceived",
        Level = LogLevel.Debug,
        Message = "{ResponseText}")]
    public static partial void LogRtspDebugResponseReceived(
        this ILogger logger,
        string responseText);

    #endregion
}
