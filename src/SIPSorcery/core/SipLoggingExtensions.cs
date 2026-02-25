using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP;

internal static partial class SipLoggingExtensions
{
    [LoggerMessage(
        EventId = 0,
        EventName = "SipChannelCreating",
        Level = LogLevel.Debug,
        Message = "Creating SIP Channel for {SipSocketNode}.")]
    public static partial void LogSipChannelCreating(
        this ILogger logger,
        string sipSocketNode);

    [LoggerMessage(
        EventId = 0,
        EventName = "SipChannelCreateAttempt",
        Level = LogLevel.Debug,
        Message = " attempting to create SIP {TransportType} channel for {SipEndPoint}.")]
    public static partial void LogSipChannelCreateAttempt(
        this ILogger logger,
        string transportType,
        IPEndPoint sipEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "SipTlsCertificatePathMissing",
        Level = LogLevel.Warning,
        Message = "Could not create SIPTLSChannel from XML configuration node as no {CertificatePathParameter} attribute was present.")]
    public static partial void LogSipTlsCertificatePathMissing(
        this ILogger logger,
        string certificatePathParameter);

    [LoggerMessage(
        EventId = 0,
        EventName = "SipTlsChannelCreateAttempt",
        Level = LogLevel.Debug,
        Message = " attempting to create SIP TLS channel for {SipEndPoint} and certificate type of {CertificateType} at {CertificatePath}.")]
    public static partial void LogSipTlsChannelCreateAttempt(
        this ILogger logger,
        IPEndPoint sipEndPoint,
        string certificateType,
        string? certificatePath);

    [LoggerMessage(
        EventId = 0,
        EventName = "SipTlsChannelNotCreated",
        Level = LogLevel.Warning,
        Message = "A SIP TLS channel was not created because the certificate could not be loaded.")]
    public static partial void LogSipTlsChannelNotCreated(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "UnknownProtocolWarning",
        Level = LogLevel.Warning,
        Message = "Could not create a SIP channel for protocol {Protocol}.")]
    public static partial void LogUnknownProtocolWarning(
        this ILogger logger,
        SIPProtocolsEnum protocol);

    [LoggerMessage(
        EventId = 0,
        EventName = "ExceptionAddingSipChannel",
        Level = LogLevel.Warning,
        Message = "Exception SIPTransportConfig Adding SIP Channel for {SipEndPoint}.")]
    public static partial void LogExceptionAddingSipChannel(
        this ILogger logger,
        SIPEndPoint sipEndPoint,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ServerCertificateLoaded",
        Level = LogLevel.Debug,
        Message = "Server Certificate loaded from file, Subject={Subject}, valid={Valid}.")]
    public static partial void LogServerCertificateLoaded(
        this ILogger logger,
        string subject,
        bool valid);

    [LoggerMessage(
        EventId = 0,
        EventName = "ExceptionLoadCertificate",
        Level = LogLevel.Error,
        Message = "Exception LoadCertificate. {ErrorMessage}")]
    public static partial void LogExceptionLoadCertificate(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "InMessageQueueFull",
        Level = LogLevel.Warning,
        Message = "SIPTransport queue full new message from {RemoteEndPoint} being discarded.",
        SkipEnabledCheck = true)]
    private static partial void LogInMessageQueueFullUnchecked(
        this ILogger logger,
        string remoteEndPoint);

    public static void LogInMessageQueueFull(
        this ILogger logger,
        IPEndPoint remoteEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogInMessageQueueFullUnchecked(
                IPSocket.GetSocketString(remoteEndPoint));
        }
    }

    public static void LogInMessageQueueFull(
        this ILogger logger,
        SIPEndPoint remoteEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogInMessageQueueFullUnchecked(remoteEndPoint.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "StatelessTransactionSendNoop",
        Level = LogLevel.Warning,
        Message = "SIP transport was requested to send a transaction in stateless mode (noop).")]
    public static partial void LogStatelessTransactionSendNoop(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResponseTopViaMissing",
        Level = LogLevel.Warning,
        Message = "There was no top Via header on a SIP response from {RemoteSIPEndPoint} in SendResponseAsync, response dropped.",
        SkipEnabledCheck = true)]
    private static partial void LogResponseTopViaMissingUnchecked(
        this ILogger logger,
        string remoteSipEndPoint);

    public static void LogResponseTopViaMissing(
        this ILogger logger,
        IPEndPoint remoteSipEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogResponseTopViaMissingUnchecked(
                IPSocket.GetSocketString(remoteSipEndPoint));
        }
    }

    public static void LogResponseTopViaMissing(
        this ILogger logger,
        SIPEndPoint remoteSipEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogResponseTopViaMissingUnchecked(remoteSipEndPoint.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "SendResponseChannelNotFound",
        Level = LogLevel.Warning,
        Message = "An existing SIP channel could not be found to send response {ShortDescription}.")]
    public static partial void LogSendResponseChannelNotFound(
        this ILogger logger,
        string shortDescription);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResendingFinalResponse",
        Level = LogLevel.Warning,
        Message = "Resending final response for {Method}, {URI}, cseq={CSeq}.")]
    public static partial void LogResendingFinalResponse(
        this ILogger logger,
        SIPMethodsEnum method,
        SIPURI uri,
        int cSeq);

    [LoggerMessage(
        EventId = 0,
        EventName = "DuplicateRequestIgnoring",
        Level = LogLevel.Warning,
        Message = "Transaction already exists, ignoring duplicate request, {Method} {URI}.")]
    public static partial void LogDuplicateRequestIgnoring(
        this ILogger logger,
        SIPMethodsEnum method,
        SIPURI uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "RequestRetransmitted",
        Level = LogLevel.Trace,
        Message = "Request retransmit {Count} for request {StatusLine}, initial transmit {InitialTransmit:0.###}s ago.",
        SkipEnabledCheck = true)]
    private static partial void LogRequestRetransmitUnchecked(
        this ILogger logger,
        int count,
        string StatusLine,
        double initialTransmit);

    [LoggerMessage(
        EventId = 0,
        EventName = "RequestRetransmitTrace",
        Level = LogLevel.Trace,
        Message = "Request retransmitted: {Request}",
        SkipEnabledCheck = true)]
    private static partial void LogRequestRetransmitTraceUnchecked(
        this ILogger logger,
        string request);

    public static void LogRequestRetransmit(
        this ILogger logger,
        int count,
        SIPRequest request,
        DateTime initialTransmit)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogRequestRetransmitUnchecked(
                count,
                request.StatusLine,
                DateTime.Now.Subtract(initialTransmit).TotalSeconds);

            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogRequestRetransmitTraceUnchecked(
                    request.ToString());
            }
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ResponseRetransmitted",
        Level = LogLevel.Trace,
        Message = "Response retransmit {Count} for response {ShortDescription}, initial transmit {InitialTransmit:0.###}s ago.",
        SkipEnabledCheck = true)]
    private static partial void LogResponseRetransmitUnchecked(
        this ILogger logger,
        int count,
        string shortDescription,
        double initialTransmit);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResponseRetransmitTrace",
        Level = LogLevel.Trace,
        Message = "Response retransmitted: {Response}",
        SkipEnabledCheck = true)]
    private static partial void LogResponseRetransmitTraceUnchecked(
        this ILogger logger,
        string response);

    public static void LogResponseRetransmit(
        this ILogger logger,
        int count,
        SIPResponse response,
        DateTime initialTransmit)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogResponseRetransmitUnchecked(
                count,
                response.ShortDescription,
                DateTime.Now.Subtract(initialTransmit).TotalSeconds);

            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogResponseRetransmitTraceUnchecked(
                    response.ToString());
            }
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelCreated",
        Level = LogLevel.Information,
        Message = "SIP {ProtDescr} Channel created for {ListeningSIPEndPoint}.")]
    public static partial void LogChannelCreated(
        this ILogger logger,
        string protDescr,
        SIPEndPoint listeningSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelCreateTLS",
        Level = LogLevel.Information,
        Message = "SIP TLS Channel ready for {ListeningSIPEndPoint} and certificate {CertificateSubject}.")]
    public static partial void LogTlsChannelReady(
        this ILogger logger,
        SIPEndPoint listeningSIPEndPoint,
        string certificateSubject);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelCreateTLSClientOnly",
        Level = LogLevel.Information,
        Message = "SIP TLS client only channel ready.")]
    public static partial void LogTlsChannelReadyClientOnly(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelAcceptThreadStarted",
        Level = LogLevel.Debug,
        Message = "SIP {ProtDescr} Channel socket on {ListeningSIPEndPoint} accept connections thread started.")]
    public static partial void LogChannelAcceptThreadStarted(
        this ILogger logger,
        string protDescr,
        SIPEndPoint listeningSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelConnectionAccepted",
        Level = LogLevel.Debug,
        Message = "SIP {ProtDescr} Channel connection accepted from {RemoteEndPoint} by {ListeningSIPEndPoint}.")]
    public static partial void LogChannelConnectionAccepted(
        this ILogger logger,
        string protDescr,
        SIPEndPoint remoteEndPoint,
        SIPEndPoint listeningSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelSocketSendWarning",
        Level = LogLevel.Warning,
        Message = "SIP {ProtDescr} Channel Socket send to {RemoteEndPoint} failed with socket error {SocketError}, removing connection.")]
    public static partial void LogChannelSocketSendWarn(
        this ILogger logger,
        string protDescr,
        EndPoint remoteEndPoint,
        SocketError socketError);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelAcceptException",
        Level = LogLevel.Warning,
        Message = "Exception SIP {ProtDescr} Channel accepting socket ({AcceptExcpType}). {AcceptExcpMessage}")]
    public static partial void LogChannelAcceptException(
        this ILogger logger,
        string protDescr,
        string acceptExcpType,
        string acceptExcpMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelSendOnConnectedWarning",
        Level = LogLevel.Warning,
        Message = "SocketException SIP {ProtDescr} Channel SendOnConnected {RemoteSIPEndPoint}. ErrorCode {SocketErrorCode}. {ErrorMessage}")]
    public static partial void LogChannelSendOnConnectedWarning(
        this ILogger logger,
        string protDescr,
        SIPEndPoint remoteSIPEndPoint,
        int socketErrorCode,
        string errorMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelPruneConnectionsError",
        Level = LogLevel.Warning,
        Message = "Exception SIP {ProtDescr} Channel PruneConnections.")]
    public static partial void LogChannelPruneConnectionsError(
        this ILogger logger,
        Exception exception,
        string protDescr);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelPruningConnectionsHaltedTCP",
        Level = LogLevel.Debug,
        Message = "ConnectClientAsync SIP {ProtDescr} Channel local end point of {ListeningSIPEndPoint} selected for connection to {DstEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelClientConnectSelectingLocalEPUnchecked(
        this ILogger logger,
        string protDescr,
        SIPEndPoint listeningSIPEndPoint,
        string dstEndPoint);

    public static void LogChannelClientConnectSelectingLocalEP(
        this ILogger logger,
        string protDescr,
        SIPEndPoint listeningSIPEndPoint,
        IPEndPoint dstEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogChannelClientConnectSelectingLocalEPUnchecked(
                protDescr,
                listeningSIPEndPoint,
                IPSocket.GetSocketString(dstEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelAttemptingTCPConnection",
        Level = LogLevel.Debug,
        Message = "Attempting TCP connection from {ListeningSIPEndPoint} to {DstEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogAttemptingTCPConnectionUnchecked(
        this ILogger logger,
        SIPEndPoint listeningSIPEndPoint,
        string dstEndPoint);

    public static void LogAttemptingTCPConnection(
        this ILogger logger,
        SIPEndPoint listeningSIPEndPoint,
        IPEndPoint dstEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogAttemptingTCPConnectionUnchecked(
                listeningSIPEndPoint,
                IPSocket.GetSocketString(dstEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelConnectFailed",
        Level = LogLevel.Warning,
        Message = "SIP {ProtDescr} Channel send to {DstEndPoint} failed. Attempt to create a client socket failed with {SocketError}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelConnectFailedUnchecked(
        this ILogger logger,
        string protDescr,
        string dstEndPoint,
        SocketError socketError);

    public static void LogChannelConnectFailed(
        this ILogger logger,
        string protDescr,
        IPEndPoint dstEndPoint,
        SocketError socketError)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogChannelConnectFailedUnchecked(
                protDescr,
                IPSocket.GetSocketString(dstEndPoint),
                socketError);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelConnectFailedDest",
        Level = LogLevel.Warning,
        Message = "SIP {ProtDescr} Channel send to {DestinationEndPoint} failed. Attempt to create a client socket failed with {SocketError}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelConnectFailedDestUnchecked(
        this ILogger logger,
        string protDescr,
        string destinationEndPoint,
        SocketError socketError);

    public static void LogChannelConnectFailedDest(
        this ILogger logger,
        string protDescr,
        IPEndPoint destinationEndPoint,
        SocketError socketError)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogChannelConnectFailedDestUnchecked(
                protDescr,
                IPSocket.GetSocketString(destinationEndPoint),
                socketError);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelConnectCompleted",
        Level = LogLevel.Debug,
        Message = "ConnectAsync SIP {ProtDescr} Channel connect completed result for {ListeningSIPEndPoint}->{DstEndPoint} {Result}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelConnectCompletedUnchecked(
        this ILogger logger,
        string protDescr,
        SIPEndPoint listeningSIPEndPoint,
        string dstEndPoint,
        SocketError result);

    public static void LogChannelConnectCompleted(
        this ILogger logger,
        string protDescr,
        SIPEndPoint listeningSIPEndPoint,
        IPEndPoint dstEndPoint,
        SocketError result)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogChannelConnectCompletedUnchecked(
                protDescr,
                listeningSIPEndPoint,
                IPSocket.GetSocketString(dstEndPoint),
                result);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelPostConnectFailed",
        Level = LogLevel.Warning,
        Message = "SIP {ProtDescr} Channel send to {DstEndPoint} failed. Attempt to connect to server at {DstEndPoint} failed with {PostConnectResult}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelPostConnectFailedUnchecked(
        this ILogger logger,
        string protDescr,
        string dstEndPoint,
        SocketError postConnectResult);

    public static void LogChannelPostConnectFailed(
        this ILogger logger,
        string protDescr,
        IPEndPoint dstEndPoint,
        SocketError postConnectResult)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogChannelPostConnectFailedUnchecked(
                protDescr,
                IPSocket.GetSocketString(dstEndPoint),
                postConnectResult);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelBlockedSend",
        Level = LogLevel.Warning,
        Message = "SIP {ProtDescr} Channel blocked Send to {DestinationSipEndPoint} as it was identified as a locally hosted. \r\n{SocketError}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelBlockedSendUnchecked(
        this ILogger logger,
        string protDescr,
        SIPEndPoint destinationSipEndPoint,
        string socketError);

    public static void LogChannelBlockedSend(
        this ILogger logger,
        string protDescr,
        SIPEndPoint destinationSipEndPoint,
        ReadOnlySpan<byte> socketError)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogChannelBlockedSendUnchecked(
                protDescr,
                destinationSipEndPoint,
                SIPConstants.DEFAULT_ENCODING.GetString(socketError));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelNoConnectionForSend",
        Level = LogLevel.Warning,
        Message = "SIP {ProtDescr} Channel did not have an existing connection for send to {DstSIPEndPoint} and requested not to initiate a connection.")]
    public static partial void LogChannelNoConnectionForSend(
        this ILogger logger,
        string protDescr,
        SIPEndPoint dstSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelStreamDisconnected",
        Level = LogLevel.Debug,
        Message = "SIP {ProtDescr} stream disconnected {RemoteSIPEndPoint} {SocketError}.")]
    public static partial void LogChannelStreamDisconnected(
        this ILogger logger,
        string protDescr,
        SIPEndPoint remoteSIPEndPoint,
        SocketError socketError);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelCloseDebug",
        Level = LogLevel.Debug,
        Message = "Closing SIP {ProtDescr} Channel {ListeningEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelCloseUnchecked(
        this ILogger logger,
        string protDescr,
        string listeningEndPoint);

    public static void LogChannelClose(
        this ILogger logger,
        string protDescr,
        IPEndPoint listeningEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogChannelCloseUnchecked(
                protDescr,
                IPSocket.GetSocketString(listeningEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelListenerStopped",
        Level = LogLevel.Debug,
        Message = "Stopping SIP {ProtDescr} Channel listener {ListeningEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelListenerStoppedUnchecked(
        this ILogger logger,
        string protDescr,
        string listeningEndPoint);

    public static void LogChannelListenerStopped(
        this ILogger logger,
        string protDescr,
        IPEndPoint listeningEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogChannelListenerStoppedUnchecked(
                protDescr,
                IPSocket.GetSocketString(listeningEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelClosedSuccessfully",
        Level = LogLevel.Debug,
        Message = "Successfully closed SIP {ProtDescr} Channel for {ListeningEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogChannelClosedSuccessfullyUnchecked(
        this ILogger logger,
        string protDescr,
        string listeningEndPoint);

    public static void LogChannelClosedSuccessfully(
        this ILogger logger,
        string protDescr,
        IPEndPoint listeningEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogChannelClosedSuccessfullyUnchecked(
                protDescr,
                IPSocket.GetSocketString(listeningEndPoint));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelPruningInactiveConnection",
        Level = LogLevel.Debug,
        Message = "Pruning inactive connection on {ProtDescr} {ListeningSIPEndPoint} to remote end point {RemoteSIPEndPoint}.")]
    public static partial void LogChannelPruningInactiveConnection(
        this ILogger logger,
        string protDescr,
        SIPEndPoint listeningSIPEndPoint,
        SIPEndPoint remoteSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelPruningConnectionsHalted",
        Level = LogLevel.Debug,
        Message = "SIP {ProtDescr} Channel socket on {ListeningSIPEndPoint} pruning connections halted.")]
    public static partial void LogChannelPruningConnectionsHalted(
        this ILogger logger,
        string protDescr,
        SIPEndPoint listeningSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelCloseException",
        Level = LogLevel.Error,
        Message = "Exception closing SIP connection on {ProtDescr}.")]
    public static partial void LogChannelCloseException(
        this ILogger logger,
        Exception exception,
        string protDescr);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelCloseListenerError",
        Level = LogLevel.Error,
        Message = "Exception SIP {ProtDescr} Channel Close (shutting down listener).")]
    public static partial void LogChannelCloseListenerError(
        this ILogger logger,
        Exception exception,
        string protDescr);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelPruneConnectionsException",
        Level = LogLevel.Error,
        Message = "Exception PruneConnections (pruning).")]
    public static partial void LogChannelPruneConnectionsException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelPruneConnectionsErrorUDP",
        Level = LogLevel.Error,
        Message = "Exception SIP {ProtDescr} Channel PruneConnections.")]
    public static partial void LogChannelPruneConnectionsError(
        this ILogger logger,
        string protDescr);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsConnectAuthTimedOut",
        Level = LogLevel.Warning,
        Message = "SIP TLS Channel failed to connect to remote host. The authentication handshake timed out.")]
    public static partial void LogTlsConnectAuthTimedOut(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsConnectAuthFailed",
        Level = LogLevel.Warning,
        Message = "SIP TLS Channel failed to connect to remote host. The authentication handshake failed.")]
    public static partial void LogTlsConnectAuthFailed(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsAcceptedClientUpgraded",
        Level = LogLevel.Debug,
        Message = "SIP TLS Channel successfully upgraded accepted client to SSL stream for {ListeningSIPEndPoint}<-{RemoteSIPEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogTlsAcceptedClientUpgradedUnchecked(
        this ILogger logger,
        string listeningSIPEndPoint,
        string remoteSIPEndPoint);

    public static void LogTlsAcceptedClientUpgraded(
        this ILogger logger,
        IPEndPoint listeningSIPEndPoint,
        IPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogTlsAcceptedClientUpgradedUnchecked(
                IPSocket.GetSocketString(listeningSIPEndPoint),
                IPSocket.GetSocketString(remoteSIPEndPoint));
        }
    }

    public static void LogTlsAcceptedClientUpgraded(
        this ILogger logger,
        SIPEndPoint listeningSIPEndPoint,
        SIPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogTlsAcceptedClientUpgradedUnchecked(
                listeningSIPEndPoint.ToString(),
                remoteSIPEndPoint.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsSslEstablishFailed",
        Level = LogLevel.Warning,
        Message = "SIP TLS channel failed to establish SSL stream with {RemoteSIPEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogTlsSslEstablishFailedUnchecked(
        this ILogger logger,
        string remoteSIPEndPoint);

    public static void LogTlsSslEstablishFailed(
        this ILogger logger,
        IPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogTlsSslEstablishFailedUnchecked(
                IPSocket.GetSocketString(remoteSIPEndPoint));
        }
    }

    public static void LogTlsSslEstablishFailed(
        this ILogger logger,
        SIPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogTlsSslEstablishFailedUnchecked(
                remoteSIPEndPoint.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsClientConnectionUpgraded",
        Level = LogLevel.Debug,
        Message = "SIP TLS Channel successfully upgraded client connection to SSL stream for {ListeningSIPEndPoint}->{RemoteSIPEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogTlsClientConnectionUpgradedUnchecked(
        this ILogger logger,
        string listeningSIPEndPoint,
        string remoteSIPEndPoint);

    public static void LogTlsClientConnectionUpgraded(
        this ILogger logger,
        IPEndPoint listeningSIPEndPoint,
        IPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogTlsClientConnectionUpgradedUnchecked(
                IPSocket.GetSocketString(listeningSIPEndPoint),
                IPSocket.GetSocketString(remoteSIPEndPoint));
        }
    }

    public static void LogTlsClientConnectionUpgraded(
        this ILogger logger,
        SIPEndPoint listeningSIPEndPoint,
        SIPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogTlsClientConnectionUpgradedUnchecked(
                listeningSIPEndPoint.ToString(),
                remoteSIPEndPoint.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsSslEstablishTimedOut",
        Level = LogLevel.Warning,
        Message = "SIP TLS channel timed out attempting to establish SSL stream with {RemoteSIPEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogTlsSslEstablishTimedOutUnchecked(
        this ILogger logger,
        string remoteSIPEndPoint);

    public static void LogTlsSslEstablishTimedOut(
        this ILogger logger,
        IPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogTlsSslEstablishTimedOutUnchecked(
                IPSocket.GetSocketString(remoteSIPEndPoint));
        }
    }

    public static void LogTlsSslEstablishTimedOut(
        this ILogger logger,
        SIPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogTlsSslEstablishTimedOutUnchecked(
                remoteSIPEndPoint.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsSocketDisconnected",
        Level = LogLevel.Debug,
        Message = "TLS socket disconnected by {RemoteSIPEndPoint}.",
        SkipEnabledCheck = true)]
    private static partial void LogTlsSocketDisconnectedUnchecked(
        this ILogger logger,
        string remoteSIPEndPoint);

    public static void LogTlsSocketDisconnected(
        this ILogger logger,
        IPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogTlsSocketDisconnectedUnchecked(
                IPSocket.GetSocketString(remoteSIPEndPoint));
        }
    }

    public static void LogTlsSocketDisconnected(
        this ILogger logger,
        SIPEndPoint remoteSIPEndPoint)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogTlsSocketDisconnectedUnchecked(
                remoteSIPEndPoint.ToString());
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCertValidated",
        Level = LogLevel.Debug,
        Message = "Successfully validated X509 certificate for {CertificateSubject}.")]
    public static partial void LogTlsCertValidated(
        this ILogger logger,
        string certificateSubject);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsNegotiatedCipherSuite",
        Level = LogLevel.Debug,
        Message = "Negotiated cipher suite: {CipherSuite}, Protocol: {SslProtocol}")]
    public static partial void LogTlsNegotiatedCipherSuite(
        this ILogger logger,
        string cipherSuite,
        string sslProtocol);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsNegotiatedDetails",
        Level = LogLevel.Debug,
        Message = "Cipher: {CipherAlgorithm} strength {CipherStrength}, Hash: {HashAlgorithm} strength {HashStrength}, Key exchange: {KeyExchangeAlgorithm} strength {KeyExchangeStrength}, Protocol: {SslProtocol}")]
    public static partial void LogTlsNegotiatedDetails(
        this ILogger logger,
        string cipherAlgorithm,
        int cipherStrength,
        string hashAlgorithm,
        int hashStrength,
        string keyExchangeAlgorithm,
        int keyExchangeStrength,
        string sslProtocol);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsAuthStatus",
        Level = LogLevel.Debug,
        Message = "Is authenticated: {IsAuthenticated} as server? {IsServer}, IsSigned: {IsSigned}, Is Encrypted: {IsEncrypted}")]
    public static partial void LogTlsAuthStatus(
        this ILogger logger,
        bool isAuthenticated,
        bool isServer,
        bool isSigned,
        bool isEncrypted);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCanRead",
        Level = LogLevel.Debug,
        Message = "Can read: {CanRead}, write {CanWrite}, Can timeout: {CanTimeout}")]
    public static partial void LogTlsCanRead(
        this ILogger logger,
        bool canRead,
        bool canWrite,
        bool canTimeout);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCheckCrl",
        Level = LogLevel.Debug,
        Message = "Certificate revocation list checked: {CheckCertRevocationStatus}")]
    public static partial void LogTlsCheckCrl(
        this ILogger logger,
        bool checkCertRevocationStatus);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsLocalCertDetails",
        Level = LogLevel.Debug,
        Message = "Local cert was issued to {LocalCertSubject} and is valid from {LocalCertEffectiveDate} until {LocalCertExpirationDate}.")]
    public static partial void LogTlsLocalCertDetails(
        this ILogger logger,
        string localCertSubject,
        string localCertEffectiveDate,
        string localCertExpirationDate);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsLocalCertNull",
        Level = LogLevel.Warning,
        Message = "Local certificate is null.")]
    public static partial void LogTlsLocalCertNull(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsRemoteCertDetails",
        Level = LogLevel.Debug,
        Message = "Remote cert was issued to {RemoteCertSubject} and is valid from {RemoteCertEffectiveDate} until {RemoteCertExpirationDate}.")]
    public static partial void LogTlsRemoteCertDetails(
        this ILogger logger,
        string remoteCertSubject,
        string remoteCertEffectiveDate,
        string remoteCertExpirationDate);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsRemoteCertNull",
        Level = LogLevel.Warning,
        Message = "Remote certificate is null.")]
    public static partial void LogTlsRemoteCertNull(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCertificateError",
        Level = LogLevel.Warning,
        Message = "Certificate error: {SslPolicyErrors}")]
    public static partial void LogTlsCertificateError(
        this ILogger logger,
        string sslPolicyErrors);

    [LoggerMessage(
        EventId = 0,
        EventName = "UdpListenerStopped",
        Level = LogLevel.Debug,
        Message = "SIPUDPChannel socket on {ListeningEndPoint} listening halted.")]
    public static partial void LogUdpListenerStopped(
        this ILogger logger,
        IPEndPoint listeningEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "UdpClosing",
        Level = LogLevel.Debug,
        Message = "Closing SIP UDP Channel {ListeningSIPEndPoint}.")]
    public static partial void LogUdpChannelClosing(
        this ILogger logger,
        SIPEndPoint listeningSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "UdpTruncatedMessage",
        Level = LogLevel.Warning,
        Message = "The message was too large to fit into the specified buffer and was truncated.")]
    public static partial void LogUdpTruncatedMessage(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketChannelCreated",
        Level = LogLevel.Information,
        Message = "SIP WebSocket Channel created for {EndPoint}.")]
    public static partial void LogWebSocketChannelCreated(
        this ILogger logger,
        IPEndPoint endPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketChannelClosingDebug",
        Level = LogLevel.Debug,
        Message = "Closing SIP Web Socket Channel {ListeningEndPoint}.")]
    public static partial void LogWebSocketChannelClosing(
        this ILogger logger,
        IPEndPoint listeningEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientWsSendDebug",
        Level = LogLevel.Debug,
        Message = "Sending {BufferLength} bytes on client web socket connection to {ServerUri}.")]
    public static partial void LogClientWsSend(
        this ILogger logger,
        int bufferLength,
        Uri serverUri);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientWsConnectedDebug",
        Level = LogLevel.Debug,
        Message = "Successfully connected web socket client to {ServerUri}.")]
    public static partial void LogClientWsConnected(
        this ILogger logger,
        Uri serverUri);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientWsClosingDebug",
        Level = LogLevel.Debug,
        Message = "Closing SIP Client Web Socket Channel.")]
    public static partial void LogClientWsClosing(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientWsReceivedDebug",
        Level = LogLevel.Debug,
        Message = "Client web socket connection to {ServerUri} received {BytesReceived} bytes.")]
    public static partial void LogClientWsReceived(
        this ILogger logger,
        Uri serverUri,
        int bytesReceived);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientWsIncompleteWarning",
        Level = LogLevel.Warning,
        Message = "Client web socket connection to {ServerUri} returned without completing, closing.")]
    public static partial void LogClientWsIncomplete(
        this ILogger logger,
        Uri serverUri);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientWsAddToCollectionError",
        Level = LogLevel.Error,
        Message = "Could not add web socket client connected to {ServerUri} to channel collection, closing.")]
    public static partial void LogClientWsAddToCollectionError(
        this ILogger logger,
        Uri serverUri);

    [LoggerMessage(
        EventId = 0,
        EventName = "InvalidSIPHeader",
        Level = LogLevel.Warning,
        Message = "Invalid SIP header, ignoring {HeaderLine}.")]
    public static partial void LogInvalidSIPHeader(
        this ILogger logger,
        string headerLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "EmptyCSEQ",
        Level = LogLevel.Warning,
        Message = "The CSeq was empty.")]
    public static partial void LogEmptyCSEQ(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "EmptyHeaderWarning",
        Level = LogLevel.Warning,
        Message = "The {HeaderName} was empty.")]
    public static partial void LogEmptyHeaderWarning(
        this ILogger logger,
        string headerName);

    [LoggerMessage(
        EventId = 0,
        EventName = "MinExpiresNotIntegerWarning",
        Level = LogLevel.Warning,
        Message = "The Min-Expires value was not a valid integer. {HeaderLine}.")]
    public static partial void LogMinExpiresNotInteger(
        this ILogger logger,
        string headerLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "ExpiresNotIntegerWarning",
        Level = LogLevel.Warning,
        Message = "The Expires value was not a valid integer. {HeaderLine}.")]
    public static partial void LogExpiresNotInteger(
        this ILogger logger,
        string headerLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "MaxForwardsNotIntegerWarning",
        Level = LogLevel.Warning,
        Message = "The MaxForwards could not be parsed as a valid integer, {HeaderLine}")]
    public static partial void LogMaxForwardsNotInteger(
        this ILogger logger,
        string headerLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "ContentLengthNotIntegerWarning",
        Level = LogLevel.Warning,
        Message = "The Content-Length could not be parsed as a valid integer.")]
    public static partial void LogContentLengthNotInteger(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "RseqAckEmptyWarning",
        Level = LogLevel.Warning,
        Message = "The RSeqAck was empty.")]
    public static partial void LogRseqAckEmpty(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "RseqAckRseqInvalidInteger",
        Level = LogLevel.Warning,
        Message = "{HeaderName} did not contain a valid integer for the RSeq being acknowledged, {HeaderLine}")]
    public static partial void LogRseqAckRseqInvalidInteger(
        this ILogger logger,
        string headerName,
        string headerLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "RseqAckCseqInvalidInteger",
        Level = LogLevel.Warning,
        Message = "{HeaderName} did not contain a valid integer for the CSeq being acknowledged, {HeaderLine}")]
    public static partial void LogRseqAckCseqInvalid(
        this ILogger logger,
        string headerName,
        string headerLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "RseqAckMethodMissing",
        Level = LogLevel.Warning,
        Message = "There was no {HeaderName} method.")]
    public static partial void LogRseqAckMethodMissing(
        this ILogger logger,
        string headerName);

    [LoggerMessage(
        EventId = 0,
        EventName = "RseqValueNotIntegerWarning",
        Level = LogLevel.Warning,
        Message = "The Rseq value was not a valid integer. {HeaderLine}.")]
    public static partial void LogRseqValueNotInteger(
        this ILogger logger,
        string headerLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "DigestAlgorithmUnrecognized",
        Level = LogLevel.Warning,
        Message = "SIPAuthorisationDigest did not recognise digest algorithm value of {DigestAlgorithms}, defaulting to {DefaultedTo}.")]
    public static partial void LogDigestAlgorithmUnrecognized(
        this ILogger logger,
        string digestAlgorithms,
        int defaultedTo);

    [LoggerMessage(
        EventId = 0,
        EventName = "NoEndOfLineFound",
        Level = LogLevel.Warning,
        Message = "Error ParseSIPMessage, no end of line character found for the first line.")]
    public static partial void LogNoEndOfLineFound(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "UnknownMethodReceived",
        Level = LogLevel.Warning,
        Message = "Unknown SIP method received {UnknownMethod}.")]
    public static partial void LogUnknownMethodReceived(
        this ILogger logger,
        string unknownMethod);

    [LoggerMessage(
        EventId = 0,
        EventName = "ParseSIPRequestException",
        Level = LogLevel.Error,
        Message = "Exception ParseSIPRequest: {SipMessage}. {ErrorMessage}")]
    public static partial void LogParseSIPRequestException(
        this ILogger logger,
        string sipMessage,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ParseSIPHeaderError",
        Level = LogLevel.Error,
        Message = "Error parsing SIP header {HeaderLine}. {ErrorMessage}")]
    public static partial void LogParseSIPHeaderError(
        this ILogger logger,
        string headerLine,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "HistoryInfoSortWarning",
        Level = LogLevel.Warning,
        Message = "SIP {ProtDescr} stream disconnected {RemoteSIPEndPoint} {SocketError}.")]
    public static partial void LogChannelStreamDisconnectedWarning(
        this ILogger logger,
        string protDescr,
        SIPEndPoint remoteSIPEndPoint,
        SocketError socketError);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPStreamDisconnected",
        Level = LogLevel.Warning,
        Message = "SIP {ProtDescr} stream disconnected {RemoteEndPoint} {SocketError}.")]
    public static partial void LogSIPStreamDisconnectedWarning(
        this ILogger logger,
        string protDescr,
        string remoteEndPoint,
        int socketError);

    [LoggerMessage(
        EventId = 0,
        EventName = "SipDnsSrvResolved",
        Level = LogLevel.Debug,
        Message = "SIP DNS SRV for {Uri} resolved to {Host} and port {Port}.")]
    public static partial void LogSipDnsSrvResolved(
        this ILogger logger,
        SIPURI uri,
        string host,
        int port);

    [LoggerMessage(
        EventId = 0,
        EventName = "DnsLookupFailed",
        Level = LogLevel.Warning,
        Message = "Operating System DNS lookup failed for {MAddrOrHostAddress}.")]
    public static partial void LogDnsLookupFailed(
        this ILogger logger,
        string mAddrOrHostAddress);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsConnectionTimeout",
        Level = LogLevel.Warning,
        Message = "TLS connection timed out.")]
    public static partial void LogTlsConnectionTimeout(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsConnectionFailed",
        Level = LogLevel.Warning,
        Message = "TLS connection failed.")]
    public static partial void LogTlsConnectionFailed(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsSslStreamFailed",
        Level = LogLevel.Warning,
        Message = "SSL stream authentication failed from {RemoteSIPEndPoint}.")]
    public static partial void LogTlsSslStreamFailed(
        this ILogger logger,
        SIPEndPoint remoteSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsClientUpgraded",
        Level = LogLevel.Debug,
        Message = "TLS client connection upgraded on {ListeningSIPEndPoint} to {RemoteSIPEndPoint}.")]
    public static partial void LogTlsClientUpgraded(
        this ILogger logger,
        SIPEndPoint listeningSIPEndPoint,
        SIPEndPoint remoteSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsSslStreamTimeout",
        Level = LogLevel.Warning,
        Message = "SSL stream authentication timed out from {RemoteSIPEndPoint}.")]
    public static partial void LogTlsSslStreamTimeout(
        this ILogger logger,
        SIPEndPoint remoteSIPEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsOnReadIOException",
        Level = LogLevel.Warning,
        Message = "TLS OnRead IOException.")]
    public static partial void LogTlsOnReadIOException(
        this ILogger logger,
        Exception ioExcp);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsOnWriteSocketException",
        Level = LogLevel.Warning,
        Message = "TLS OnWrite SocketException from {RemoteSIPEndPoint}. ErrorCode: {SocketErrorCode}.")]
    public static partial void LogTlsOnWriteSocketException(
        this ILogger logger,
        Exception exception,
        SIPEndPoint remoteSIPEndPoint,
        SocketError socketErrorCode);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCertificateValidated",
        Level = LogLevel.Debug,
        Message = "Certificate validated: {CertificateSubject}.")]
    public static partial void LogTlsCertificateValidated(
        this ILogger logger,
        string certificateSubject);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCertificatePolicyErrors",
        Level = LogLevel.Warning,
        Message = "SSL policy errors: {SslPolicyErrors}.")]
    public static partial void LogTlsCertificatePolicyErrors(
        this ILogger logger,
        SslPolicyErrors sslPolicyErrors);

