using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    internal static partial class SipChannelsLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientSending",
            Level = LogLevel.Debug,
            Message = "Sending {BufferLength} bytes on client web socket connection to {ServerUri}.")]
        public static partial void LogWebSocketClientSending(
            this ILogger logger,
            int bufferLength,
            Uri serverUri);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientConnected",
            Level = LogLevel.Debug,
            Message = "Successfully connected web socket client to {ServerUri}.")]
        public static partial void LogWebSocketClientConnected(
            this ILogger logger,
            Uri serverUri);

        [LoggerMessage(
            EventId = 0,
            EventName = "ClosingWebSocketChannel",
            Level = LogLevel.Debug,
            Message = "Closing SIP Client Web Socket Channel.")]
        public static partial void LogClosingWebSocketChannel(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientReceived",
            Level = LogLevel.Debug,
            Message = "Client web socket connection to {ServerUri} received {BytesReceived} bytes.")]
        public static partial void LogWebSocketClientReceived(
            this ILogger logger,
            Uri serverUri,
            int bytesReceived);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketConnectionStart",
            Level = LogLevel.Debug,
            Message = "SIP {ProtDescr} Channel socket on {ListeningEndPoint} accept connections thread started.")]
        public static partial void LogTcpSocketConnectionStart(
            this ILogger logger,
            string protDescr,
            SIPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketConnectionAccepted",
            Level = LogLevel.Debug,
            Message = "SIP {ProtDescr} Channel connection accepted from {RemoteEndPoint} by {ListeningEndPoint}.")]
        public static partial void LogTcpSocketConnectionAccepted(
            this ILogger logger,
            string protDescr,
            SIPEndPoint remoteEndPoint,
            SIPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketConnectionError",
            Level = LogLevel.Warning,
            Message = "Exception SIP {ProtDescr} Channel accepting socket ({ExceptionType}). {ExceptionMessage}")]
        public static partial void LogTcpSocketConnectionError(
            this ILogger logger,
            string protDescr,
            Type exceptionType,
            string exceptionMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketConnectClientEndpoint",
            Level = LogLevel.Debug,
            Message = "ConnectClientAsync SIP {ProtDescr} Channel local end point of {ListeningEndPoint} selected for connection to {DestEndPoint}.")]
        public static partial void LogTcpSocketConnectClientEndpoint(
            this ILogger logger,
            string protDescr,
            SIPEndPoint listeningEndPoint,
            IPEndPoint destEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketAttemptingConnection",
            Level = LogLevel.Debug,
            Message = "Attempting TCP connection from {ListeningEndPoint} to {DestEndPoint}.")]
        public static partial void LogTcpSocketAttemptingConnection(
            this ILogger logger,
            SIPEndPoint listeningEndPoint,
            IPEndPoint destEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketConnectCompleted",
            Level = LogLevel.Debug,
            Message = "ConnectAsync SIP {ProtDescr} Channel connect completed result for {ListeningEndPoint}->{DestEndPoint} {Result}.")]
        public static partial void LogTcpSocketConnectCompleted(
            this ILogger logger,
            string protDescr,
            SIPEndPoint listeningEndPoint,
            IPEndPoint destEndPoint,
            SocketError result);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketStoppingListener",
            Level = LogLevel.Debug,
            Message = "Stopping SIP {ProtDescr} Channel listener {ListeningEndPoint}.")]
        public static partial void LogTcpSocketStoppingListener(
            this ILogger logger,
            string protDescr,
            IPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketCloseSuccess",
            Level = LogLevel.Debug,
            Message = "Successfully closed SIP {ProtDescr} Channel for {ListeningEndPoint}.")]
        public static partial void LogTcpSocketCloseSuccess(
            this ILogger logger,
            string protDescr,
            IPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketPruneInactive",
            Level = LogLevel.Debug,
            Message = "Pruning inactive connection on {ProtDescr} {ListeningEndPoint} to remote end point {RemoteEndPoint}.")]
        public static partial void LogTcpSocketPruneInactive(
            this ILogger logger,
            string protDescr,
            SIPEndPoint listeningEndPoint,
            SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketPruningHalted",
            Level = LogLevel.Debug,
            Message = "SIP {ProtDescr} Channel socket on {ListeningEndPoint} pruning connections halted.")]
        public static partial void LogTcpSocketPruningHalted(
            this ILogger logger,
            string protDescr,
            SIPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketClosing",
            Level = LogLevel.Debug,
            Message = "Closing SIP {ProtDescr} Channel {ListeningEndPoint}.")]
        public static partial void LogTcpSocketClosing(
            this ILogger logger,
            string protDescr,
            IPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpStreamDisconnected",
            Level = LogLevel.Debug,
            Message = "SIP {ProtDescr} stream disconnected {RemoteSIPEndPoint} {SocketError}.")]
        public static partial void LogTcpStreamDisconnected(
            this ILogger logger,
            string protDescr,
            SIPEndPoint remoteSIPEndPoint,
            SocketError socketError);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsServerUpgraded",
            Level = LogLevel.Debug,
            Message = "SIP TLS Channel successfully upgraded accepted client to SSL stream for {ListeningEndPoint}<-{RemoteEndPoint}.")]
        public static partial void LogTlsServerUpgraded(
        this ILogger logger,
        SIPEndPoint listeningEndPoint,
        SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsClientUpgraded",
            Level = LogLevel.Debug,
            Message = "SIP TLS Channel successfully upgraded client connection to SSL stream for {ListeningEndPoint}->{RemoteEndPoint}.")]
        public static partial void LogTlsClientUpgraded(
            this ILogger logger,
            SIPEndPoint listeningEndPoint,
            SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsChannelReady",
            Level = LogLevel.Information,
            Message = "SIP TLS Channel ready for {ListeningSIPEndPoint} and certificate {CertificateSubject}.")]
        public static partial void LogTlsChannelReady(
            this ILogger logger,
            SIPEndPoint listeningSIPEndPoint,
            string certificateSubject);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsClientOnlyChannelReady",
            Level = LogLevel.Information,
            Message = "SIP TLS client only channel ready.")]
        public static partial void LogTlsClientOnlyChannelReady(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsAuthenticateTimeout",
            Level = LogLevel.Warning,
            Message = "SIP TLS Channel failed to connect to remote host. The authentication handshake timed out.")]
        public static partial void LogTlsAuthenticateTimeout(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsConnectionError",
            Level = LogLevel.Error,
            Message = "SIP TLS channel could not connect to remote host. {ExceptionMessage}")]
        public static partial void LogTlsConnectionError(
            this ILogger logger,
            string exceptionMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsStreamAuthenticateFailure",
            Level = LogLevel.Warning,
            Message = "SIP TLS channel failed to establish SSL stream with {RemoteSIPEndPoint}.")]
        public static partial void LogTlsStreamAuthenticateFailure(
            this ILogger logger,
            SIPEndPoint remoteSIPEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsStreamConnectTimeout",
            Level = LogLevel.Warning,
            Message = "SIP TLS channel timed out attempting to establish SSL stream with {RemoteSIPEndPoint}.")]
        public static partial void LogTlsStreamConnectTimeout(
            this ILogger logger,
            SIPEndPoint remoteSIPEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsRemoteDisconnection",
            Level = LogLevel.Debug,
            Message = "TLS socket disconnected by {RemoteSIPEndPoint}.")]
        public static partial void LogTlsRemoteDisconnection(
            this ILogger logger,
            SIPEndPoint remoteSIPEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsCertificateValidated",
            Level = LogLevel.Debug,
            Message = "Successfully validated X509 certificate for {CertificateSubject}.")]
        public static partial void LogTlsCertificateValidated(
            this ILogger logger,
            string certificateSubject);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsSecurityLevel",
            Level = LogLevel.Debug,
            Message = "Cipher: {CipherAlgorithm} strength {CipherStrength}, Hash: {HashAlgorithm} strength {HashStrength}, Key exchange: {KeyExchangeAlgorithm} strength {KeyExchangeStrength}, Protocol: {SslProtocol}")]
        public static partial void LogSecurityLevel(
            this ILogger logger,
            CipherAlgorithmType cipherAlgorithm,
            int cipherStrength,
            HashAlgorithmType hashAlgorithm,
            int hashStrength,
            ExchangeAlgorithmType keyExchangeAlgorithm,
            int keyExchangeStrength,
            SslProtocols sslProtocol);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsSecurityServices",
            Level = LogLevel.Debug,
            Message = "Is authenticated: {IsAuthenticated} as server? {IsServer}, IsSigned: {IsSigned}, Is Encrypted: {IsEncrypted}")]
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
            Message = "Can read: {CanRead}, write {CanWrite}, Can timeout: {CanTimeout}")]
        public static partial void LogTlsStreamProperties(
            this ILogger logger,
            bool canRead,
            bool canWrite,
            bool canTimeout);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsRevocationCheck",
            Level = LogLevel.Debug,
            Message = "Certificate revocation list checked: {CheckCertRevocationStatus}")]
        private static partial void LogTlsRevocationCheck(
            this ILogger logger,
            bool checkCertRevocationStatus);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsLocalCertificateDetails",
            Level = LogLevel.Debug,
            Message = "Local cert was issued to {Subject} and is valid from {EffectiveDate} until {ExpirationDate}",
            SkipEnabledCheck = true)]
        private static partial void LogTlsLocalCertificateDetails(
            this ILogger logger,
            string subject,
            string effectiveDate,
            string expirationDate);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsLocalCertificateNull",
            Level = LogLevel.Warning,
            Message = "Local certificate is null.")]
        private static partial void LogTlsLocalCertificateNull(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsRemoteCertificateDetails",
            Level = LogLevel.Debug,
            Message = "Remote cert was issued to {Subject} and is valid from {EffectiveDate} until {ExpirationDate}")]
        private static partial void LogTlsRemoteCertificateDetails(
            this ILogger logger,
            string subject,
            string effectiveDate,
            string expirationDate);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsRemoteCertificateNull",
            Level = LogLevel.Warning,
            Message = "Remote certificate is null.")]
        private static partial void LogTlsRemoteCertificateNull(
            this ILogger logger);

        public static void DisplayCertificateInformation(this ILogger logger, SslStream stream)
        {
            LogTlsRevocationCheck(logger, stream.CheckCertRevocationStatus);

            X509Certificate localCertificate = stream.LocalCertificate;
            if (localCertificate != null)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    LogTlsLocalCertificateDetails(logger,
                        localCertificate.Subject,
                        localCertificate.GetEffectiveDateString(),
                        localCertificate.GetExpirationDateString());
                }
            }
            else
            {
                LogTlsLocalCertificateNull(logger);
            }

            X509Certificate remoteCertificate = stream.RemoteCertificate;
            if (remoteCertificate != null)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    LogTlsRemoteCertificateDetails(logger,
                        remoteCertificate.Subject,
                        remoteCertificate.GetEffectiveDateString(),
                        remoteCertificate.GetExpirationDateString());
                }
            }
            else
            {
                LogTlsRemoteCertificateNull(logger);
            }
        }

        public static void LogCertificateChain(

            this ILogger logger,
            X509Certificate2 certificate)
        {
            if (!logger.IsEnabled(LogLevel.Trace))
            {
                return;
            }

            X509Chain ch = new X509Chain();
            ch.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
            ch.ChainPolicy.RevocationMode = X509RevocationMode.Offline;
            ch.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
            ch.Build(certificate);

            var messageBuilder = new StringBuilder();

            messageBuilder.AppendLine("Chain Information");
            messageBuilder.Append("Chain revocation flag: ").AppendLine(ch.ChainPolicy.RevocationFlag.ToString());
            messageBuilder.Append("Chain revocation mode: ").AppendLine(ch.ChainPolicy.RevocationMode.ToString());
            messageBuilder.Append("Chain verification flag: ").AppendLine(ch.ChainPolicy.VerificationFlags.ToString());
            messageBuilder.Append("Chain verification time: ").AppendLine(ch.ChainPolicy.VerificationTime.ToString());
            messageBuilder.Append("Chain status length: ").AppendLine(ch.ChainStatus.Length.ToString());
            messageBuilder.Append("Chain application policy count: ").AppendLine(ch.ChainPolicy.ApplicationPolicy.Count.ToString());
            messageBuilder.Append("Chain certificate policy count: ").AppendLine(ch.ChainPolicy.CertificatePolicy.Count.ToString());
            messageBuilder.AppendLine();

            // Output chain element information.
            messageBuilder.AppendLine("Chain Element Information");
            messageBuilder.Append("Number of chain elements: ").AppendLine(ch.ChainElements.Count.ToString());
            messageBuilder.Append("Chain elements synchronized? ").AppendLine(ch.ChainElements.IsSynchronized.ToString());
            messageBuilder.AppendLine();

            for (var ce = 0; ce < ch.ChainElements.Count; ce++)
            {
                X509ChainElement element = ch.ChainElements[ce];

                messageBuilder.Append("Element issuer name: ").AppendLine(element.Certificate.Issuer);
                messageBuilder.Append("Element certificate valid until: ").AppendLine(element.Certificate.NotAfter.ToString());
                messageBuilder.Append("Element certificate is valid: ").AppendLine(element.Certificate.Verify().ToString());
                messageBuilder.Append("Element error status length: ").AppendLine(element.ChainElementStatus.Length.ToString());
                messageBuilder.Append("Element information: ").AppendLine(element.Information);
                messageBuilder.Append("Number of element extensions: ").AppendLine(element.Certificate.Extensions.Count.ToString());
                messageBuilder.AppendLine();

                if (ch.ChainStatus.Length > 1)
                {
                    for (int index = 0; index < element.ChainElementStatus.Length; index++)
                    {
                        messageBuilder.Append("Element issuer name: ").AppendLine(element.ChainElementStatus[index].Status.ToString());
                        messageBuilder.Append("Element issuer name: ").AppendLine(element.ChainElementStatus[index].StatusInformation);
                    }
                }
            }

            logger.LogTrace("Certificate chain: {CertificateChain}", messageBuilder.ToString());
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelCreated",
            Level = LogLevel.Information,
            Message = "SIP UDP Channel created for {ListeningEndPoint}.")]
        public static partial void LogUdpChannelCreated(
            this ILogger logger,
            SIPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelClosed",
            Level = LogLevel.Debug,
            Message = "Closing SIP UDP Channel {ListeningEndPoint}.")]
        public static partial void LogUdpChannelClosed(
            this ILogger logger,
            SIPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelReceiveError",
            Level = LogLevel.Error,
            Message = "Exception SIPUDPChannel Receive. {ErrorMessage}")]
        public static partial void LogUdpChannelReceiveError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelSendError",
            Level = LogLevel.Error,
            Message = "Exception SIPUDPChannel.SendAsync. {ErrorMessage}")]
        public static partial void LogUdpChannelSendError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelSocketException",
            Level = LogLevel.Warning,
            Message = "SocketException SIPUDPChannel EndReceiveFrom from {RemoteEndPoint} ({ErrorCode}). {Message}")]
        public static partial void LogUdpChannelSocketException(
            this ILogger logger,
            SIPEndPoint remoteEndPoint,
            int errorCode,
            string message);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelMessageTruncated",
            Level = LogLevel.Warning,
            Message = "The message was too large to fit into the specified buffer and was truncated.")]
        public static partial void LogUdpMessageTruncated(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelListeningHalted",
            Level = LogLevel.Debug,
            Message = "SIPUDPChannel socket on {ListeningEndPoint} listening halted.")]
        public static partial void LogUdpChannelListeningHalted(
            this ILogger logger,
            IPEndPoint listeningEndPoint);

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
            EventName = "WebSocketClientOnOpen",
            Level = LogLevel.Debug,
            Message = "SIPMessagWebSocketBehavior.OnOpen.")]
        public static partial void LogWebSocketClientOnOpen(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientOnMessage",
            Level = LogLevel.Debug,
            Message = "SIPMessagWebSocketBehavior.OnMessage: bytes received {Length}.")]
        public static partial void LogWebSocketClientOnMessage(
            this ILogger logger,
            int? length);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientOnClose",
            Level = LogLevel.Debug,
            Message = "SIPMessagWebSocketBehavior.OnClose: reason {Reason}, was clean {WasClean}.")]
        public static partial void LogWebSocketClientOnClose(
            this ILogger logger,
            string reason,
            bool wasClean);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientOnError",
            Level = LogLevel.Debug,
            Message = "SIPMessagWebSocketBehavior.OnError: reason {Message}.")]
        public static partial void LogWebSocketClientOnError(
            this ILogger logger,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketChannelClosing",
            Level = LogLevel.Debug,
            Message = "Closing SIP Web Socket Channel {ListeningEndPoint}.")]
        public static partial void LogWebSocketChannelClosing(
            this ILogger logger,
            IPEndPoint listeningEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpChannelCreated",
            Level = LogLevel.Information,
            Message = "SIP {protDescr} Channel created for {listeningSIPEndPoint}.")]
        public static partial void LogTcpChannelCreated(
            this ILogger logger,
            string protDescr,
            SIPEndPoint listeningSIPEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "SocketExceptionEndReceiveFrom",
            Level = LogLevel.Warning,
            Message = "SocketException SIPUDPChannel EndReceiveFrom ({ErrorCode}). {ErrorMessage}")]
        public static partial void LogSocketExceptionEndReceiveFrom(
            this ILogger logger,
            int errorCode,
            string errorMessage);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpConnectionError",
            Level = LogLevel.Error,
            Message = "Exception SIPTCPChannel processing receive tasks. {ErrorMessage}")]
        public static partial void LogTcpConnectionError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientClose",
            Level = LogLevel.Warning,
            Message = "Exception SIPWebSocketChannel Close. {ErrorMessage}")]
        public static partial void LogWebSocketClientClose(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientMonitorError",
            Level = LogLevel.Error,
            Message = "Exception SIPClientWebSocketChannel.MonitorReceiveTasks. {ErrorMessage}")]
        public static partial void LogWebSocketClientMonitorError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsStreamError",
            Level = LogLevel.Error,
            Message = "Exception ConnectClientAsync. {ErrorMessage}")]
        public static partial void LogTlsStreamError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpChannelClose",
            Level = LogLevel.Error,
            Message = "Exception closing SIP connection on {ProtDescr}. {ErrorMessage}")]
        public static partial void LogTcpChannelClose(
            this ILogger logger,
            string protDescr,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientMonitorError",
            Level = LogLevel.Error,
            Message = "Exception processing SIP {ProtDescr} stream receive on read from {RemoteEndPoint} closing connection. {ErrorMessage}")]
        public static partial void LogTcpStreamReceiveError(
            this ILogger logger,
            string protDescr,
            EndPoint remoteEndPoint,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketSendError",
            Level = LogLevel.Warning,
            Message = "SIP {ProtDescr} Channel Socket send to {RemoteEndPoint} failed with socket error {SocketError}, removing connection.")]
        public static partial void LogTcpSocketSendError(
            this ILogger logger,
            string protDescr,
            EndPoint remoteEndPoint,
            SocketError socketError);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketSendException",
            Level = LogLevel.Warning,
            Message = "SocketException SIP {ProtDescr} Channel SendOnConnected {RemoteSIPEndPoint}. ErrorCode {ErrorCode}. {ErrorMessage}")]
        public static partial void LogTcpSocketSendException(
            this ILogger logger,
            string protDescr,
            SIPEndPoint remoteSIPEndPoint,
            SocketError errorCode,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketPruningException",
            Level = LogLevel.Error,
            Message = "Exception SIP {ProtDescr} Channel PruneConnections. {Message}")]
        public static partial void LogTcpSocketPruningException(
            this ILogger logger,
            string protDescr,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketPruningError",
            Level = LogLevel.Warning,
            Message = "Socket error in PruneConnections. {Message} ({ErrorCode}).")]
        public static partial void LogTcpSocketPruningError(
            this ILogger logger,
            string message,
            int errorCode,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketChannelSendError",
            Level = LogLevel.Error,
            Message = "Could not add web socket client connected to {ServerUri} to channel collection, closing.")]
        public static partial void LogWebSocketChannelSendError(
            this ILogger logger,
            Uri serverUri);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientClose",
            Level = LogLevel.Warning,
            Message = "Client web socket connection to {ServerUri} returned without completing, closing.")]
        public static partial void LogWebSocketClientConnectionClose(
            this ILogger logger,
            Uri serverUri);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsStreamSendError",
            Level = LogLevel.Warning,
            Message = "SocketException SIP TLS Channel sending to {RemoteSIPEndPoint}. ErrorCode {SocketErrorCode}. {ErrorMessage}")]
        public static partial void LogTlsStreamSendError(
            this ILogger logger,
            SIPEndPoint remoteSIPEndPoint,
            SocketError socketErrorCode,
            string errorMessage);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsCertificateError",
            Level = LogLevel.Warning,
            Message = "Certificate error: {SslPolicyErrors}")]
        public static partial void LogTlsCertificateError(
            this ILogger logger,
            SslPolicyErrors sslPolicyErrors);

        [LoggerMessage(
            EventId = 0,
            EventName = "TlsCloseError",
            Level = LogLevel.Warning,
            Message = "IOException SIPTLSChannel OnReadCallback. {ErrorMessage}")]
        public static partial void LogTlsCloseError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketListenerError",
            Level = LogLevel.Error,
            Message = "Exception SIP {ProtDescr} Channel Close (shutting down listener). {Message}")]
        public static partial void LogTcpSocketListenerError(
            this ILogger logger,
            string protDescr,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketDisconnected",
            Level = LogLevel.Warning,
            Message = "SIP {ProtDescr} stream disconnected {RemoteSIPEndPoint} {SocketError}")]
        public static partial void LogTcpSocketDisconnected(
            this ILogger logger,
            string protDescr,
            SIPEndPoint remoteSIPEndPoint,
            SocketError socketError);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSocketPruningError",
            Level = LogLevel.Error,
            Message = "Exception PruneConnections (pruning). {Message}")]
        public static partial void LogTcpSocketPruningMessage(
            this ILogger logger,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpSocketCloseError",
            Level = LogLevel.Warning,
            Message = "Exception SIPUDPChannel Close. {Message}")]
        public static partial void LogUdpSocketCloseError(
            this ILogger logger,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpFailedSendsError",
            Level = LogLevel.Error,
            Message = "Exception SIPUDPChannel.ExpireFailedSends. {ErrorMessage}")]
        public static partial void LogUdpFailedSendsError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelError",
            Level = LogLevel.Error,
            Message = "Exception SIPUDPChannel EndReceiveFrom. {ErrorMessage}")]
        public static partial void LogUdpChannelError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpChannelSendToError",
            Level = LogLevel.Error,
            Message = "Exception SIPUDPChannel EndSendTo. {ErrorMessage}")]
        public static partial void LogUdpChannelSendToError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSendLocalBlockError",
            Level = LogLevel.Warning,
            Message = "SIP {ProtDescr} Channel blocked Send to {DestinationSipEndPoint} as it was identified as a locally hosted. \r\n{SocketError}")]
        public static partial void LogTcpSendLocalBlockError(
            this ILogger logger,
            string protDescr,
            SIPEndPoint destinationSipEndPoint,
            string socketError);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpDestinationError",
            Level = LogLevel.Warning,
            Message = "SIP {ProtDescr} Channel send to {dstEndPoint} failed. Attempt to create a client socket failed with {SocketError}")]
        public static partial void LogTcpDestinationError(
            this ILogger logger,
            string protDescr,
            IPEndPoint dstEndPoint,
            SocketError socketError);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpConnectionInitiateError",
            Level = LogLevel.Warning,
            Message = "SIP {ProtDescr} Channel did not have an existing connection for send to {dstSIPEndPoint} and requested not to initiate a connection")]
        public static partial void LogTcpConnectionInitiateError(
            this ILogger logger,
            string protDescr,
            SIPEndPoint dstSIPEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSendError",
            Level = LogLevel.Warning,
            Message = "SIP {ProtDescr} Channel send to {DestinationEndPoint} failed. Attempt to create a client socket failed with {SocketError}")]
        public static partial void LogTcpSendError(
            this ILogger logger,
            string protDescr,
            IPEndPoint destinationEndPoint,
            SocketError socketError);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpChannelSendError",
            Level = LogLevel.Error,
            Message = "Exception SIPTCPChannel Send (sendto=>{DestinationEndPoint}). {ErrorMessage}")]
        public static partial void LogTcpChannelSendError(
            this ILogger logger,
            SIPEndPoint destinationEndPoint,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UdpEndSendToError",
            Level = LogLevel.Warning,
            Message = "SocketException SIPUDPChannel EndSendTo ({ErrorCode}). {ErrorMessage}")]
        public static partial void LogUdpEndSendToError(
            this ILogger logger,
            int errorCode,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ClientWebSocketClose",
            Level = LogLevel.Warning,
            Message = "Exception SIPClientWebSocketChannel Close. {ErrorMessage}")]
        public static partial void LogClientWebSocketClose(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpStreamDisconnectionWarning",
            Level = LogLevel.Warning,
            Message = "SIP {ProtDescr} stream disconnected {RemoteSIPEndPoint} {SocketError}.")]
        public static partial void LogTcpStreamDisconnectionWarning(
            this ILogger logger,
            string protDescr,
            SIPEndPoint remoteSIPEndPoint,
            SocketError socketError);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpConnectFailure",
            Level = LogLevel.Warning,
            Message = "SIP {ProtDescr} Channel send to {DstEndPoint} failed. Attempt to connect to server at {ServerEndPoint} failed with {Result}.")]
        public static partial void LogTcpConnectFailure(
            this ILogger logger,
            string protDescr,
            IPEndPoint dstEndPoint,
            IPEndPoint serverEndPoint,
            SocketError result);

        [LoggerMessage(
            EventId = 0,
            EventName = "MonitorReceiveTasksError",
            Level = LogLevel.Error,
            Message = "Exception SIPClientWebSocketChannel.MonitorReceiveTasks. {ErrorMessage}")]
        public static partial void LogMonitorReceiveTasksError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ConnectClientError",
            Level = LogLevel.Error,
            Message = "Exception ConnectClientAsync. {ErrorMessage}")]
        public static partial void LogConnectClientError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SIPChannelError", 
            Level = LogLevel.Error, 
            Message = "Channel error {Error}")]
        public static partial void LogSIPChannelError(
            this ILogger logger,
            string error,
            Exception ex);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SIPChannelSocketError", 
            Level = LogLevel.Warning, 
            Message = "Socket error {SocketError} in SIP channel")]
        public static partial void LogSIPChannelSocketError(
            this ILogger logger,
            int socketError);
    }
}
