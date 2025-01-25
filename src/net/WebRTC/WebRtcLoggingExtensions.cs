using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Tls;

namespace SIPSorcery.Net
{
    internal static partial class WebRtcLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsCertificate",
            Level = LogLevel.Trace,
            Message = "-----BEGIN CERTIFICATE-----\n{Certificate}\n-----END CERTIFICATE-----",
            SkipEnabledCheck = true)]
        private static partial void LogDtlsCertificateImpl(
            this ILogger logger,
            string certificate);

        public static void LogDtlsCertificate(
            this ILogger logger,
            Certificate dtlsCertificate)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogDtlsCertificateImpl(DtlsUtils.ExportToDerBase64(dtlsCertificate));
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "RemoteCertificate",
            Level = LogLevel.Trace,
            Message = "Remote peer DTLS certificate, signature algorithm {RemoteCertificateSignatureAlgorithm}.\n-----BEGIN CERTIFICATE-----\n{Certificate}\n-----END CERTIFICATE-----",
            SkipEnabledCheck = true)]
        private static partial void LogRemoteCertificateImpl(
            this ILogger logger, 
            string remoteCertificateSignatureAlgorithm, 
            string certificate);

        public static void LogRemoteCertificate(
            this ILogger logger, 
            X509CertificateStructure remoteCertificate)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogRemoteCertificateImpl(DtlsUtils.GetSignatureAlgorithm(remoteCertificate), Convert.ToBase64String(remoteCertificate.GetDerEncoded()));
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelOpen",
            Level = LogLevel.Debug,
            Message = "Data channel for label {Label} now open.")]
        public static partial void LogWebRtcDataChannelOpen(
            this ILogger logger,
            string label);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelClose",
            Level = LogLevel.Debug,
            Message = "Data channel with id {Id} has been closed.")]
        public static partial void LogWebRtcDataChannelClose(
            this ILogger logger,
            ushort? id);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcPeerConnectionClose",
            Level = LogLevel.Debug,
            Message = "Peer connection closed with reason {Reason}.")]
        public static partial void LogWebRtcPeerConnectionClose(
            this ILogger logger,
            string reason);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelCreate",
            Level = LogLevel.Debug,
            Message = "Data channel create request for label {Label}.")]
        public static partial void LogWebRtcDataChannelCreate(
            this ILogger logger,
            string label);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSctpConnecting",
            Level = LogLevel.Debug,
            Message = "SCTP transport for create data channel request changed to state {State}.")]
        public static partial void LogWebRtcSctpConnecting(
            this ILogger logger,
            RTCSctpTransportState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcCertificateCreated",
            Level = LogLevel.Debug,
            Message = "RTCPeerConnection created with DTLS certificate with fingerprint {DtlsCertificateFingerprint} and signature algorithm {DtlsCertificateSignatureAlgorithm}.")]
        public static partial void LogWebRtcCertificateCreated(
            this ILogger logger,
            RTCDtlsFingerprint dtlsCertificateFingerprint,
            string dtlsCertificateSignatureAlgorithm);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSctpTransportConnected",
            Level = LogLevel.Debug,
            Message = "SCTP transport successfully connected.")]
        public static partial void LogWebRtcSctpTransportConnected(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcNewDataChannel",
            Level = LogLevel.Information,
            Message = "WebRTC new data channel opened by remote peer for stream ID {StreamID}, type {Type}, priority {Priority}, reliability {Reliability}, label {Label}, protocol {Protocol}.")]
        public static partial void LogWebRtcNewDataChannel(
            this ILogger logger,
            ushort streamID,
            DataChannelTypes type,
            ushort priority,
            uint reliability,
            string label,
            string protocol);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsHandshakeStarted",
            Level = LogLevel.Debug,
            Message = "Starting DLS handshake with role {IceRole}.")]
        public static partial void LogWebRtcDtlsHandshakeStarted(
            this ILogger logger,
            IceRolesEnum iceRole);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsHandshakeStarting",
            Level = LogLevel.Debug,
            Message = "RTCPeerConnection DoDtlsHandshake started.")]
        public static partial void LogWebRtcDtlsHandshakeStarting(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientConnecting",
            Level = LogLevel.Debug,
            Message = "websocket-client attempting to connect to {WebSocketServerUri}.")]
        public static partial void LogWebSocketClientConnecting(
            this ILogger logger,
            Uri webSocketServerUri);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientStartReceive",
            Level = LogLevel.Debug,
            Message = "websocket-client starting receive task for server {WebSocketServerUri}.")]
        public static partial void LogWebSocketClientStartReceive(
            this ILogger logger,
            Uri webSocketServerUri);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientSendingIceCandidate",
            Level = LogLevel.Debug,
            Message = "WebRTCWebSocketClient sending ICE candidate to server.")]
        public static partial void LogWebSocketClientSendingIceCandidate(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientReceiveLoopExit",
            Level = LogLevel.Debug,
            Message = "websocket-client receive loop exiting.")]
        public static partial void LogWebSocketClientReceiveLoopExit(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientGotRemoteIceCandidate",
            Level = LogLevel.Debug,
            Message = "Got remote ICE candidate.")]
        public static partial void LogWebSocketClientGotRemoteIceCandidate(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientGotRemoteSdp",
            Level = LogLevel.Debug,
            Message = "Got remote SDP, type {SdpType}.")]
        public static partial void LogWebSocketClientGotRemoteSdp(
            this ILogger logger,
            RTCSdpType sdpType);

        [LoggerMessage(
            EventId = 0,
            EventName = "SctpAssociationCreating",
            Level = LogLevel.Debug,
            Message = "SCTP creating DTLS based association, is DTLS client {IsDtlsClient}, ID {AssociationId}.")]
        public static partial void LogSctpAssociationCreating(
            this ILogger logger,
            bool isDtlsClient,
            string associationId);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDcepOpen",
            Level = LogLevel.Debug,
            Message = "DCEP OPEN channel type {ChannelType}, priority {Priority}, reliability {Reliability}, label {Label}, protocol {Protocol}.")]
        public static partial void LogWebRtcDcepOpen(
            this ILogger logger,
            byte channelType,
            ushort priority,
            uint reliability,
            string label,
            string protocol);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcNoCertificate",
            Level = LogLevel.Debug,
            Message = "No DTLS certificate is provided in the configuration")]
        public static partial void LogWebRtcNoCertificate(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelSendFailed",
            Level = LogLevel.Warning,
            Message = "WebRTC data channel send failed due to SCTP transport in state {TransportState}.")]
        public static partial void LogWebRtcDataChannelSendFailed(
            this ILogger logger,
            RTCSctpTransportState transportState);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelIdOpenAttemptFailed",
            Level = LogLevel.Error,
            Message = "Attempt to open a data channel without an assigned ID has failed.")]
        public static partial void LogWebRtcDataChannelIdOpenAttemptFailed(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcIceRemoteEndpointChange",
            Level = LogLevel.Debug,
            Message = "ICE changing connected remote end point to {ConnectedEndpoint}.")]
        public static partial void LogWebRtcIceRemoteEndpointChange(
            this ILogger logger,
            IPEndPoint connectedEndpoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcIceConnected",
            Level = LogLevel.Debug,
            Message = "ICE connected to remote end point {ConnectedEndpoint}.")]
        public static partial void LogWebRtcIceConnected(
            this ILogger logger,
            IPEndPoint connectedEndpoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelOpenAttempt",
            Level = LogLevel.Debug,
            Message = "WebRTC attempting to open data channel with label {Label} and stream ID {StreamID}.")]
        public static partial void LogWebRtcDataChannelOpenAttempt(
            this ILogger logger,
            string label,
            ushort? streamID);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcRemoteDescription",
            Level = LogLevel.Debug,
            Message = "[setRemoteDescription] - Extension:[{Id} - {Uri}]")]
        public static partial void LogWebRtcRemoteDescription(
            this ILogger logger,
            int id,
            string uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsHandshakeResult",
            Level = LogLevel.Debug,
            Message = "RTCPeerConnection DTLS handshake result {HandshakeResult}, is handshake complete {IsHandshakeComplete}.")]
        public static partial void LogWebRtcDtlsHandshakeResult(
            this ILogger logger,
            bool handshakeResult,
            bool isHandshakeComplete);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcRemoteCertificateFingerprint",
            Level = LogLevel.Debug,
            Message = "RTCPeerConnection remote certificate fingerprint matched expected value of {RemoteFingerprintValue} for {RemoteFingerprintAlgorithm}.")]
        public static partial void LogWebRtcRemoteCertificateFingerprint(
            this ILogger logger,
            string remoteFingerprintValue,
            string remoteFingerprintAlgorithm);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSctpTransportClose",
            Level = LogLevel.Debug,
            Message = "SCTP closing transport as a result of DTLS close notification.")]
        public static partial void LogWebRtcSctpTransportClose(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcCreateOfferHeaderExtension",
            Level = LogLevel.Debug,
            Message = "[createOffer] - {Media}:[{MediaID}] - Add HeaderExtensions:[{Id} - {Uri}]")]
        public static partial void LogWebRtcCreateOfferHeaderExtension(
            this ILogger logger,
            SDPMediaTypesEnum media,
            string mediaID,
            int id,
            string uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcCreateAnswerHeaderExtension",
            Level = LogLevel.Debug,
            Message = "[createAnswer] - {Media}:[{MediaID}] - Add HeaderExtensions:[{Id} - {Uri}]")]
        public static partial void LogWebRtcCreateAnswerHeaderExtension(
            this ILogger logger,
            SDPMediaTypesEnum media,
            string mediaID,
            int id,
            string uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelOpened",
            Level = LogLevel.Debug,
            Message = "WebRTC data channel opened label {Label} and stream ID {StreamID}.")]
        public static partial void LogWebRtcDataChannelOpened(
            this ILogger logger,
            string label,
            ushort streamID);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDcepDataChunk",
            Level = LogLevel.Trace,
            Message = "WebRTC data channel GotData stream ID {StreamID}, stream seqnum {StreamSeqNum}, ppid {PpID}, label {Label}.")]
        public static partial void LogWebRtcDcepDataChunk(
            this ILogger logger,
            ushort streamID,
            ushort streamSeqNum,
            uint ppID,
            string label);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelForStreamId",
            Level = LogLevel.Warning,
            Message = "WebRTC data channel got data but no channel found for stream ID {StreamID}.")]
        public static partial void LogWebRtcDataChannelForStreamId(
            this ILogger logger,
            ushort streamID);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelNegotiated",
            Level = LogLevel.Debug,
            Message = "WebRTC data channel negotiated out of band with label {Label} and stream ID {StreamID}; invoking open event")]
        public static partial void LogWebRtcDataChannelNegotiated(
            this ILogger logger,
            string label,
            ushort? streamID);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientConnection",
            Level = LogLevel.Debug,
            Message = "Web socket client connection from {UserEndPoint}.")]
        public static partial void LogWebSocketClientConnection(
            this ILogger logger,
            object userEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientSdpOffer",
            Level = LogLevel.Debug,
            Message = "Sending SDP offer to client {UserEndPoint}.")]
        public static partial void LogWebSocketClientSdpOffer(
            this ILogger logger,
            object userEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebSocketClientSdpAnswer",
            Level = LogLevel.Debug,
            Message = "Sending SDP answer to client {UserEndPoint}.")]
        public static partial void LogWebSocketClientSdpAnswer(
            this ILogger logger,
            object userEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcSctpInit",
            Level = LogLevel.Debug,
            Message = "SCTP INIT packet received, initial tag {InitiateTag}, initial TSN {InitialTSN}.")]
        public static partial void LogRtcSctpInit(
            this ILogger logger,
            uint initiateTag,
            uint initialTSN);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcSctpWarning",
            Level = LogLevel.Warning,
            Message = "SCTP error acquiring handshake cookie from COOKIE ECHO chunk.")]
        public static partial void LogRtcSctpWarning(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcSctpAssociation",
            Level = LogLevel.Warning,
            Message = "SCTP association {ID} receive thread stopped.")]
        public static partial void LogRtcSctpAssociation(
            this ILogger logger,
            string id);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcSctpReceive",
            Level = LogLevel.Warning,
            Message = "SCTP the RTCSctpTransport DTLS transport returned an error.")]
        public static partial void LogRtcSctpReceive(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingPeerExcludeIceCandidate",
            Level = LogLevel.Debug,
            Message = "WebRTCWebSocketPeer excluding ICE candidate due to filter: {Candidate}")]
        public static partial void LogWebRtcSignalingPeerExcludeIceCandidate(
            this ILogger logger,
            string candidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingJsonParseFailed",
            Level = LogLevel.Warning,
            Message = "websocket-server could not parse JSON message. {MessageData}")]
        public static partial void LogWebRtcSignalingJsonParseFailed(
            this ILogger logger,
            string messageData);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingRestStart",
            Level = LogLevel.Debug,
            Message = "webrtc-rest starting receive task for server {RestServerUri}, our ID {OurID} and their ID {TheirID}.")]
        public static partial void LogWebRtcSignalingRestStart(
            this ILogger logger,
            Uri restServerUri,
            string ourID,
            string theirID);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingRestSendOffer",
            Level = LogLevel.Debug,
            Message = "webrtc-rest sending initial SDP offer to server.")]
        public static partial void LogWebRtcSignalingRestSendOffer(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingRestPut",
            Level = LogLevel.Debug,
            Message = "webrtc-rest PUT result for {RestServerUri}/{SendType}/{OurID}/{TheirID} {StatusCode}.")]
        public static partial void LogWebRtcSignalingRestPut(
            this ILogger logger,
            Uri restServerUri,
            WebRTCSignalTypesEnum sendType,
            string ourID,
            string theirID,
            HttpStatusCode statusCode);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingRestRetry",
            Level = LogLevel.Debug,
            Message = "webrtc-rest server initial connection attempt failed, will retry in {RetryPeriod}ms.")]
        public static partial void LogWebRtcSignalingRestRetry(
            this ILogger logger,
            int retryPeriod);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingRestReceiveExit",
            Level = LogLevel.Debug,
            Message = "webrtc-rest receive task exiting.")]
        public static partial void LogWebRtcSignalingRestReceiveExit(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingRestRemoteCandidate",
            Level = LogLevel.Debug,
            Message = "Got remote ICE candidate, {Candidate}")]
        public static partial void LogWebRtcSignalingRestRemoteCandidate(
            this ILogger logger,
            string candidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcReceiveLoopCancel",
            Level = LogLevel.Debug,
            Message = "cancelling HTTP receive task.")]
        public static partial void LogWebRtcReceiveLoopCancel(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcIceCandidate",
            Level = LogLevel.Debug,
            Message = "webrtc-rest onicecandidate: {CandidateStr}",
            SkipEnabledCheck = true)]
        private static partial void LogWebRtcIceCandidateUnchecked(
            this ILogger logger,
            string candidateStr);

        public static void LogWebRtcIceCandidate(
            this ILogger logger,
            RTCIceCandidate candidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogWebRtcIceCandidateUnchecked(candidate.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingFilterIceCandidate",
            Level = LogLevel.Debug,
            Message = "WebRTCRestPeer excluding ICE candidate due to filter: {Candidate}")]
        public static partial void LogWebRtcSignalingFilterIceCandidate(
            this ILogger logger,
            string candidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSignalingStateRejectOffer",
            Level = LogLevel.Warning,
            Message = "RTCPeerConnection received an SDP offer but was already in {signalingState} state. Remote offer rejected.")]
        public static partial void LogWebRtcSignalingStateRejectOffer(
            this ILogger logger,
            RTCSignalingState signalingState);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataTransportUnsupported",
            Level = LogLevel.Warning,
            Message = "The remote SDP requested an unsupported data channel transport of {transport}.")]
        public static partial void LogWebRtcDataTransportUnsupported(
            this ILogger logger,
            string transport);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsFingerprintInvalid",
            Level = LogLevel.Warning,
            Message = "The DTLS fingerprint was invalid or not supported.")]
        public static partial void LogWebRtcDtlsFingerprintInvalid(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsFingerprintMissing",
            Level = LogLevel.Warning,
            Message = "The DTLS fingerprint was missing from the remote party's session description.")]
        public static partial void LogWebRtcDtlsFingerprintMissing(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcIceComponentError",
            Level = LogLevel.Warning,
            Message = "Remote ICE candidate not added as no available ICE session for component {component}.")]
        public static partial void LogWebRtcIceComponentError(
            this ILogger logger,
            int component);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsAlert",
            Level = LogLevel.Warning,
            Message = "DTLS unexpected {alertLevel} alert {alertType}{alertMsg}")]
        public static partial void LogWebRtcDtlsAlert(
            this ILogger logger,
            AlertLevelsEnum alertLevel,
            AlertTypesEnum alertType,
            string alertMsg);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcIcePortStateError",
            Level = LogLevel.Warning,
            Message = "SCTP source port cannot be updated when the transport is in state {state}.")]
        public static partial void LogWebRtcIcePortStateError(
            this ILogger logger,
            RTCSctpTransportState state);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDuplicateDataChannel",
            Level = LogLevel.Warning,
            Message = "WebRTC duplicate data channel requested for stream ID {streamId}.")]
        public static partial void LogWebRtcDuplicateDataChannel(
            this ILogger logger,
            ushort streamId);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcScpError",
            Level = LogLevel.Error,
            Message = "SCTP fatal error processing RTCSctpTransport receive. {errorMessage}")]
        public static partial void LogWebRtcScpError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcIceSocketError",
            Level = LogLevel.Warning,
            Message = "SCTP RTCSctpTransport receive socket failure {socketErrorCode}.")]
        public static partial void LogWebRtcIceSocketError(
            this ILogger logger,
            SocketError socketErrorCode,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsHandshakeError",
            Level = LogLevel.Warning,
            Message = "RTCPeerConnection DTLS handshake failed. {errorMessage}")]
        public static partial void LogWebRtcDtlsHandshakeError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSctpEstablishError",
            Level = LogLevel.Error,
            Message = "SCTP exception establishing association, data channels will not be available. {errorMessage}")]
        public static partial void LogWebRtcSctpEstablishError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsFingerprintMismatch",
            Level = LogLevel.Warning,
            Message = "RTCPeerConnection remote certificate fingerprint mismatch, expected {expectedFingerprint}, actual {remoteFingerprint}.")]
        public static partial void LogWebRtcDtlsFingerprintMismatch(
            this ILogger logger,
            RTCDtlsFingerprint expectedFingerprint,
            RTCDtlsFingerprint remoteFingerprint);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSetDescriptionError",
            Level = LogLevel.Warning,
            Message = "Failed to set remote description, {result}.")]
        public static partial void LogWebRtcSetDescriptionError(
            this ILogger logger,
            SetDescriptionResultEnum result);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsRecvNoTransport",
            Level = LogLevel.Warning,
            Message = "DTLS packet received {bufferLength} bytes from {remoteEndPoint} but no DTLS transport available.")]
        public static partial void LogWebRtcDtlsRecvNoTransport(
            this ILogger logger,
            int bufferLength,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcCheckpointExcluded",
            Level = LogLevel.Warning,
            Message = "Media announcement for {kind} omitted due to no reciprocal remote announcement.")]
        public static partial void LogWebRtcCheckpointExcluded(
            this ILogger logger,
            SDPMediaTypesEnum kind);

        [LoggerMessage(
            EventId = 0, 
            EventName = "WebRtcDataChannelNotFound",
            Level = LogLevel.Warning,
            Message = "WebRTC data channel got ACK but data channel not found for stream ID {streamId}.")]
        public static partial void LogWebRtcDataChannelNotFound(
            this ILogger logger,
            ushort streamId);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDcepUnrecognized",
            Level = LogLevel.Warning,
            Message = "DCEP message type {messageType} not recognised, ignoring.")]
        public static partial void LogWebRtcDcepUnrecognized(
            this ILogger logger,
            byte messageType);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcGatheringTimeout",
            Level = LogLevel.Warning,
            Message = "ICE gathering timed out after {gatherTimeoutMs}Ms")]
        public static partial void LogWebRtcGatheringTimeout(
            this ILogger logger,
            int gatherTimeoutMs);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDcepUnknownChannelType",
            Level = LogLevel.Warning,
            Message = "DECP OPEN channel type of {channelType} not recognised, defaulting to {defaultChannelType}.")]
        public static partial void LogWebRtcDcepUnknownChannelType(
            this ILogger logger,
            byte channelType,
            DataChannelTypes defaultChannelType);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSendOfferError",
            Level = LogLevel.Error,
            Message = "An error has occurred during the OnOpen event.")]
        public static partial void LogWebRtcSendOfferError(
            this ILogger logger,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtcSctpDiscardedPacket",
            Level = LogLevel.Warning,
            Message = "SCTP packet received on DTLS transport dropped due to invalid checksum.")]
        public static partial void LogRtcSctpDiscardedPacket(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "WebRtcCertificateFingerprint", 
            Level = LogLevel.Warning,
            Message = "RTCPeerConnection was passed a certificate for {friendlyName} with a non-exportable RSA private key.")]
        public static partial void LogWebRtcCertificateFingerprint(
            this ILogger logger,
            string friendlyName);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcSctpProcessError",
            Level = LogLevel.Warning,
            Message = "SCTP error processing RTCSctpTransport receive. {message}")]
        public static partial void LogWebRtcSctpProcessError(
            this ILogger logger, 
            string message);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcIceSessionError", 
            Level = LogLevel.Warning,
            Message = "Remote ICE candidate not added as no available ICE session for component {component}.")]
        public static partial void LogWebRtcIceSessionError(
            this ILogger logger,
            RTCIceComponent component);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDataChannelIdError",
            Level = LogLevel.Warning,
            Message = "WebRTC data channel got ACK but data channel not found for stream ID {streamId}.")]
        public static partial void LogWebRtcDataChannelIdError(
            this ILogger logger,
            ushort streamId);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcRestServerError",
            Level = LogLevel.Warning,
            Message = "webrtc-rest server connection attempt failed.")]
        public static partial void LogWebRtcRestServerError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcRestServerDecodeError",
            Level = LogLevel.Warning,
            Message = "webrtc-rest could not parse JSON message. {signal}")]
        public static partial void LogWebRtcRestServerDecodeError(
            this ILogger logger,
            string signal);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcRestTaskError",
            Level = LogLevel.Error,
            Message = "Exception receiving webrtc signal. {errorMessage}")]
        public static partial void LogWebRtcRestTaskError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcDtlsHandshakeWarn",
            Level = LogLevel.Warning,
            Message = "RTCPeerConnection DTLS handshake failed with error {handshakeError}.")]
        public static partial void LogWebRtcDtlsHandshakeWarn(
            this ILogger logger,
            string handshakeError);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcRtpDataReceiveError",
            Level = LogLevel.Error,
            Message = "Exception RTCPeerConnection.OnRTPDataReceived {errorMessage}")]
        public static partial void LogWebRtcRtpDataReceiveError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "WebRtcMediaAnnouncementWarn",
            Level = LogLevel.Warning,
            Message = "Media announcement for data channel establishment omitted due to no reciprocal remote announcement.")]
        public static partial void LogWebRtcMediaAnnouncementWarn(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "WebRtcSessionDescription", 
            Level = LogLevel.Debug, 
            Message = "WebRTC local session description set to {Type}.")]
        public static partial void LogWebRtcSessionDescription(
            this ILogger logger,
            string type);

        [LoggerMessage(
            EventId = 0, 
            EventName = "WebRtcRemoteDescription", 
            Level = LogLevel.Debug, 
            Message = "WebRTC remote description set to {Type}.")]
        public static partial void LogWebRtcRemoteDescription(
            this ILogger logger,
            string type);
    }
}
