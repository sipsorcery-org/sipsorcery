using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.SharpSRTP.DTLS;

namespace SIPSorcery.Net;

internal static partial class WebRtcLoggingExtensions
{
    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDataChannelOpen",
        Level = LogLevel.Debug,
        Message = "Data channel for label {Label} now open.")]
    public static partial void LogWebRtcDataChannelOpen(
        this ILogger logger,
        string? label);

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
        string? protocol);

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
        string? protocol);

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
        string? label,
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
        string? remoteFingerprintValue,
        string? remoteFingerprintAlgorithm);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcClosePeerConnection",
        Level = LogLevel.Debug,
        Message = "Closing peer connection as a result of DTLS close notification.")]
    public static partial void LogWebRtcClosePeerConnection(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcCreateOfferHeaderExtension",
        Level = LogLevel.Debug,
        Message = "[createOffer] - {Media}:[{MediaID}] - Add HeaderExtensions:[{Id} - {Uri}]")]
    public static partial void LogWebRtcCreateOfferHeaderExtension(
        this ILogger logger,
        SDPMediaTypesEnum media,
        string? mediaID,
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
        string? mediaID,
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
        string? label,
        ushort? streamID);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketClientConnection",
        Level = LogLevel.Debug,
        Message = "Web socket client connection from {UserEndPoint}.")]
    public static partial void LogWebSocketClientConnection(
        this ILogger logger,
        string userEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketClientSdpOffer",
        Level = LogLevel.Debug,
        Message = "Sending SDP offer to client {UserEndPoint}.")]
    public static partial void LogWebSocketClientSdpOffer(
        this ILogger logger,
        string userEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketClientSdpAnswer",
        Level = LogLevel.Debug,
        Message = "Sending SDP answer to client {UserEndPoint}.")]
    public static partial void LogWebSocketClientSdpAnswer(
        this ILogger logger,
        string userEndPoint);

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
        Message = "RTCPeerConnection received an SDP offer but was already in {SignalingState} state. Remote offer rejected.")]
    public static partial void LogWebRtcSignalingStateRejectOffer(
        this ILogger logger,
        RTCSignalingState signalingState);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDataTransportUnsupported",
        Level = LogLevel.Warning,
        Message = "The remote SDP requested an unsupported data channel transport of {Transport}.")]
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
        Message = "Remote ICE candidate not added as no available ICE session for component {Component}.")]
    public static partial void LogWebRtcIceComponentError(
        this ILogger logger,
        int component);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDtlsAlert",
        Level = LogLevel.Warning,
        Message = "DTLS unexpected {AlertLevel} alert {AlertType}{AlertMsg}")]
    public static partial void LogWebRtcDtlsAlert(
        this ILogger logger,
        TlsAlertLevelsEnum alertLevel,
        TlsAlertTypesEnum alertType,
        string alertMsg);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcIcePortStateError",
        Level = LogLevel.Warning,
        Message = "SCTP source port cannot be updated when the transport is in state {State}.")]
    public static partial void LogWebRtcIcePortStateError(
        this ILogger logger,
        RTCSctpTransportState state);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDuplicateDataChannel",
        Level = LogLevel.Warning,
        Message = "WebRTC duplicate data channel requested for stream ID {StreamId}.")]
    public static partial void LogWebRtcDuplicateDataChannel(
        this ILogger logger,
        ushort streamId);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcScpError",
        Level = LogLevel.Error,
        Message = "SCTP fatal error processing RTCSctpTransport receive. {ErrorMessage}")]
    public static partial void LogWebRtcScpError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcIceSocketError",
        Level = LogLevel.Warning,
        Message = "SCTP RTCSctpTransport receive socket failure {SocketErrorCode}.")]
    public static partial void LogWebRtcIceSocketError(
        this ILogger logger,
        SocketError socketErrorCode,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDtlsHandshakeError",
        Level = LogLevel.Warning,
        Message = "RTCPeerConnection DTLS handshake failed. {ErrorMessage}")]
    public static partial void LogWebRtcDtlsHandshakeError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcSctpEstablishError",
        Level = LogLevel.Error,
        Message = "SCTP exception establishing association, data channels will not be available. {ErrorMessage}")]
    public static partial void LogWebRtcSctpEstablishError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDtlsFingerprintMismatch",
        Level = LogLevel.Warning,
        Message = "RTCPeerConnection remote certificate fingerprint mismatch, expected {ExpectedFingerprint}, actual {RemoteFingerprint}.")]
    public static partial void LogWebRtcDtlsFingerprintMismatch(
        this ILogger logger,
        RTCDtlsFingerprint expectedFingerprint,
        RTCDtlsFingerprint remoteFingerprint);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcSetDescriptionError",
        Level = LogLevel.Warning,
        Message = "Failed to set remote description, {Result}.")]
    public static partial void LogWebRtcSetDescriptionError(
        this ILogger logger,
        SetDescriptionResultEnum result);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDtlsRecvNoTransport",
        Level = LogLevel.Warning,
        Message = "DTLS packet received {BufferLength} bytes from {RemoteEndPoint} but no DTLS transport available.")]
    public static partial void LogWebRtcDtlsRecvNoTransport(
        this ILogger logger,
        int bufferLength,
        IPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcIceSourceFilterDrop",
        Level = LogLevel.Debug,
        Message = "Dropped {ByteCount} byte non-STUN packet from {RemoteEndPoint}; source does not match any known ICE remote candidate (issues #1559, #1731).")]
    public static partial void LogWebRtcIceSourceFilterDrop(
        this ILogger logger,
        int byteCount,
        IPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcCheckpointExcluded",
        Level = LogLevel.Warning,
        Message = "Media announcement for {Kind} omitted due to no reciprocal remote announcement.")]
    public static partial void LogWebRtcCheckpointExcluded(
        this ILogger logger,
        SDPMediaTypesEnum kind);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDataChannelNotFound",
        Level = LogLevel.Warning,
        Message = "WebRTC data channel got ACK but data channel not found for stream ID {StreamId}.")]
    public static partial void LogWebRtcDataChannelNotFound(
        this ILogger logger,
        ushort streamId);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDcepUnrecognized",
        Level = LogLevel.Warning,
        Message = "DCEP message type {MessageType} not recognised, ignoring.")]
    public static partial void LogWebRtcDcepUnrecognized(
        this ILogger logger,
        byte messageType);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcGatheringTimeout",
        Level = LogLevel.Warning,
        Message = "ICE gathering timed out after {GatherTimeoutMs}Ms")]
    public static partial void LogWebRtcGatheringTimeout(
        this ILogger logger,
        int gatherTimeoutMs);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDcepUnknownChannelType",
        Level = LogLevel.Warning,
        Message = "DECP OPEN channel type of {ChannelType} not recognised, defaulting to {DefaultChannelType}.")]
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
        Message = "RTCPeerConnection was passed a certificate for {FriendlyName} with a non-exportable RSA private key.")]
    public static partial void LogWebRtcCertificateFingerprint(
        this ILogger logger,
        string friendlyName);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcSctpProcessError",
        Level = LogLevel.Warning,
        Message = "SCTP error processing RTCSctpTransport receive. {Message}")]
    public static partial void LogWebRtcSctpProcessError(
        this ILogger logger,
        string message);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcIceSessionError",
        Level = LogLevel.Warning,
        Message = "Remote ICE candidate not added as no available ICE session for component {Component}.")]
    public static partial void LogWebRtcIceSessionError(
        this ILogger logger,
        RTCIceComponent component);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDataChannelIdError",
        Level = LogLevel.Warning,
        Message = "WebRTC data channel got ACK but data channel not found for stream ID {StreamId}.")]
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
        Message = "webrtc-rest could not parse JSON message. {Signal}")]
    public static partial void LogWebRtcRestServerDecodeError(
        this ILogger logger,
        string signal);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcRestTaskError",
        Level = LogLevel.Error,
        Message = "Exception receiving webrtc signal. {ErrorMessage}")]
    public static partial void LogWebRtcRestTaskError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcDtlsHandshakeWarn",
        Level = LogLevel.Warning,
        Message = "RTCPeerConnection DTLS handshake failed with error {HandshakeError}.")]
    public static partial void LogWebRtcDtlsHandshakeWarn(
        this ILogger logger,
        string handshakeError);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebRtcRtpDataReceiveError",
        Level = LogLevel.Error,
        Message = "Exception RTCPeerConnection.OnRTPDataReceived {ErrorMessage}")]
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
        EventName = "WebRtcGatheringCompleteTimeout",
        Level = LogLevel.Warning,
        Message = "Waiting for ICE gathering to complete timed out after {GatherTimeoutMs}ms")]
    public static partial void LogWebRtcGatheringCompleteTimeout(
        this ILogger logger,
        int gatherTimeoutMs);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketClientConnectionEstablished",
        Level = LogLevel.Debug,
        Message = "Web socket client connection established.")]
    public static partial void LogWebSocketClientConnectionEstablished(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "LocalIceCandidate",
        Level = LogLevel.Debug,
        Message = "Got local ICE candidate, {Candidate}.")]
    public static partial void LogLocalIceCandidate(
        this ILogger logger,
        string candidate);

    [LoggerMessage(
        EventId = 0,
        EventName = "PeerConnectionStateChanged",
        Level = LogLevel.Debug,
        Message = "{Caller} peer connection state changed to {State}.")]
    public static partial void LogPeerConnectionStateChanged(
        this ILogger logger,
        string caller,
        RTCPeerConnectionState state);

    [LoggerMessage(
        EventId = 0,
        EventName = "GeneratingSdpOffer",
        Level = LogLevel.Debug,
        Message = "Generating SDP offer to send to web socket client.")]
    public static partial void LogGeneratingSdpOffer(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SendingSdpOffer",
        Level = LogLevel.Debug,
        Message = "Sending SDP offer to web socket client.")]
    public static partial void LogSendingSdpOffer(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpAnswerToClient",
        Level = LogLevel.Debug,
        Message = "Sending SDP answer to client.")]
    public static partial void LogSdpAnswerToClient(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SendMessageError",
        Level = LogLevel.Error,
        Message = "An error has occurred sending web socket message to client.")]
    public static partial void LogSendMessageError(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReceiveLoopCommenced",
        Level = LogLevel.Debug,
        Message = "{Name} commenced start receiving.")]
    public static partial void LogReceiveLoopCommenced(
        this ILogger logger,
        string name);

    [LoggerMessage(
        EventId = 0,
        EventName = "CloseMessageReceived",
        Level = LogLevel.Debug,
        Message = "{Name} close message received from remote web socket client.")]
    public static partial void LogCloseMessageReceived(
        this ILogger logger,
        string name);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReceiveLoopCancelled",
        Level = LogLevel.Debug,
        Message = "{Caller} stopped due to application cancellation request.")]
    public static partial void LogReceiveLoopCancelled(
        this ILogger logger,
        string caller);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReceiveError",
        Level = LogLevel.Error,
        Message = "Error while receiving WebSocket.")]
    public static partial void LogReceiveError(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReceivedMessage",
        Level = LogLevel.Debug,
        Message = "Received message: {Message}")]
    public static partial void LogReceivedMessage(
        this ILogger logger,
        string message);

    [LoggerMessage(
        EventId = 0,
        EventName = "ConnectionClosed",
        Level = LogLevel.Debug,
        Message = "WebSocket connection closed.")]
    public static partial void LogConnectionClosed(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SocketConnectionFromUserEndPoint",
        Level = LogLevel.Debug,
        Message = "Web socket client connection from {UserEndPoint}.")]
    public static partial void LogSocketConnectionFromUserEndPoint(
        this ILogger logger,
        IPEndPoint userEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "SendingSdpOfferToClient",
        Level = LogLevel.Debug,
        Message = "Sending SDP offer to client {UserEndPoint}.")]
    public static partial void LogSendingSdpOfferToClient(
        this ILogger logger,
        string userEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "OnOpenError",
        Level = LogLevel.Error,
        Message = "An error has occurred during the OnOpen event.")]
    public static partial void LogOnOpenError(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SignalingJsonParseFailedServer",
        Level = LogLevel.Warning,
        Message = "websocket-server could not parse JSON message. {MessageData}")]
    public static partial void LogSignalingJsonParseFailedServer(
        this ILogger logger,
        string messageData);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpAnswerToRemoteClient",
        Level = LogLevel.Debug,
        Message = "Sending SDP answer to client {UserEndPoint}.")]
    public static partial void LogSdpAnswerToRemoteClient(
        this ILogger logger,
        IPEndPoint userEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "FailedSetRemoteDescriptionWithSDP",
        Level = LogLevel.Warning,
        Message = "Failed to set remote description, {Result}\n{RemoteSDP}.")]
    public static partial void LogFailedSetRemoteDescriptionWithSDP(
        this ILogger logger,
        SetDescriptionResultEnum result,
        string remoteSdp);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketParseError",
        Level = LogLevel.Warning,
        Message = "WebSocket server could not parse JSON message. {MessageData}")]
    public static partial void LogWebSocketParseError(
        this ILogger logger,
        string messageData);

    [LoggerMessage(
        EventId = 0,
        EventName = "RestReceiveTaskExit",
        Level = LogLevel.Debug,
        Message = "webrtc-rest receive task exiting.")]
    public static partial void LogRestReceiveTaskExit(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "RestServerConnectionAttemptFailed",
        Level = LogLevel.Warning,
        Message = "webrtc-rest server connection attempt failed.")]
    public static partial void LogRestServerConnectionAttemptFailed(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SignalingReceiveError",
        Level = LogLevel.Error,
        Message = "Exception receiving webrtc signal. {ErrorMessage}")]
    public static partial void LogSignalingReceiveError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SignalingJsonParseFailedRest",
        Level = LogLevel.Warning,
        Message = "webrtc-rest could not parse JSON message. {Signal}")]
    public static partial void LogSignalingJsonParseFailedRest(
        this ILogger logger,
        string signal);

    [LoggerMessage(
        EventId = 0,
        EventName = "RestRetryPeriod",
        Level = LogLevel.Debug,
        Message = "webrtc-rest server initial connection attempt failed, will retry in {RetryPeriod}ms.")]
    public static partial void LogRestRetryPeriod(
        this ILogger logger,
        int retryPeriod);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpOfferToClientEndPoint",
        Level = LogLevel.Debug,
        Message = "Sending SDP offer to client {UserEndPoint}.")]
    public static partial void LogSdpOfferToClientEndPoint(
        this ILogger logger,
        IPEndPoint userEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "SignalingCancelReceiveTask",
        Level = LogLevel.Debug,
        Message = "cancelling HTTP receive task.")]
    public static partial void LogSignalingCancelReceiveTask(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "RestPutResult",
        Level = LogLevel.Debug,
        Message = "webrtc-rest PUT result for {RestServerUri}/{SendType}/{OurID}/{TheirID} {StatusCode}.")]
    public static partial void LogRestPutResult(
        this ILogger logger,
        Uri restServerUri,
        WebRTCSignalTypesEnum sendType,
        string ourId,
        string theirId,
        HttpStatusCode statusCode);

    [LoggerMessage(
        EventId = 0,
        EventName = "RestStartReceive",
        Level = LogLevel.Debug,
        Message = "webrtc-rest starting receive task for server {RestServerUri}, our ID {OurID} and their ID {TheirID}.")]
    public static partial void LogRestStartReceive(
        this ILogger logger,
        Uri restServerUri,
        string ourId,
        string theirId);

    [LoggerMessage(
        EventId = 0,
        EventName = "InitialSdpOffer",
        Level = LogLevel.Debug,
        Message = "webrtc-rest sending initial SDP offer to server.")]
    public static partial void LogInitialSdpOffer(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "FailedSetRemoteDescription",
        Level = LogLevel.Warning,
        Message = "Failed to set remote description, {Result}.")]
    public static partial void LogFailedSetRemoteDescription(
        this ILogger logger,
        SetDescriptionResultEnum result);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketCouldNotParseJson",
        Level = LogLevel.Warning,
        Message = "websocket-client could not parse JSON message. {JsonStr}")]
    public static partial void LogWebSocketCouldNotParseJson(
        this ILogger logger,
        string jsonStr);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketClientReceiveLoopExitExtended",
        Level = LogLevel.Debug,
        Message = "websocket-client receive loop exiting.")]
    public static partial void LogWebSocketClientReceiveLoopExitExtended(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "GotRemoteIceCandidate",
        Level = LogLevel.Debug,
        Message = "Got remote ICE candidate.")]
    public static partial void LogGotRemoteIceCandidateSimple(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketClientSendingIceCandidateExtended",
        Level = LogLevel.Debug,
        Message = "WebRTCWebSocketClient sending ICE candidate to server.")]
    public static partial void LogWebSocketClientSendingIceCandidateExtended(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketServerParseJson",
        Level = LogLevel.Warning,
        Message = "websocket-server could not parse JSON message. {MessageData}")]
    public static partial void LogWebSocketServerParseJson(
        this ILogger logger,
        string messageData);

    [LoggerMessage(
        EventId = 0,
        EventName = "SocketConnectionEstablished",
        Level = LogLevel.Debug,
        Message = "Web socket client connection established.")]
    public static partial void LogSocketConnectionEstablished(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "OnOpenErrorExtended",
        Level = LogLevel.Error,
        Message = "An error has occurred during the OnOpen event.")]
    public static partial void LogOnOpenErrorExtended(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReceiveLoopStopped",
        Level = LogLevel.Debug,
        Message = "{Caller} stopped due to application cancellation request.")]
    public static partial void LogReceiveLoopStopped(
        this ILogger logger,
        string caller);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpAnswerToClientEndPoint",
        Level = LogLevel.Debug,
        Message = "Sending SDP answer to client {UserEndPoint}.")]
    public static partial void LogSdpAnswerToClientEndPoint(
        this ILogger logger,
        string userEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "RemoteSdpWas",
        Level = LogLevel.Trace,
        Message = "Remote SDP was:\n{Description}")]
    public static partial void LogRemoteSdpWas(
        this ILogger logger,
        string description);

    [LoggerMessage(
        EventId = 0,
        EventName = "KeepAlivePing",
        Level = LogLevel.Trace,
        Message = "Sending signaling channel keep alive 'ping'.")]
    public static partial void LogKeepAlivePing(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SendingMessageToRemoteWebSocketClient",
        Level = LogLevel.Debug,
        Message = "{Name} sending message to remote web socket client.")]
    public static partial void LogSendingMessageToRemoteWebSocketClient(
        this ILogger logger,
        string name);
}
