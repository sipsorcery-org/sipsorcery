using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Net
{
    internal static partial class NetRtpLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionNoLocalMedia",
            Level = LogLevel.Warning,
            Message = "No local media tracks available for create offer.")]
        public static partial void LogRtpSessionNoLocalMedia(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionSetRemoteTrackSsrc",
            Level = LogLevel.Debug,
            Message = "Set remote track ({MediaType} - index={Index}) SSRC to {SyncSource}.")]
        public static partial void LogRtpSessionSetRemoteTrackSsrc(
            this ILogger logger,
            SDPMediaTypesEnum mediaType,
            int index,
            uint syncSource);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionEndPointSwitched",
            Level = LogLevel.Debug,
            Message = "{MediaType} end point switched for RTP ssrc {Ssrc} from {ExpectedEndPoint} to {ReceivedOnEndPoint}.")]
        public static partial void LogRtpSessionEndPointSwitched(
            this ILogger logger,
            SDPMediaTypesEnum mediaType,
            uint ssrc,
            IPEndPoint expectedEndPoint,
            IPEndPoint receivedOnEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionUnrecognisedEndPoint",
            Level = LogLevel.Warning,
            Message = "RTP packet with SSRC {Ssrc} received from unrecognised end point {ReceivedOnEndPoint}.")]
        public static partial void LogRtpSessionUnrecognisedEndPoint(
            this ILogger logger,
            uint ssrc,
            IPEndPoint receivedOnEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionSecureContextNotReady",
            Level = LogLevel.Warning,
            Message = "RTP or RTCP packet received before secure context ready.")]
        public static partial void LogRtpSessionSecureContextNotReady(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionNoMatchingSsrc",
            Level = LogLevel.Warning,
            Message = "Could not match an RTCP packet against any SSRC's in the session.")]
        public static partial void LogRtpSessionNoMatchingSsrc(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionSendRtpRawOnClosedSession",
            Level = LogLevel.Warning,
            Message = "SendRtpRaw was called for a {MediaType} packet on a closed RTP session.")]
        public static partial void LogRtpSessionSendRtpRawOnClosedSession(
            this ILogger logger,
            SDPMediaTypesEnum mediaType);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionSendRtpRawNoLocalTrack",
            Level = LogLevel.Warning,
            Message = "SendRtpRaw was called for a {MediaType} packet on an RTP session without a local track.")]
        public static partial void LogRtpSessionSendRtpRawNoLocalTrack(
            this ILogger logger,
            SDPMediaTypesEnum mediaType);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionSendRtpRawInactiveStream",
            Level = LogLevel.Warning,
            Message = "SendRtpRaw was called for a {MediaType} packet on an RTP session with a Stream Status set to {StreamStatus}")]
        public static partial void LogRtpSessionSendRtpRawInactiveStream(
            this ILogger logger,
            SDPMediaTypesEnum mediaType,
            MediaStreamStatusEnum streamStatus);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionSendRtpPacketSecureContextNotReady",
            Level = LogLevel.Warning,
            Message = "SendRtpPacket cannot be called on a secure session before calling SetSecurityContext.")]
        public static partial void LogRtpSessionSendRtpPacketSecureContextNotReady(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionSecureContextAlreadyExists",
            Level = LogLevel.Warning,
            Message = "Secure context already exists for {MediaType}.")]
        public static partial void LogRtpSessionSecureContextAlreadyExists(
            this ILogger logger,
            SDPMediaTypesEnum mediaType);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpChannelClosing",
            Level = LogLevel.Debug,
            Message = "RTPChannel closing, RTP receiver on port {RTPPort}, Control receiver on port {ControlPort}. Reason: {CloseReason}.")]
        public static partial void LogRtpChannelClosing(
            this ILogger logger,
            int rtpPort,
            int controlPort,
            string closeReason);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpChannelClosingRtpOnly",
            Level = LogLevel.Debug,
            Message = "RTPChannel closing, RTP receiver on port {RTPPort}. Reason: {CloseReason}.")]
        public static partial void LogRtpChannelClosingRtpOnly(
            this ILogger logger,
            int rtpPort,
            string closeReason);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpChannelStarted",
            Level = LogLevel.Debug,
            Message = "RTPChannel for {LocalEndPoint} started.")]
        public static partial void LogRtpChannelStarted(
            this ILogger logger,
            EndPoint localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpChannelSendRtpPacketProtectionFailed",
            Level = LogLevel.Error,
            Message = "SendRTPPacket protection failed, result {RtpError}.")]
        public static partial void LogRtpChannelSendRtpPacketProtectionFailed(
            this ILogger logger,
            int rtpError);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpChannelSocketException",
            Level = LogLevel.Warning,
            Message = "SocketException RTPChannel EndSendTo ({SocketErrorCode}). {Message}")]
        public static partial void LogRtpChannelSocketException(
            this ILogger logger,
            int socketErrorCode,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpChannelException",
            Level = LogLevel.Error,
            Message = "Exception RTPChannel EndSendTo. {Message}")]
        public static partial void LogRtpChannelException(
            this ILogger logger,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionRemoteControlEndpointSwitched",
            Level = LogLevel.Debug,
            Message = "{MediaType} control end point switched from {ControlDestinationEndPoint} to {RemoteEndPoint}.")]
        public static partial void LogRtpSessionRemoteControlEndpointSwitched(
            this ILogger logger,
            SDPMediaTypesEnum mediaType,
            IPEndPoint controlDestinationEndPoint,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionRtcpByeReceived",
            Level = LogLevel.Debug,
            Message = "RTCP BYE received for SSRC {SSRC}, reason {Reason}.")]
        public static partial void LogRtpSessionRtcpByeReceived(
            this ILogger logger,
            uint ssrc,
            string reason);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionRemoteSDPSsrcAttributes",
            Level = LogLevel.Debug,
            Message = "LogRemoteSDPSsrcAttributes: {RemoteSDPSsrcAttributes}")]
        public static partial void LogRtpSessionRemoteSDPSsrcAttributes(
            this ILogger logger,
            string remoteSDPSsrcAttributes);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpVideoCodecDepacketiserSet",
            Level = LogLevel.Debug,
            Message = "Video depacketisation codec set to {Codec} for SSRC {SSRC}.",
            SkipEnabledCheck = true)]
        private static partial void LogRtpVideoCodecDepacketiserSetUnchecked(
            this ILogger logger,
            VideoCodecsEnum codec,
            uint ssrc);

        public static void LogRtpVideoCodecDepacketiserSet(
            this ILogger logger,
            SDPAudioVideoMediaFormat format,
            uint ssrc)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                LogRtpVideoCodecDepacketiserSetUnchecked(logger, format.ToVideoFormat().Codec, ssrc);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpVideoCodecNotImplemented",
            Level = LogLevel.Warning,
            Message = "Video depacketisation logic for codec {CodecName} has not been implemented, PR's welcome!",
            SkipEnabledCheck = true)]
        private static partial void LogRtpVideoCodecNotImplementedUnchecked(
            this ILogger logger,
            string codecName);

        public static void LogRtpVideoCodecNotImplemented(
            this ILogger logger,
            SDPAudioVideoMediaFormat format)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                LogRtpVideoCodecNotImplementedUnchecked(logger, format.Name());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSequenceNumberJumped",
            Level = LogLevel.Warning,
            Message = "{TrackType} stream sequence number jumped from {LastRemoteSeqNum} to {SequenceNumber}.")]
        public static partial void LogRtpSequenceNumberJumped(
            this ILogger logger,
            string trackType,
            ushort lastRemoteSeqNum,
            ushort sequenceNumber);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSocketException",
            Level = LogLevel.Warning,
            Message = "Socket error {SocketErrorCode} in UdpReceiver.BeginReceiveFrom. {Message}")]
        public static partial void LogRtpSocketException(
            this ILogger logger,
            SocketError socketErrorCode,
            string message);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpDuplicateSeqNum",
            Level = LogLevel.Information,
            Message = "Duplicate seq number: {sequenceNumber}")]
        public static partial void LogRtpDuplicateSeqNum(
            this ILogger logger,
            ushort sequenceNumber);

        [LoggerMessage(
            EventId = 0,
            EventName = "SendDtmfEventInProgress",
            Level = LogLevel.Warning,
            Message = "SendDtmfEvent an RTPEvent is already in progress.")]
        public static partial void LogDtmfEventInProgress(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SendDtmfEventCancelled",
            Level = LogLevel.Warning,
            Message = "SendDtmfEvent was cancelled by caller.")]
        public static partial void LogDtmfEventCancelled(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpVideoFramerError",
            Level = LogLevel.Warning,
            Message = "Discarding RTP packet, VP8 header Start bit not set.")]
        public static partial void LogRtpVideoFramerError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSocketExceptionSendDtmfEvent",
            Level = LogLevel.Error,
            Message = "SocketException SendDtmfEvent. {errorMessage}")]
        public static partial void LogRtpSocketExceptionSendDtmfEvent(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSocketExceptionSendAudioFrame",
            Level = LogLevel.Error,
            Message = "SocketException SendAudioFrame. {errorMessage}")]
        public static partial void LogRtpSocketExceptionSendAudioFrame(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSocketExceptionSendJpegFrame",
            Level = LogLevel.Error,
            Message = "SocketException SendJpegFrame. {ErrorMessage}")]
        public static partial void LogRtpSocketExceptionSendJpegFrame(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSocketExceptionSendVp8Frame",
            Level = LogLevel.Error,
            Message = "SocketException SendVp8Frame. {errorMessage}")]
        public static partial void LogRtpSocketExceptionSendVp8Frame(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpMaximumBandwidthRemoteTrack",
            Level = LogLevel.Warning,
            Message = "The maximum bandwith cannot be set for remote tracks.")]
        public static partial void LogRtpMaximumBandwidthRemoteTrack(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpUnsupportedVideoFormat",
            Level = LogLevel.Error,
            Message = "Unsupported video format selected {formatName}.")]
        public static partial void LogRtpUnsupportedVideoFormat(
            this ILogger logger,
            string formatName);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpCannotCreateRtcpCompoundPacket",
            Level = LogLevel.Warning,
            Message = "Can't create RTCPCompoundPacket from the provided RTCP bytes. {message}")]
        public static partial void LogRtpCannotCreateRtcpCompoundPacket(
            this ILogger logger,
            string message);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSrtpRtcpUnprotectFailed",
            Level = LogLevel.Warning,
            Message = "SRTP unprotect failed for {mediaType}, result {result}.")]
        public static partial void LogRtpSrtpRtcpUnprotectFailed(
            this ILogger logger,
            SDPMediaTypesEnum mediaType,
            int result);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSrtpRtcpProtectFailed",
            Level = LogLevel.Warning,
            Message = "SRTP RTCP packet protection failed, result {rtpError}.")] 
        public static partial void LogRtpSrtpRtcpProtectFailed(
            this ILogger logger,
            int rtpError);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSrtpReportNotReady",
            Level = LogLevel.Warning,
            Message = "SendRtcpReport cannot be called on a secure session before calling SetSecurityContext.")]
        public static partial void LogRtpSrtpReportNotReady(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RtpInvalidPortNumber",
            Level = LogLevel.Warning,
            Message = "Remote {sdpMediaType} announcement contained an invalid port number {port}.")]
        public static partial void LogRtpInvalidPortNumber(
            this ILogger logger,
            SDPMediaTypesEnum sdpMediaType,
            int port);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpDestinationAddressInvalid",
            Level = LogLevel.Warning,
            Message = "The destination address for Send in RTPChannel cannot be {address}.")]
        public static partial void LogRtpDestinationAddressInvalid(
            this ILogger logger,
            IPAddress address);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpFailedToParseReport",
            Level = LogLevel.Warning,
            Message = "Failed to parse RTCP compound report.")]
        public static partial void LogRtpFailedToParseReport(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpPacketWithSsrcNotMatched",
            Level = LogLevel.Warning,
            Message = "An RTP packet with SSRC {syncSource} and payload ID {payloadType} was received that could not be matched to an audio or video stream.")]
        public static partial void LogRtpPacketWithSsrcNotMatched(
            this ILogger logger,
            uint syncSource,
            int payloadType);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionException",
            Level = LogLevel.Error,
            Message = "Exception in RTPSession SetRemoteDescription. {errorMessage}")]
        public static partial void LogRtpSessionException(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSessionClose",
            Level = LogLevel.Error,
            Message = "Exception RTPChannel.Close. {errorMessage}")]
        public static partial void LogRtpSessionClose(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSecureMediaInvalidTransport",
            Level = LogLevel.Error,
            Message = "Error negotiating secure media. Invalid Transport {transport}.")]
        public static partial void LogRtpSecureMediaInvalidTransport(
            this ILogger logger,
            string transport);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSecureMediaIncompatibleCrypto", 
            Level = LogLevel.Error,
            Message = "Error negotiating secure media for type {mediaType}. Incompatible crypto parameter.")]
        public static partial void LogRtpSecureMediaIncompatibleCrypto(
            this ILogger logger,
            SDPMediaTypesEnum mediaType);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSecureMediaNoCompatibleCrypto",
            Level = LogLevel.Error, 
            Message = "Error negotiating secure media. No compatible crypto suite.")]
        public static partial void LogRtpSecureMediaNoCompatibleCrypto(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpUnknownSsrcRtcpPacket",
            Level = LogLevel.Warning,
            Message = "Could not find appropriate remote track for SSRC for RTCP packet - Ssrc:{ssrc}")]
        public static partial void LogRtpUnknownSsrcRtcpPacket(
            this ILogger logger,
            uint ssrc);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpPacketBeforeContextReady",
            Level = LogLevel.Warning,
            Message = "RTP or RTCP packet received before secure context ready.")]
        public static partial void LogRtpPacketBeforeContextReady(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpSecureContextError",
            Level = LogLevel.Warning,
            Message = "SRTCP unprotect failed for {mediaType} track, result {result}.")]
        public static partial void LogRtpSecureContextError(
            this ILogger logger,
            SDPMediaTypesEnum mediaType,
            int result);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpChannelGeneralException",
            Level = LogLevel.Error,
            Message = "Exception RTPChannel.Send. {errorMessage}")]
        public static partial void LogRtpChannelGeneralException(
            this ILogger logger,
            string errorMessage);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpChannelBeginReceiveError",
            Level = LogLevel.Error,
            Message = "Exception UdpReceiver.BeginReceiveFrom. {errorMessage}")]
        public static partial void LogRtpChannelBeginReceiveError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpEndReceiveFromWarning",
            Level = LogLevel.Warning,
            Message = "SocketException UdpReceiver.EndReceiveFrom ({socketErrorCode}). {errorMessage}")]
        public static partial void LogRtpEndReceiveFromWarning(
            this ILogger logger,
            SocketError socketErrorCode,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpEndReceiveFromError",  
            Level = LogLevel.Error,
            Message = "Exception UdpReceiver.EndReceiveFrom. {errorMessage}")]
        public static partial void LogRtpEndReceiveFromError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpUnknownVideoCodec",
            Level = LogLevel.Warning,
            Message = "rtp unknown video, seqnum {sequenceNumber}, ts {timestamp}, marker {markerBit}, payload {payloadLength}.")]
        public static partial void LogRtpUnknownVideo(
            this ILogger logger,
            ushort sequenceNumber,
            uint timestamp, 
            int markerBit,
            int payloadLength);
    }
}
