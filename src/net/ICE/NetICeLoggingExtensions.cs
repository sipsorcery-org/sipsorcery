using System;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Net
{
    internal static partial class NetIceLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerDnsLookup",
            Level = LogLevel.Debug,
            Message = "Attempting to resolve STUN server URI {Uri}.")]
        public static partial void LogIceServerDnsLookup(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerDnsResolutionFailed",
            Level = LogLevel.Warning,
            Message = "ICE server DNS resolution failed for {Uri}.")]
        public static partial void LogIceServerDnsResolutionFailed(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerConnectionTimeout",
            Level = LogLevel.Warning,
            Message = "Connection attempt to ICE server {Uri} timed out after {RequestsSent} requests.")]
        public static partial void LogIceServerConnectionTimeout(
            this ILogger logger,
            STUNUri uri,
            int requestsSent);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerErrorResponses",
            Level = LogLevel.Warning,
            Message = "Connection attempt to ICE server {Uri} cancelled after {ErrorResponseCount} error responses.")]
        public static partial void LogIceServerErrorResponses(
            this ILogger logger,
            STUNUri uri,
            int errorResponseCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunBindingSuccess",
            Level = LogLevel.Debug,
            Message = "STUN binding success response received for ICE server check to {Uri}.")]
        public static partial void LogIceStunBindingSuccess(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunBindingError",
            Level = LogLevel.Warning,
            Message = "STUN binding error response received for ICE server check to {Uri}.")]
        public static partial void LogIceStunBindingError(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnRefreshRequest",
            Level = LogLevel.Debug,
            Message = "Sending TURN refresh request to ICE server {Uri}.")]
        public static partial void LogIceTurnRefreshRequest(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnPermissionsFailed",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel failed to get a Create Permissions response from {IceServerUri} after {TurnPermissionsRequestSent} attempts.")]
        public static partial void LogIceTurnPermissionsFailed(
            this ILogger logger,
            STUNUri iceServerUri,
            int turnPermissionsRequestSent);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnPermissionsRequest",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel sending TURN permissions request {TurnPermissionsRequestSent} to server {IceServerUri} for peer {RemoteCandidate} (TxID: {RequestTransactionID}).")]
        public static partial void LogIceTurnPermissionsRequest(
            this ILogger logger,
            int turnPermissionsRequestSent,
            STUNUri iceServerUri,
            IPEndPoint remoteCandidate,
            string requestTransactionID);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecksTimerStopped",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel stopping connectivity checks in connection state {IceConnectionState}.")]
        public static partial void LogIceChecksTimerStopped(
            this ILogger logger,
            RTCIceConnectionState iceConnectionState);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChannelLocalCandidates",
            Level = LogLevel.Debug,
            Message = "RTP ICE Channel discovered {CandidateCount} local candidates.")]
        public static partial void LogIceChannelLocalCandidates(
            this ILogger logger,
            int candidateCount);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistEntryTxIdMatch",
            Level = LogLevel.Information,
            Message = "Received transaction id from a previous cached RequestTransactionID {Id} Index: {Index}")]
        public static partial void LogIceChecklistEntryTxIdMatch(
            this ILogger logger,
            string id,
            int index);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnPermissionResponse",
            Level = LogLevel.Debug,
            Message = "A TURN Create Permission success response was received from {RemoteEndPoint} (TxID: {TransactionId}).")]
        public static partial void LogIceTurnPermissionResponse(
            this ILogger logger,
            IPEndPoint remoteEndPoint,
            string transactionId);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistEntryFailed",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel check list entry set to failed: {RemoteCandidate}.")]
        public static partial void LogIceChecklistEntryFailed(
            this ILogger logger,
            RTCIceCandidate remoteCandidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChannelConnected",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel connected after {Duration:0.##}ms {LocalCandidate}->{RemoteCandidate}.",
            SkipEnabledCheck = true)]
        private static partial void LogIceChannelConnectedUnchecked(
            this ILogger logger,
            double duration,
            string localCandidate,
            string remoteCandidate);

        public static void LogIceChannelConnected(
            this ILogger logger,
            long duration,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceChannelConnectedUnchecked(duration, localCandidate?.ToShortString(), remoteCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChannelNominatedChanged",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel remote nominated candidate changed from {OldCandidate} to {NewCandidate}.",
            SkipEnabledCheck = true)]
        private static partial void LogIceChannelNominatedChangedUnchecked(
            this ILogger logger,
            string oldCandidate,
            string newCandidate);

        public static void LogIceChannelNominatedChanged(
            this ILogger logger,
            RTCIceCandidate oldCandidate,
            RTCIceCandidate newCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceChannelNominatedChangedUnchecked(oldCandidate?.ToShortString(), newCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChannelFailed",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel failed to connect as no checklist entries became available within {ElapsedSeconds}s.")]
        public static partial void LogIceChannelFailed(
            this ILogger logger,
            double elapsedSeconds);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceMdnsResolutionFailed",
            Level = LogLevel.Warning,
            Message = "RTP ICE channel MDNS resolver failed to resolve {RemoteCandidateAddress}.")]
        public static partial void LogIceMdnsResolutionFailed(
            this ILogger logger,
            string remoteCandidateAddress);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceMdnsResolutionSuccess",
            Level = LogLevel.Debug,
            Message = "RTP ICE channel resolved MDNS hostname {RemoteCandidateAddress} to {RemoteCandidateIPAddr}.")]
        public static partial void LogIceMdnsResolutionSuccess(
            this ILogger logger,
            string remoteCandidateAddress,
            IPAddress remoteCandidateIPAddr);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTcpStarted",
            Level = LogLevel.Debug,
            Message = "RTPIceChannel TCP for {LocalEndPoint} started.")]
        public static partial void LogTcpStarted(
            this ILogger logger,
            EndPoint? localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistNominatedBinding",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel remote peer nominated entry from binding request: {RemoteCandidate}.")]
        public static partial void LogIceChecklistNominatedBinding(
            this ILogger logger,
            string remoteCandidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistBindingResponse",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel binding response state {State} as Controller for {RemoteCandidate}",
            SkipEnabledCheck = true)]
        private static partial void LogIceChecklistBindingResponseUnchecked(
            this ILogger logger,
            ChecklistEntryState state,
            string remoteCandidate);

        public static void LogIceChecklistBindingResponse(
            this ILogger logger,
            ChecklistEntryState state,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceChecklistBindingResponseUnchecked(state, remoteCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistNominatedResponse",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel remote peer nominated entry from binding response {RemoteCandidate}",
            SkipEnabledCheck = true)]
        private static partial void LogIceChecklistNominatedResponseUnchecked(
            this ILogger logger,
            string remoteCandidate);

        public static void LogIceChecklistNominatedResponse(
            this ILogger logger,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceChecklistNominatedResponseUnchecked(remoteCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChannelDisconnected",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel disconnected after {Duration:0.##}s {LocalCandidate}->{RemoteCandidate}.",
            SkipEnabledCheck = true)]
        private static partial void LogIceChannelDisconnectedUnchecked(
            this ILogger logger,
            double duration,
            string localCandidate,
            string remoteCandidate);

        public static void LogIceChannelDisconnected(
            this ILogger logger,
            double duration,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogIceChannelDisconnectedUnchecked(duration, localCandidate?.ToShortString(), remoteCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChannelReconnected",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel has re-connected {LocalCandidate}->{RemoteCandidate}.",
            SkipEnabledCheck = true)]
        private static partial void LogIceChannelReconnectedUnchecked(
            this ILogger logger,
            string localCandidate,
            string remoteCandidate);

        public static void LogIceChannelReconnected(
            this ILogger logger,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceChannelReconnectedUnchecked(localCandidate?.ToShortString(), remoteCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceConnectivityCheckSending",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel sending connectivity check for {LocalCandidate}->{RemoteCandidate} from {LocalEndPoint} to {RemoteEndPoint} (use candidate {SetUseCandidate}).",
            SkipEnabledCheck = true)]
        private static partial void LogIceConnectivityCheckUnchecked(
            this ILogger logger,
            string localCandidate,
            string remoteCandidate,
            IPEndPoint localEndPoint,
            IPEndPoint remoteEndPoint,
            bool setUseCandidate);

        public static void LogIceConnectivityCheck(
            this ILogger logger,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate,
            IPEndPoint localEndPoint,
            IPEndPoint remoteEndPoint,
            bool setUseCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceConnectivityCheckUnchecked(localCandidate?.ToShortString(), remoteCandidate?.ToShortString(), localEndPoint, remoteEndPoint, setUseCandidate);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceRelayCheck",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel sending connectivity check for {LocalCandidate}->{RemoteCandidate} from {LocalEndPoint} to relay at {RelayServerEndPoint} (use candidate {SetUseCandidate}).",
            SkipEnabledCheck = true)]
        private static partial void LogIceRelayCheckUnchecked(
            this ILogger logger,
            string localCandidate,
            string remoteCandidate,
            IPEndPoint localEndPoint,
            IPEndPoint relayServerEndPoint,
            bool setUseCandidate);

        public static void LogIceRelayCheck(
            this ILogger logger,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate,
            IPEndPoint localEndPoint,
            IPEndPoint relayServerEndPoint,
            bool setUseCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceRelayCheckUnchecked(localCandidate?.ToShortString(), remoteCandidate?.ToShortString(), localEndPoint, relayServerEndPoint, setUseCandidate);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunBindingRequestFailed",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel STUN binding request from {RemoteEndPoint} failed an integrity check, rejecting.")]
        public static partial void LogIceStunBindingRequestFailed(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceUnrecognisedStunResponse",
            Level = LogLevel.Warning,
            Message = "An unrecognised STUN {MessageType} response for an ICE server check was received from {RemoteEndPoint}.")]
        public static partial void LogIceUnrecognisedStunResponse(
            this ILogger logger,
            STUNMessageTypesEnum messageType,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerRefreshError",
            Level = LogLevel.Error,
            Message = "Cannot refresh TURN allocation")]
        public static partial void LogIceServerRefreshError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunRequestMismatch",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel STUN request matched a remote candidate but NOT a checklist entry.")]
        public static partial void LogIceStunRequestMismatch(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceBindingRequestRejected",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel rejecting non-relayed STUN binding request from {RemoteEndPoint}.")]
        public static partial void LogIceBindingRequestRejected(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunServerBindingSendError",
            Level = LogLevel.Warning,
            Message = "Error sending STUN server binding request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.")]
        public static partial void LogIceStunServerBindingSendError(
            this ILogger logger,
            int outstandingRequestsSent,
            STUNUri uri,
            IPEndPoint serverEndPoint,
            SocketError sendResult);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnAllocateRequestSendError",
            Level = LogLevel.Warning,
            Message = "Error sending TURN Allocate request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.")]
        public static partial void LogIceTurnAllocateRequestSendError(
            this ILogger logger,
            int outstandingRequestsSent,
            STUNUri uri,
            IPEndPoint serverEndPoint,
            SocketError sendResult);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnRefreshRequestSendError",
            Level = LogLevel.Warning,
            Message = "Error sending TURN Refresh request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.")]
        public static partial void LogIceTurnRefreshRequestSendError(
            this ILogger logger,
            int outstandingRequestsSent,
            STUNUri uri,
            IPEndPoint serverEndPoint,
            SocketError sendResult);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnCreatePermissionsRequestSendError",
            Level = LogLevel.Warning,
            Message = "Error sending TURN Create Permissions request {OutstandingRequestsSent} for {Uri} to {ServerEndPoint}. {SendResult}.")]
        public static partial void LogIceTurnCreatePermissionsRequestSendError(
            this ILogger logger,
            int outstandingRequestsSent,
            STUNUri uri,
            IPEndPoint serverEndPoint,
            SocketError sendResult);

        [LoggerMessage(
            EventId = 0,
            EventName = "IcePeerReflexAdded",
            Level = LogLevel.Debug,
            Message = "Adding peer reflex ICE candidate for {RemoteEndPoint}.")]
        public static partial void LogIcePeerReflexAdded(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceClosed",
            Level = LogLevel.Debug,
            Message = "RtpIceChannel for {LocalEndPoint} closed.")]
        public static partial void LogIceClosed(
            this ILogger logger,
            IPEndPoint localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceConnCheckEmptyPair",
            Level = LogLevel.Warning,
            Message = "RTP ICE channel was requested to send a connectivity check on an empty candidate pair.")]
        public static partial void LogIceConnCheckEmptyPair(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunBindingErrorResponse",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel a STUN binding error response was received from {RemoteEndPoint}.")]
        public static partial void LogIceStunBindingErrorResponse(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnCreatePermissionsError",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel TURN Create Permission error response was received from {RemoteEndPoint}.")]
        public static partial void LogIceTurnCreatePermissionsError(
            this ILogger logger,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceUnexpectedStunResponse",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel received an unexpected STUN response {MessageType} from {RemoteEndPoint}.")]
        public static partial void LogIceUnexpectedStunResponse(
            this ILogger logger,
            STUNMessageTypesEnum messageType,
            IPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunNoAuthServer",
            Level = LogLevel.Warning,
            Message = "A STUN error response was received on an ICE candidate without a corresponding ICE server, ignoring.")]
        public static partial void LogIceStunNoAuthServer(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunRequestTxIdMismatch",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel received a STUN {MessageType} with a transaction ID that did not match a checklist entry.")]
        public static partial void LogIceStunRequestTxIdMismatch(
            this ILogger logger,
            STUNMessageTypesEnum messageType);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceAllocationSucceeded",
            Level = LogLevel.Debug,
            Message = "TURN allocate success response received for ICE server check to {Uri}.")]
        public static partial void LogIceAllocationSucceeded(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerCandidateUnavailable",
            Level = LogLevel.Warning,
            Message = "Could not get ICE server candidate for {Uri} and type {Type}.")]
        public static partial void LogIceServerCandidateUnavailable(
            this ILogger logger,
            STUNUri uri,
            RTCIceCandidateType type);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunAllocateError",
            Level = LogLevel.Warning,
            Message = "ICE session received an error response for an Allocate request to {Uri}.")]
        public static partial void LogIceStunAllocateError(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunRefreshError",
            Level = LogLevel.Warning,
            Message = "ICE session received an error response for a Refresh request to {Uri}.")]
        public static partial void LogIceStunRefreshError(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunAlternateServer",
            Level = LogLevel.Warning,
            Message = "ICE session received an alternate response for an Allocate request to {Uri}, changed server url to {ServerEndPoint}.")]
        public static partial void LogIceStunAlternateServer(
            this ILogger logger,
            STUNUri uri,
            IPEndPoint serverEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceAllocateRequestErrorResponseWithCode",
            Level = LogLevel.Warning,
            Message = "ICE session received an error response for an Allocate request to {Uri}, error {ErrorCode} {ReasonPhrase}.")]
        public static partial void LogIceAllocateRequestErrorResponseWithCode(
            this ILogger logger,
            STUNUri uri,
            int errorCode,
            string reasonPhrase);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceBindingRequestErrorResponseWithCode",
            Level = LogLevel.Warning,
            Message = "ICE session received an error response for a Binding request to {Uri}, error {ErrorCode} {ReasonPhrase}.")]
        public static partial void LogIceBindingRequestErrorResponseWithCode(
            this ILogger logger,
            STUNUri uri,
            int errorCode,
            string reasonPhrase);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceRefreshRequestErrorResponseWithCode",
            Level = LogLevel.Warning,
            Message = "ICE session received an error response for a Refresh request to {Uri}, error {ErrorCode} {ReasonPhrase}.")]
        public static partial void LogIceRefreshRequestErrorResponseWithCode(
            this ILogger logger,
            STUNUri uri,
            int errorCode,
            string reasonPhrase);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsUnexpectedStunMessage",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel received an unexpected STUN message {MessageType} from {RemoteEndPoint}.\nJson: {StunMessage}")]
        public static partial void LogDtlsUnexpectedStunMessage(
            this ILogger logger,
            STUNMessageTypesEnum messageType,
            IPEndPoint remoteEndPoint,
            STUNMessage stunMessage);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSendToSocketError",
            Level = LogLevel.Warning,
            Message = "SocketException RTPIceChannel EndSendToTCP ({SocketErrorCode}). {ErrorMessage}")]
        public static partial void LogTcpSendToSocketError(
            this ILogger logger,
            SocketError socketErrorCode,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSendToError",
            Level = LogLevel.Error,
            Message = "Exception RTPIceChannel EndSendToTCP. {ErrorMessage}")]
        public static partial void LogTcpSendToError(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunBindingSendError",
            Level = LogLevel.Warning,
            Message = "Error sending STUN server binding request to {RemoteEndPoint}. {SendResult}.")]
        public static partial void LogStunBindingSendError(
            this ILogger logger,
            IPEndPoint remoteEndPoint,
            SocketError sendResult);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnRelayError",
            Level = LogLevel.Warning,
            Message = "Error sending TURN relay request to TURN server at {RelayEndPoint}. {SendResult}.")]
        public static partial void LogTurnRelayError(
            this ILogger logger,
            IPEndPoint relayEndPoint,
            SocketError sendResult);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceRemoteCandidate",
            Level = LogLevel.Debug,
            Message = "RTP ICE Channel received remote candidate: {Candidate}")]
        public static partial void LogRemoteCandidate(
            this ILogger logger,
            RTCIceCandidate candidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceRemoteCredentials",
            Level = LogLevel.Debug,
            Message = "RTP ICE Channel remote credentials set.")]
        public static partial void LogRemoteCredentialsSet(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpDisconnectRequest",
            Level = LogLevel.Debug,
            Message = "SendOverTCP request disconnect.")]
        public static partial void LogTcpDisconnectRequest(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceSocketReceiveError",
            Level = LogLevel.Error,
            Message = "Exception IceTcpReceiver.BeginReceiveFrom. {ErrorMessage}")]
        public static partial void LogIceSocketReceiveError(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpSendStatus",
            Level = LogLevel.Debug,
            Message = "SendOverTCP status: {Connected} endpoint: {EndPoint}")]
        public static partial void LogTcpSendStatus(
            this ILogger logger,
            bool connected,
            IPEndPoint endPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTcpError",
            Level = LogLevel.Error,
            Message = "Exception RTPChannel.Close. {ErrorMessage}")]
        public static partial void LogTcpError(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceSocketWarning",
            Level = LogLevel.Warning,
            Message = "Socket error {SocketErrorCode} in IceTcpReceiver.BeginReceiveFrom. {ErrorMessage}")]
        public static partial void LogIceSocketWarning(
            this ILogger logger,
            SocketError socketErrorCode,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpAddress",
            Level = LogLevel.Debug,
            Message = "RTP ICE channel has no MDNS resolver set, and the system can not resolve remote candidate with MDNS hostname {CandidateAddress}.")]
        public static partial void LogTcpAddress(
            this ILogger logger,
            string candidateAddress);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServersChecksFailed",
            Level = LogLevel.Debug,
            Message = "RTP ICE Channel all ICE server connection checks failed, stopping ICE servers timer.")]
        public static partial void LogIceServersChecksFailed(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStopsProcessing",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel stopping ICE server checks in gathering state {IceGatheringState} and connection state {IceConnectionState}.")]
        public static partial void LogIceStopsProcessing(
            this ILogger logger,
            RTCIceGatheringState iceGatheringState,
            RTCIceConnectionState iceConnectionState);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerNotAcquired",
            Level = LogLevel.Debug,
            Message = "RTP ICE Channel was not able to acquire an active ICE server, stopping ICE servers timer.")]
        public static partial void LogIceServerNotAcquired(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceActiveServerNotSet",
            Level = LogLevel.Debug,
            Message = "RTP ICE Channel was not able to set an active ICE server, stopping ICE servers timer.")]
        public static partial void LogIceActiveServerNotSet(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceStunBindingRequest",
            Level = LogLevel.Debug,
            Message = "Sending STUN binding request to ICE server {Uri} with address {EndPoint}.")]
        public static partial void LogIceStunBindingRequest(
            this ILogger logger,
            STUNUri uri,
            IPEndPoint endPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceTurnAllocateRequest",
            Level = LogLevel.Debug,
            Message = "Sending TURN allocate request to ICE server {Uri} with address {EndPoint}.")]
        public static partial void LogIceTurnAllocateRequest(
            this ILogger logger,
            STUNUri uri,
            IPEndPoint endPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceUnexpectedState",
            Level = LogLevel.Warning,
            Message = "The active ICE server reached an unexpected state {Uri}.")]
        public static partial void LogIceUnexpectedState(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceNominatedNewCandidate",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel remote peer nominated a new candidate: {RemoteCandidate}.")]
        public static partial void LogIceNominatedNewCandidate(
            this ILogger logger,
            string remoteCandidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "IcePeerReflexAddingCandidate",
            Level = LogLevel.Debug,
            Message = "Adding server reflex ICE candidate for ICE server {Uri} and {EndPoint}.")]
        public static partial void LogIcePeerReflexAddingCandidate(
            this ILogger logger,
            STUNUri uri,
            IPEndPoint endPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceRelayAddingCandidate",
            Level = LogLevel.Debug,
            Message = "Adding relay ICE candidate for ICE server {Uri} and {EndPoint}.")]
        public static partial void LogIceRelayAddingCandidate(
            this ILogger logger,
            STUNUri uri,
            IPEndPoint endPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceDnsResolutionSuccess",
            Level = LogLevel.Warning,
            Message = "RTP ICE channel resolved remote candidate {RemoteCandidateAddress} to {RemoteCandidateIPAddr}.")]
        public static partial void LogIceDnsResolutionSuccess(
            this ILogger logger,
            string remoteCandidateAddress,
            IPAddress remoteCandidateIPAddr);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceDnsResolutionFailed",
            Level = LogLevel.Debug,
            Message = "RTP ICE channel failed to resolve remote candidate {RemoteCandidateAddress}.")]
        public static partial void LogIceDnsResolutionFailed(
            this ILogger logger,
            string remoteCandidateAddress);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceNewChecklistEntry",
            Level = LogLevel.Debug,
            Message = "Adding new candidate pair to checklist for: {LocalCandidate}->{RemoteCandidate}")]
        private static partial void LogIceNewChecklistEntryUnchecked(
            this ILogger logger,
            string localCandidate,
            string remoteCandidate);

        public static void LogIceNewChecklistEntry(
            this ILogger logger,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceNewChecklistEntryUnchecked(localCandidate?.ToShortString(), remoteCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistEntryTimeout",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel checks for checklist entry have timed out, state being set to failed: {LocalCandidate}->{RemoteCandidate}.",
            SkipEnabledCheck = true)]
        private static partial void LogIceChecklistEntryTimeoutUnchecked(
            this ILogger logger,
            string localCandidate,
            string remoteCandidate);

        public static void LogIceChecklistEntryTimeout(
            this ILogger logger,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceChecklistEntryTimeoutUnchecked(localCandidate?.ToShortString(), remoteCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistEntrySucceededTimeout",
            Level = LogLevel.Debug,
            Message = "ICE RTP channel checks for succeded checklist entry have timed out, state being set to failed: {LocalCandidate}->{RemoteCandidate}.")]
        private static partial void LogIceChecklistEntrySucceededTimeoutUnchecked(
            this ILogger logger,
            string localCandidate,
            string remoteCandidate);

        public static void LogIceChecklistEntrySucceededTimeout(
            this ILogger logger,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogIceChecklistEntrySucceededTimeoutUnchecked(localCandidate?.ToShortString(), remoteCandidate?.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistEntryLowerPriority",
            Level = LogLevel.Debug,
            Message = "Removing lower priority entry and adding candidate pair to checklist for: {RemoteCandidate}")]
        public static partial void LogIceChecklistEntryLowerPriority(
            this ILogger logger,
            RTCIceCandidate remoteCandidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChecklistEntryHigherPriority",
            Level = LogLevel.Debug,
            Message = "Existing checklist entry has higher priority, NOT adding entry for: {RemoteCandidate}")]
        public static partial void LogIceChecklistEntryHigherPriority(
            this ILogger logger,
            RTCIceCandidate remoteCandidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerAdded",
            Level = LogLevel.Debug,
            Message = "Adding ICE server for {Uri}.")]
        public static partial void LogIceServerAdded(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerEndPointSet",
            Level = LogLevel.Debug,
            Message = "ICE server end point for {Uri} set to {EndPoint}.")]
        public static partial void LogIceServerEndPointSet(
            this ILogger logger,
            STUNUri uri,
            IPEndPoint endPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerResolved",
            Level = LogLevel.Debug,
            Message = "ICE server {Uri} successfully resolved to {Result}.")]
        public static partial void LogIceServerResolved(
            this ILogger logger,
            STUNUri uri,
            IPEndPoint result);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerResolutionFailed",
            Level = LogLevel.Warning,
            Message = "Unable to resolve ICE server end point for {Uri}.")]
        public static partial void LogIceServerResolutionFailed(
            this ILogger logger,
            STUNUri uri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceFailedNoRemoteCredentials",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel checklist processing cannot occur as either the remote ICE user or password are not set.")]
        public static partial void LogIceFailedNoRemoteCredentials(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChannelFailedTimeout",
            Level = LogLevel.Warning,
            Message = "ICE RTP channel failed after {Duration:0.##}s {LocalCandidate}->{RemoteCandidate}.",
            SkipEnabledCheck = true)]
        private static partial void LogIceChannelFailedTimeoutUnchecked(
            this ILogger logger,
            int duration,
            string localCandidate,
            string remoteCandidate);

        public static void LogIceChannelFailedTimeout(
            this ILogger logger,
            int duration,
            RTCIceCandidate localCandidate,
            RTCIceCandidate remoteCandidate)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogIceChannelFailedTimeoutUnchecked(duration, localCandidate.ToShortString(), remoteCandidate.ToShortString());
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "IceLocalCandidateUpdateChecklistError",
            Level = LogLevel.Error,
            Message = "UpdateChecklist the local candidate supplied to UpdateChecklist was null.")]
        public static partial void LogIceLocalCandidateUpdateChecklistError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceRemoteCandidateUpdateChecklistError",
            Level = LogLevel.Error,
            Message = "UpdateChecklist the remote candidate supplied to UpdateChecklist was null.")]
        public static partial void LogIceRemoteCandidateUpdateChecklistError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IcePolicyStunWarning",
            Level = LogLevel.Warning,
            Message = "ICE channel policy is relay only, ignoring STUN server {stunUri}.")]
        public static partial void LogIcePolicyStunWarning(
            this ILogger logger,
            STUNUri stunUri);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerUrlParseError",
            Level = LogLevel.Warning,
            Message = "RTP ICE Channel could not parse ICE server URL {url}.")]
        public static partial void LogIceServerUrlParseError(
            this ILogger logger,
            string url);

        [LoggerMessage(
            EventId = 0,
            EventName = "MdnsResolutionError",
            Level = LogLevel.Error,
            Message = "Error resolving mDNS hostname {HostName}")]
        public static partial void LogMdnsResolutionError(
            this ILogger logger,
            string hostName,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "MdnsNameError",
            Level = LogLevel.Error,
            Message = "Unsupported mDNS hostname {HostName}")]
        public static partial void LogMdnsNameError(
            this ILogger logger,
            string hostName,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerTlsNotSupported",
            Level = LogLevel.Warning,
            Message = "ICE channel does not currently support TLS for STUN and TURN servers, not checking {stunUri}.")]
        public static partial void LogStunServerTlsNotSupported(
            this ILogger logger,
            STUNUri stunUri);

        [LoggerMessage(
            EventId = 0,
            EventName = "MaxServers",
            Level = LogLevel.Warning,
            Message = "The maximum number of ICE servers for the session has been reached.")]
        public static partial void LogMaxServers(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "TcpEndReceiveError",
            Level = LogLevel.Error,
            Message = "Exception IceTcpReceiver.EndReceiveFrom. {ErrorMessage}")]
        public static partial void LogTcpEndReceiveError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RefreshError",
            Level = LogLevel.Error,
            Message = "Exception RefreshTurn. {ErrorMessage}")]
        public static partial void LogRefreshError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UpdateChecklistError",
            Level = LogLevel.Error,
            Message = "Exception UpdateChecklist. {ErrorMessage}")]
        public static partial void LogUpdateChecklistError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "DstAddressError",
            Level = LogLevel.Error,
            Message = "The destination address for Send in RTPChannel cannot be {Address}.")]
        public static partial void LogDstAddressError(
            this ILogger logger,
            IPAddress address);

        [LoggerMessage(
            EventId = 0,
            EventName = "SendTcpError",
            Level = LogLevel.Error,
            Message = "Exception RTPIceChannel.SendOverTCP. {ErrorMessage}")]
        public static partial void LogSendTcpError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "RtpCandidatesUnavailable",
            Level = LogLevel.Warning,
            Message = "RTP ICE Channel could not create a check list entry for a remote candidate with no destination end point, {RemoteCandidate}.")]
        public static partial void LogRtpCandidatesUnavailable(
            this ILogger logger,
            RTCIceCandidate remoteCandidate);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceSocketEndReceiveError",
            Level = LogLevel.Warning,
            Message = "SocketException IceTcpReceiver.EndReceiveFrom ({SocketErrorCode}). {ErrorMessage}")]
        public static partial void LogIceSocketEndReceiveError(
            this ILogger logger,
            SocketError socketErrorCode,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceChannelBothFuncs",
            Level = LogLevel.Warning,
            Message = "RTP ICE channel has both "+ nameof(RtpIceChannel.MdnsGetAddresses) + " and " + nameof(RtpIceChannel.MdnsResolve) + " set.Only " + nameof(RtpIceChannel.MdnsGetAddresses) + " will be used.")]
        public static partial void LogIceMdnsBothSet(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceAgentStartGathering",
            Level = LogLevel.Debug,
            Message = "ICE agent starting gathering.")]
        public static partial void LogIceAgentStartGathering(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerSocket",
            Level = LogLevel.Debug,
            Message = "ICE server socket for component {Component} created local end point {LocalEndPoint}.")]
        public static partial void LogIceServerSocket(
            this ILogger logger,
            RTCIceComponent component,
            IPEndPoint localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceGatheringTimeout",
            Level = LogLevel.Warning,
            Message = "ICE gathering timed out after {TimeoutMilliseconds}ms.")]
        public static partial void LogIceGatheringTimeout(
            this ILogger logger,
            int timeoutMilliseconds);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceServerControlEndPoint",
            Level = LogLevel.Debug,
            Message = "ICE server control socket for component {Component} created local end point {LocalEndPoint}.")]
        public static partial void LogIceServerControlEndPoint(
            this ILogger logger,
            RTCIceComponent component,
            IPEndPoint localEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceAgentNoCandidates",
            Level = LogLevel.Warning,
            Message = "No ICE candidates were gathered.")]
        public static partial void LogIceAgentNoCandidates(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "IceGatheringStart",
            Level = LogLevel.Debug,
            Message = "ICE gathering connecting to STUN server at {StunServer}.")]
        public static partial void LogIceGatheringStart(
            this ILogger logger,
            string stunServer);

        // ... add more logging methods as needed
    }
}