#if NET5_0_OR_GREATER
    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCipherInfo",
        Level = LogLevel.Debug,
        Message = "TLS cipher suite: {CipherSuite}, Protocol: {SslProtocol}.")]
    public static partial void LogTlsCipherInfo(
        this ILogger logger,
        TlsCipherSuite cipherSuite,
        SslProtocols sslProtocol);
#else
    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCipherFallback",
        Level = LogLevel.Information,
        Message = "Cipher: {CipherAlgorithm} strength {CipherStrength}, Hash: {HashAlgorithm} strength {HashStrength}, Key exchange: {KeyExchangeAlgorithm} strength {KeyExchangeStrength}, Protocol: {SslProtocol}")]
    public static partial void LogTlsCipherFallback(
#pragma warning disable SYSLIB0058
        this ILogger logger,
        CipherAlgorithmType cipherAlgorithm,
        int cipherStrength,
        HashAlgorithmType hashAlgorithm,
        int hashStrength,
        ExchangeAlgorithmType keyExchangeAlgorithm,
        int keyExchangeStrength,
        SslProtocols sslProtocol);
#pragma warning restore SYSLIB0058
#endif

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsSecurityServices",
        Level = LogLevel.Debug,
        Message = "TLS security: Authenticated={IsAuthenticated}, Server={IsServer}, Signed={IsSigned}, Encrypted={IsEncrypted}.")]
    public static partial void LogTlsSecurityServices(
        this ILogger logger,
        bool isAuthenticated,
        bool isServer,
        bool isSigned,
        bool isEncrypted);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsStreamProperties",
        Level = LogLevel.Debug,
        Message = "TLS stream: CanRead={CanRead}, CanWrite={CanWrite}, CanTimeout={CanTimeout}.")]
    public static partial void LogTlsStreamProperties(
        this ILogger logger,
        bool canRead,
        bool canWrite,
        bool canTimeout);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsCertRevocationCheck",
        Level = LogLevel.Debug,
        Message = "TLS cert revocation check: {CheckCertRevocationStatus}.")]
    public static partial void LogTlsCertRevocationCheck(
        this ILogger logger,
        bool checkCertRevocationStatus);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsLocalCertificate",
        Level = LogLevel.Debug,
        Message = "TLS local cert: Subject={Subject}, EffectiveDate={EffectiveDate}, ExpirationDate={ExpirationDate}.")]
    public static partial void LogTlsLocalCertificate(
        this ILogger logger,
        string subject,
        string effectiveDate,
        string expirationDate);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsLocalCertificateIsNull",
        Level = LogLevel.Debug,
        Message = "TLS local certificate is null.")]
    public static partial void LogTlsLocalCertificateIsNull(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsRemoteCertificate",
        Level = LogLevel.Debug,
        Message = "TLS remote cert: Subject={Subject}, EffectiveDate={EffectiveDate}, ExpirationDate={ExpirationDate}.")]
    public static partial void LogTlsRemoteCertificate(
        this ILogger logger,
        string subject,
        string effectiveDate,
        string expirationDate);

    [LoggerMessage(
        EventId = 0,
        EventName = "TlsRemoteCertificateIsNull",
        Level = LogLevel.Debug,
        Message = "TLS remote certificate is null.")]
    public static partial void LogTlsRemoteCertificateIsNull(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelAcceptCancelledObjectDisposed",
        Level = LogLevel.Debug,
        Message = "SIP {ProtDescr} Channel accepts for {ListeningEndPoint} cancelled (object disposed).")]
    public static partial void LogChannelAcceptCancelledObjectDisposed(
        this ILogger logger,
        string protDescr,
        System.Net.IPEndPoint listeningEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelAcceptCancelledAggregate",
        Level = LogLevel.Debug,
        Message = "SIP {ProtDescr} Channel accepts for {ListeningEndPoint} cancelled (aggregate exception).")]
    public static partial void LogChannelAcceptCancelledAggregate(
        this ILogger logger,
        string protDescr,
        System.Net.IPEndPoint listeningEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ChannelSendObjectDisposed",
        Level = LogLevel.Debug,
        Message = "SIP {ProtDescr} Channel send cancelled (socket disposed) for {RemoteEndPoint}.")]
    public static partial void LogChannelSendObjectDisposed(
        this ILogger logger,
        string protDescr,
        System.Net.IPEndPoint remoteEndPoint);
}
