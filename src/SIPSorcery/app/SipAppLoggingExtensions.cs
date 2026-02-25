using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App;

internal static partial class SipAppLoggingExtensions
{
    [LoggerMessage(
        EventId = 0,
        EventName = "SdpManglingFailed",
        Level = LogLevel.Warning,
        Message = "SDP mangling failed to find a valid RTP endpoint in the SDP body.")]
    public static partial void LogSdpManglingFailed(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "EmptySdpBodyOrAddress",
        Level = LogLevel.Warning,
        Message = "Mangle SDP was called with an empty body or public IP address.")]
    public static partial void LogEmptySdpBodyOrAddress(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ExceptionMangleSDP",
        Level = LogLevel.Error,
        Message = "Exception MangleSDP. {ErrorMessage}")]
    public static partial void LogExceptionMangleSDP(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpMangled",
        Level = LogLevel.Debug,
        Message = "SDP mangled for {Status} response from {RemoteSIPEndPoint}, adjusted address {RemoteEndPointAddress}.",
        SkipEnabledCheck = true)]
    private static partial void LogSdpMangledUnchecked(
        this ILogger logger,
        string status,
        string remoteSipEndPoint,
        string remoteEndPointAddress);

    public static void LogSdpMangled(
        this ILogger logger,
        SIPResponseStatusCodesEnum status,
        SIPEndPoint remoteSipEndPoint,
        IPAddress remoteEndPointAddress)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogSdpMangledUnchecked(
                status.ToString(),
                remoteSipEndPoint.ToString(),
                remoteEndPointAddress.ToString());
        }
    }

    public static void LogSdpMangled(
        this ILogger logger,
        SIPMethodsEnum status,
        SIPEndPoint remoteSipEndPoint,
        string remoteEndPointAddress)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogSdpMangledUnchecked(
                status.ToString(),
                remoteSipEndPoint.ToString(),
                remoteEndPointAddress);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ExceptionMangleSIPResponse",
        Level = LogLevel.Error,
        Message = "Exception MangleSIPResponse. {ErrorMessage}")]
    public static partial void LogExceptionMangleSIPResponse(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SdpMangleTimerExpiry",
        Level = LogLevel.Debug,
        Message = "Setting ring timeout of {RingTimeout}s.")]
    public static partial void LogSdpMangleTimerExpiry(
        this ILogger logger,
        double ringTimeout);

    [LoggerMessage(
        EventId = 0,
        EventName = "IncomingCallCancelled",
        Level = LogLevel.Debug,
        Message = "The incoming call has been cancelled.")]
    public static partial void LogIncomingCallCancelled(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtpChannelClosed",
        Level = LogLevel.Warning,
        Message = "RTP channel was closed with reason {CloseReason}.")]
    public static partial void LogRtpChannelClosed(
        this ILogger logger,
        string closeReason);

    [LoggerMessage(
        EventId = 0,
        EventName = "InviteSetRemoteDescFailed",
        Level = LogLevel.Warning,
        Message = "Error setting remote description from INVITE {SetRemoteResult}.")]
    public static partial void LogInviteSetRemoteDescFailed(
        this ILogger logger,
        SetDescriptionResultEnum setRemoteResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "AnswerFailedDialogNotCreated",
        Level = LogLevel.Warning,
        Message = "The attempt to answer a call failed as the dialog was not created. The likely cause is the ACK not being received in time.")]
    public static partial void LogAnswerFailedDialogNotCreated(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "BlindTransferNoDialogue",
        Level = LogLevel.Warning,
        Message = "Blind transfer was called on the SIPUserAgent when no dialogue was available.")]
    public static partial void LogBlindTransferNoDialogue(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "AttendedTransferNoDialogue",
        Level = LogLevel.Warning,
        Message = "Attended transfer was called on the SIPUserAgent when no dialogue was available.")]
    public static partial void LogAttendedTransferNoDialogue(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "TransferNoDialogue",
        Level = LogLevel.Warning,
        Message = "Transfer was called on the SIPUserAgent when no dialogue was available.")]
    public static partial void LogTransferNoDialogue(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallTransferAcceptedByRemoteServer",
        Level = LogLevel.Information,
        Message = "Call transfer was accepted by remote server.")]
    public static partial void LogCallTransferAcceptedByRemoteServer(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallTransferTimedOut",
        Level = LogLevel.Warning,
        Message = "Call transfer request timed out after {TimeoutMilliseconds}ms.")]
    public static partial void LogCallTransferTimedOut(
        this ILogger logger,
        double timeoutMilliseconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "RemoteCallPartyHungup",
        Level = LogLevel.Information,
        Message = "Remote call party hungup {StatusLine}.")]
    public static partial void LogRemoteCallPartyHungup(
        this ILogger logger,
        string statusLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReInviteRequestReceived",
        Level = LogLevel.Debug,
        Message = "Re-INVITE request received {StatusLine}.")]
    public static partial void LogReInviteRequestReceived(
        this ILogger logger,
        string statusLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "UnableToCreateReInviteAnswer",
        Level = LogLevel.Warning,
        Message = "Unable to create an answer for the re-INVITE request.")]
    public static partial void LogUnableToCreateReInviteAnswer(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "UnableToSetRemoteDescFromReInvite",
        Level = LogLevel.Warning,
        Message = "Unable to set remote description from reINVITE request {SetRemoteResult}")]
    public static partial void LogUnableToSetRemoteDescFromReInvite(
        this ILogger logger,
        SetDescriptionResultEnum setRemoteResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "MediaSessionCantProcessReInvite",
        Level = LogLevel.Error,
        Message = "MediaSession can't process the re-INVITE request.")]
    public static partial void LogMediaSessionCantProcessReInvite(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RemotePartyAddedStream",
        Level = LogLevel.Debug,
        Message = "Re-INVITE remote party added {Type} stream.")]
    public static partial void LogRemotePartyAddedStream(
        this ILogger logger,
        SDPMediaTypesEnum type);

    [LoggerMessage(
        EventId = 0,
        EventName = "RemotePartyRemovedStream",
        Level = LogLevel.Debug,
        Message = "Re-INVITE remote party removed {Type} stream.")]
    public static partial void LogRemotePartyRemovedStream(
        this ILogger logger,
        SDPMediaTypesEnum type);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReferMissingReferToHeader",
        Level = LogLevel.Warning,
        Message = "A REFER request was received from {RemoteSIPEndPoint} without a Refer-To header.")]
    public static partial void LogReferMissingReferToHeader(
        this ILogger logger,
        SIPEndPoint remoteSipEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReferNoDialogAvailable",
        Level = LogLevel.Warning,
        Message = "A REFER request was received from {RemoteSIPEndPoint} when there was no dialog or the dialog was not in a ready state.")]
    public static partial void LogReferNoDialogAvailable(
        this ILogger logger,
        SIPEndPoint remoteSipEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "TransferRequestReceived",
        Level = LogLevel.Debug,
        Message = "Transfer request received, referred by {ReferredBy}, refer to {ReferToUserField}.")]
    public static partial void LogTransferRequestReceived(
        this ILogger logger,
        string referredBy,
        SIPUserField referToUserField);

    [LoggerMessage(
        EventId = 0,
        EventName = "TransferRejectedByApplication",
        Level = LogLevel.Debug,
        Message = "Transfer request was rejected by application.")]
    public static partial void LogTransferRejectedByApplication(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallingTransferDestinationURI",
        Level = LogLevel.Debug,
        Message = "Calling transfer destination URI {ReferToUri}.")]
    public static partial void LogCallingTransferDestinationURI(
        this ILogger logger,
        string referToUri);

    [LoggerMessage(
        EventId = 0,
        EventName = "TransferDestinationResult",
        Level = LogLevel.Debug,
        Message = "Result of calling transfer destination {TransferResult}.")]
    public static partial void LogTransferDestinationResult(
        this ILogger logger,
        bool transferResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "TransferSucceededHangUpCall",
        Level = LogLevel.Debug,
        Message = "Transfer succeeded, hanging up original call.")]
    public static partial void LogTransferSucceededHangUpCall(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ExceptionProcessingTransferRequest",
        Level = LogLevel.Error,
        Message = "Exception processing transfer request. {ErrorMessage}")]
    public static partial void LogExceptionProcessingTransferRequest(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReInviteNoDialogAvailable",
        Level = LogLevel.Warning,
        Message = "No dialog available, re-INVITE request cannot be sent.")]
    public static partial void LogReInviteNoDialogAvailable(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ExceptionSIPTransportRequestReceived",
        Level = LogLevel.Error,
        Message = "Exception SIPUserAgent.SIPTransportRequestReceived. {ErrorMessage}")]
    public static partial void LogExceptionSIPTransportRequestReceived(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "AttendedTransferIgnoredCallIDMismatch",
        Level = LogLevel.Debug,
        Message = "Attended transfer INVITE ignored, Replaces CallID {ReplacesCallID} does not match our dialog Call-ID {DialogCallID}.")]
    public static partial void LogAttendedTransferIgnoredCallIDMismatch(
        this ILogger logger,
        string replacesCallID,
        string dialogCallID);

    [LoggerMessage(
        EventId = 0,
        EventName = "AttendedTransferCallIDMatch",
        Level = LogLevel.Debug,
        Message = "INVITE for attended transfer received, Replaces CallID {ReplacesCallID} matches our dialog Call-ID {DialogCallID}.")]
    public static partial void LogAttendedTransferCallIDMatch(
        this ILogger logger,
        string replacesCallID,
        string dialogCallID);

    [LoggerMessage(
        EventId = 0,
        EventName = "ProceedingWithAttendedTransfer",
        Level = LogLevel.Debug,
        Message = "Proceeding with attended transfer INVITE received from {RemoteEndPoint}.")]
    public static partial void LogProceedingWithAttendedTransfer(
        this ILogger logger,
        SIPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "IncomingCallRequest",
        Level = LogLevel.Information,
        Message = "Incoming call request: {LocalSIPEndPoint}<-{RemoteEndPoint}, uri:{URI}.")]
    public static partial void LogIncomingCallRequest(
        this ILogger logger,
        SIPEndPoint localSipEndPoint,
        SIPEndPoint remoteEndPoint,
        SIPURI uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "CurrentCallPlacedOnHold",
        Level = LogLevel.Debug,
        Message = "Current call placed on hold.")]
    public static partial void LogCurrentCallPlacedOnHold(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "AttendedTransferSuccessfullyAnswered",
        Level = LogLevel.Debug,
        Message = "Attended transfer was successfully answered, hanging up original call.")]
    public static partial void LogAttendedTransferSuccessfullyAnswered(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "AttendedTransferAnswerFailed",
        Level = LogLevel.Debug,
        Message = "Attended transfer answer failed, taking original call off hold.")]
    public static partial void LogAttendedTransferAnswerFailed(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReInviteResponseForOriginalCallID",
        Level = LogLevel.Debug,
        Message = "Re-INVITE response received for original Call-ID, disregarding.")]
    public static partial void LogReInviteResponseForOriginalCallID(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ReInviteRequestFailed",
        Level = LogLevel.Warning,
        Message = "Re-INVITE request failed with response {ShortDescription}.")]
    public static partial void LogReInviteRequestFailed(
        this ILogger logger,
        string shortDescription);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallAttemptReceivedTrying",
        Level = LogLevel.Information,
        Message = "Call attempt to {Uri} received a trying response {ShortDescription}.")]
    public static partial void LogCallAttemptReceivedTrying(
        this ILogger logger,
        string uri,
        string shortDescription);

    [LoggerMessage(
        EventId = 0,
        EventName = "SetRemoteDescriptionEarlyMedia",
        Level = LogLevel.Debug,
        Message = "Set remote description for early media result {SetDescriptionResult}.")]
    public static partial void LogSetRemoteDescriptionEarlyMedia(
        this ILogger logger,
        SetDescriptionResultEnum setDescriptionResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallAttemptReceivedRinging",
        Level = LogLevel.Information,
        Message = "Call attempt to {Uri} received a ringing response {ShortDescription}.")]
    public static partial void LogCallAttemptReceivedRinging(
        this ILogger logger,
        string uri,
        string shortDescription);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallAttemptFailed",
        Level = LogLevel.Warning,
        Message = "Call attempt to {Uri} failed with {ErrorMessage}.")]
    public static partial void LogCallAttemptFailed(
        this ILogger logger,
        string uri,
        string errorMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallAttemptAnsweredNoEarlyMedia",
        Level = LogLevel.Information,
        Message = "Call attempt to {Uri} was answered; no media update from early media.")]
    public static partial void LogCallAttemptAnsweredNoEarlyMedia(
        this ILogger logger,
        string uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallAttemptAnswered",
        Level = LogLevel.Information,
        Message = "Call attempt to {URI} was answered.")]
    public static partial void LogCallAttemptAnswered(
        this ILogger logger,
        string uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallAttemptFailureResponse",
        Level = LogLevel.Warning,
        Message = "Call attempt was answered with failure response {ShortDescription}.")]
    public static partial void LogCallAttemptFailureResponse(
        this ILogger logger,
        string shortDescription);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallAttemptAnsweredWithError",
        Level = LogLevel.Warning,
        Message = "Call attempt was answered with {ShortDescription} ({SetDescriptionResult}) but an {ErrorText}.")]
    public static partial void LogCallAttemptAnsweredWithError(
        this ILogger logger,
        string shortDescription,
        SetDescriptionResultEnum setDescriptionResult,
        string errorText);

    [LoggerMessage(
        EventId = 0,
        EventName = "RtpTimeoutMedia",
        Level = LogLevel.Warning,
        Message = "RTP has timed out for media {MediaType} hanging up call.")]
    public static partial void LogRtpTimeoutMedia(
        this ILogger logger,
        SDPMediaTypesEnum mediaType);

    [LoggerMessage(
        EventId = 0,
        EventName = "UsingAlternateOutboundProxy",
        Level = LogLevel.Debug,
        Message = "SIPClientUserAgent Call using alternate outbound proxy of {ServerEndPoint}.")]
    public static partial void LogUsingAlternateOutboundProxy(
        this ILogger logger,
        SIPEndPoint serverEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "RouteSetForCall",
        Level = LogLevel.Debug,
        Message = "Route set for call {RouteSet}.")]
    public static partial void LogRouteSetForCall(
        this ILogger logger,
        SIPRouteSet routeSet);

    [LoggerMessage(
        EventId = 0,
        EventName = "AttemptingToResolveHost",
        Level = LogLevel.Debug,
        Message = "SIPClientUserAgent attempting to resolve {Host}.")]
    public static partial void LogAttemptingToResolveHost(
        this ILogger logger,
        string host);

    [LoggerMessage(
        EventId = 0,
        EventName = "DNSResolutionFailure",
        Level = LogLevel.Debug,
        Message = "SIPClientUserAgent DNS failure resolving {Host} in {Duration:0.##}ms. Call cannot proceed.")]
    public static partial void LogDNSResolutionFailure(
        this ILogger logger,
        string host,
        double duration);

    [LoggerMessage(
        EventId = 0,
        EventName = "DNSResolutionSuccess",
        Level = LogLevel.Debug,
        Message = "SIPClientUserAgent resolved {Host} to {Result} in {Duration:0.##}ms.")]
    public static partial void LogDNSResolutionSuccess(
        this ILogger logger,
        string host,
        SIPEndPoint result,
        double duration);

    [LoggerMessage(
        EventId = 0,
        EventName = "UACCommencingCall",
        Level = LogLevel.Debug,
        Message = "UAC commencing call to {CanonicalAddress}.")]
    public static partial void LogUACCommencingCall(
        this ILogger logger,
        string canonicalAddress);

    [LoggerMessage(
        EventId = 0,
        EventName = "UnrecognisedOutboundProxy",
        Level = LogLevel.Debug,
        Message = "Error an outbound proxy value was not recognised in SIPClientUserAgent Call. {RouteSet}.")]
    public static partial void LogUnrecognisedOutboundProxy(
        this ILogger logger,
        string routeSet);

    [LoggerMessage(
        EventId = 0,
        EventName = "UACCallBodyEmpty",
        Level = LogLevel.Debug,
        Message = "Body on UAC call was empty.")]
    public static partial void LogUACCallBodyEmpty(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "CancellingForwardedCallLeg",
        Level = LogLevel.Debug,
        Message = "Cancelling forwarded call leg {Uri}, server transaction has not been created yet no CANCEL request required.")]
    public static partial void LogCancellingForwardedCallLeg(
        this ILogger logger,
        string uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "AlreadyCancelled",
        Level = LogLevel.Debug,
        Message = "Call {Uri} has already been cancelled once, trying again.")]
    public static partial void LogAlreadyCancelled(
        this ILogger logger,
        SIPURI uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "AlreadyRespondedToCancel",
        Level = LogLevel.Debug,
        Message = "Call {Uri} has already responded to CANCEL, probably overlap in messages not re-sending.")]
    public static partial void LogAlreadyRespondedToCancel(
        this ILogger logger,
        SIPURI uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "SendingCancelToURI",
        Level = LogLevel.Debug,
        Message = "Cancelling forwarded call leg, sending CANCEL to {URI}.")]
    public static partial void LogSendingCancelToURI(
        this ILogger logger,
        SIPURI uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "ByeRequestFailed",
        Level = LogLevel.Warning,
        Message = "Bye request for {Uri} failed with {Reason}.")]
    public static partial void LogByeRequestFailed(
        this ILogger logger,
        string uri,
        SocketError reason);

    [LoggerMessage(
        EventId = 0,
        EventName = "ServerFinalResponse",
        Level = LogLevel.Debug,
        Message = "Response {StatusCode} {ReasonPhrase} for {URI}.")]
    public static partial void LogServerFinalResponse(
        this ILogger logger,
        int statusCode,
        string reasonPhrase,
        SIPURI uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "CancelledCallAlreadyHungup",
        Level = LogLevel.Debug,
        Message = "A cancelled call to {Uri} has been answered AND has already been hungup, no further action being taken.")]
    public static partial void LogCancelledCallAlreadyHungup(
        this ILogger logger,
        string uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "CancelledCallAnsweredHangingUp",
        Level = LogLevel.Debug,
        Message = "A cancelled call to {Uri} has been answered, hanging up.")]
    public static partial void LogCancelledCallAnsweredHangingUp(
        this ILogger logger,
        string uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "NoContactHeaderForCancelledCall",
        Level = LogLevel.Debug,
        Message = "No contact header provided on response for cancelled call to {Uri} no further action.")]
    public static partial void LogNoContactHeaderForCancelledCall(
        this ILogger logger,
        string uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "NoCredentialsAvailable",
        Level = LogLevel.Debug,
        Message = "Forward leg failed, authentication was requested but no credentials were available.")]
    public static partial void LogNoCredentialsAvailable(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "InformationResponse",
        Level = LogLevel.Debug,
        Message = "Information response {StatusCode} {ReasonPhrase} for {URI}.")]
    public static partial void LogInformationResponse(
        this ILogger logger,
        int statusCode,
        string reasonPhrase,
        SIPURI uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "AuthenticatedByeRequestFailed",
        Level = LogLevel.Warning,
        Message = "Authenticated Bye request for {Uri} failed with {Reason}.")]
    public static partial void LogAuthenticatedByeRequestFailed(
        this ILogger logger,
        string uri,
        SocketError reason);

    [LoggerMessage(
        EventId = 0,
        EventName = "RejectingUnauthenticatedCall",
        Level = LogLevel.Warning,
        Message = "Rejecting authentication required call for {TransactionRequestFrom}, SIP account not found.")]
    public static partial void LogRejectingUnauthenticatedCall(
        this ILogger logger,
        SIPUserField transactionRequestFrom);

    [LoggerMessage(
        EventId = 0,
        EventName = "AuthenticationSuccessByIP",
        Level = LogLevel.Debug,
        Message = "New call from {RemoteEndPoint} successfully authenticated by IP address.")]
    public static partial void LogAuthenticationSuccessByIP(
        this ILogger logger,
        SIPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "AuthenticationSuccessByDigest",
        Level = LogLevel.Debug,
        Message = "New call from {RemoteEndPoint} successfully authenticated by digest.")]
    public static partial void LogAuthenticationSuccessByDigest(
        this ILogger logger,
        SIPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "CallNotAuthenticated",
        Level = LogLevel.Warning,
        Message = "Call not authenticated for {SIPUsername}@{SIPDomain}, responding with {ErrorResponse}.")]
    public static partial void LogCallNotAuthenticated(
        this ILogger logger,
        string sipUsername,
        string sipDomain,
        SIPResponseStatusCodesEnum errorResponse);

    [LoggerMessage(
        EventId = 0,
        EventName = "UASInvalidProgressStatus",
        Level = LogLevel.Debug,
        Message = "UAS call was passed an invalid response status of {ProgressStatus}({ProgressStatusValue}), ignoring.")]
    public static partial void LogUASInvalidProgressStatus(
        this ILogger logger,
        SIPResponseStatusCodesEnum progressStatus,
        int progressStatusValue);

    [LoggerMessage(
        EventId = 0,
        EventName = "UASIgnoringProgressResponse",
        Level = LogLevel.Debug,
        Message = "UAS call ignoring progress response with status of {ProgressStatus}({ProgressStatusValue}) as already in {TransactionState}.")]
    public static partial void LogUASIgnoringProgressResponse(
        this ILogger logger,
        SIPResponseStatusCodesEnum progressStatus,
        int progressStatusValue,
        SIPTransactionStatesEnum transactionState);

    [LoggerMessage(
        EventId = 0,
        EventName = "UASCallProgressing",
        Level = LogLevel.Debug,
        Message = "UAS call progressing with {ProgressStatus}.")]
    public static partial void LogUASCallProgressing(
        this ILogger logger,
        SIPResponseStatusCodesEnum progressStatus);

    [LoggerMessage(
        EventId = 0,
        EventName = "ProgressOnAnsweredCall",
        Level = LogLevel.Warning,
        Message = "SIPServerUserAgent Progress fired on already answered call.")]
    public static partial void LogProgressOnAnsweredCall(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "UASAnswerAlreadyAnswered",
        Level = LogLevel.Debug,
        Message = "UAS Answer was called on an already answered call, ignoring.")]
    public static partial void LogUASAnswerAlreadyAnswered(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "UASRejectInvalidStatus",
        Level = LogLevel.Debug,
        Message = "UAS Reject was passed an invalid response status of {FailureStatusCode}({FailureStatusValue}), ignoring.")]
    public static partial void LogUASRejectInvalidStatus(
        this ILogger logger,
        SIPResponseStatusCodesEnum failureStatusCode,
        int failureStatusValue);

    [LoggerMessage(
        EventId = 0,
        EventName = "UASCallFailed",
        Level = LogLevel.Warning,
        Message = "UAS call failed with a response status of {FailureStatus}{FailureReason}.")]
    public static partial void LogUASCallFailed(
        this ILogger logger,
        int failureStatus,
        string failureReason);

    [LoggerMessage(
        EventId = 0,
        EventName = "RejectOnAnsweredCall",
        Level = LogLevel.Warning,
        Message = "SIPServerUserAgent Reject fired on already answered call.")]
    public static partial void LogRejectOnAnsweredCall(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "MissingContactHeader",
        Level = LogLevel.Warning,
        Message = "The Contact header on the INVITE request was missing, BYE request cannot be generated.")]
    public static partial void LogMissingContactHeader(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ByeServerFinalResponse",
        Level = LogLevel.Debug,
        Message = "Response {StatusCode} {ReasonPhrase} for {TransactionRequestURI}.")]
    public static partial void LogByeServerFinalResponse(
        this ILogger logger,
        int statusCode,
        string reasonPhrase,
        SIPURI transactionRequestURI);

    [LoggerMessage(
        EventId = 0,
        EventName = "CancellationReceived",
        Level = LogLevel.Debug,
        Message = "SIPServerUserAgent got cancellation request.")]
    public static partial void LogCancellationReceived(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientTransactionFailed",
        Level = LogLevel.Debug,
        Message = "SIPServerUserAgent client failed with {FailureReason} in transaction state {TransactionState}.")]
    public static partial void LogClientTransactionFailed(
        this ILogger logger,
        SocketError failureReason,
        SIPTransactionStatesEnum transactionState);

    [LoggerMessage(
        EventId = 0,
        EventName = "RequestAuthenticatedByIP",
        Level = LogLevel.Debug,
        Message = "{Method} request from {RemoteEndPoint} successfully authenticated by IP address.")]
    public static partial void LogRequestAuthenticatedByIP(
        this ILogger logger,
        SIPMethodsEnum method,
        SIPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "RequestAuthenticatedByDigest",
        Level = LogLevel.Debug,
        Message = "{Method} request from {RemoteEndPoint} successfully authenticated by digest.")]
    public static partial void LogRequestAuthenticatedByDigest(
        this ILogger logger,
        SIPMethodsEnum method,
        SIPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "TransactionNotAuthenticated",
        Level = LogLevel.Debug,
        Message = "{TransactionRequestMethod} request not authenticated for {SipUsername}@{SipDomain}, responding with {ErrorResponse}.")]
    public static partial void LogTransactionNotAuthenticated(
        this ILogger logger,
        SIPMethodsEnum transactionRequestMethod,
        string sipUsername,
        string sipDomain,
        SIPResponseStatusCodesEnum errorResponse);

    [LoggerMessage(
        EventId = 0,
        EventName = "NonInviteAnswerSucceeded",
        Level = LogLevel.Debug,
        Message = "{Method} request succeeded with a response status of {StatusCode} {ReasonPhrase}.")]
    public static partial void LogNonInviteAnswerSucceeded(
        this ILogger logger,
        SIPMethodsEnum method,
        int statusCode,
        string reasonPhrase);

    [LoggerMessage(
        EventId = 0,
        EventName = "NonInviteRejectFailed",
        Level = LogLevel.Debug,
        Message = "{Method} request failed with a response status of {StatusCode} {ReasonPhrase}.")]
    public static partial void LogNonInviteRejectFailed(
        this ILogger logger,
        SIPMethodsEnum method,
        int statusCode,
        string reasonPhrase);

    [LoggerMessage(
        EventId = 0,
        EventName = "SendRequestFailure",
        Level = LogLevel.Error,
        Message = "Exception SIPNonInviteClientUserAgent SendRequest to {Uri}. {ErrorMessage}")]
    public static partial void LogSendRequestFailure(
        this ILogger logger,
        Exception exception,
        string uri,
        string errorMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "TransactionFailedAttempt",
        Level = LogLevel.Warning,
        Message = "Attempt to send {Method} to {Uri} failed with {FailureReason}.")]
    public static partial void LogTransactionFailedAttempt(
        this ILogger logger,
        SIPMethodsEnum method,
        string uri,
        SocketError failureReason);

    [LoggerMessage(
        EventId = 0,
        EventName = "ServerResponseReceived",
        Level = LogLevel.Debug,
        Message = "Server response {StatusCode} {ReasonPhrase} received for {Method} to {Uri}.",
        SkipEnabledCheck = true)]
    private static partial void LogServerResponseReceivedUnchecked(
        this ILogger logger,
        int statusCode,
        string reasonPhrase,
        SIPMethodsEnum method,
        string uri);

    public static void LogServerResponseReceived(
        this ILogger logger,
        int statusCode,
        string reasonPhrase,
        SIPMethodsEnum method,
        string uri)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogServerResponseReceivedUnchecked(logger, statusCode, reasonPhrase.IsNullOrBlank() ? statusCode.ToString() : reasonPhrase, method, uri);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "NoCredentialsForAuth",
        Level = LogLevel.Debug,
        Message = "Send request received an authentication required response but no credentials were available.")]
    public static partial void LogNoCredentialsForAuth(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "SendRequestFailedNoAuthHeader",
        Level = LogLevel.Debug,
        Message = "Send request failed with {StatusCode} but no authentication header was supplied for {Method} to {Uri}.")]
    public static partial void LogSendRequestFailedNoAuthHeader(
        this ILogger logger,
        int statusCode,
        SIPMethodsEnum method,
        string uri);

    [LoggerMessage(
        EventId = 0,
        EventName = "AuthenticatedServerResponseReceived",
        Level = LogLevel.Debug,
        Message = "Server response {StatusCode} {ReasonPhrase} received for authenticated {Method} to {Uri}.",
        SkipEnabledCheck = true)]
    private static partial void LogAuthenticatedServerResponseReceivedUnchecked(
        this ILogger logger,
        SIPResponseStatusCodesEnum statusCode,
        string reasonPhrase,
        SIPMethodsEnum method,
        string uri);

    public static void LogAuthenticatedServerResponseReceived(
        this ILogger logger,
        SIPResponseStatusCodesEnum statusCode,
        string reasonPhrase,
        SIPMethodsEnum method,
        string uri)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogAuthenticatedServerResponseReceivedUnchecked(logger, statusCode, reasonPhrase.IsNullOrBlank() ? statusCode.ToString() : reasonPhrase, method, uri);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RegisterStarting",
        Level = LogLevel.Debug,
        Message = "Starting SIPRegistrationUserAgent for {SIPAccountAOR}, callback period {CallbackPeriodSeconds}s.")]
    public static partial void LogRegisterStarting(
        this ILogger logger,
        SIPURI sipAccountAOR,
        double callbackPeriodSeconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegisterStartingRegistration",
        Level = LogLevel.Debug,
        Message = "Starting registration for {SIPAccountAOR}.")]
    public static partial void LogRegisterStartingRegistration(
        this ILogger logger,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegisterSuccess",
        Level = LogLevel.Debug,
        Message = "SIPRegistrationUserAgent was successful, scheduling next registration to {SIPAccountAOR} in {RefreshTime}s.")]
    public static partial void LogRegisterSuccess(
        this ILogger logger,
        SIPURI sipAccountAOR,
        double refreshTime);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegisterTempFailure",
        Level = LogLevel.Debug,
        Message = "SIPRegistrationUserAgent temporarily failed, scheduling next registration to {SIPAccountAOR} in {RegisterFailureRetryInterval}s.")]
    public static partial void LogRegisterTempFailure(
        this ILogger logger,
        SIPURI sipAccountAOR,
        double registerFailureRetryInterval);

    [LoggerMessage(
        EventId = 0,
        EventName = "InfoExpiryUpdated",
        Level = LogLevel.Information,
        Message = "Expiry for registration agent for {SIPAccountAOR} updated from {OldExpiry} to {NewExpiry}.")]
    public static partial void LogInfoExpiryUpdated(
        this ILogger logger,
        SIPURI sipAccountAOR,
        long oldExpiry,
        long newExpiry);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegisterStopping",
        Level = LogLevel.Debug,
        Message = "Stopping SIP registration user agent for {SIPAccountAOR}.")]
    public static partial void LogRegisterStopping(
        this ILogger logger,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "MaxAttemptsReached",
        Level = LogLevel.Warning,
        Message = "Registration to {SIPAccountAOR} reached the maximum number of allowed attempts without a failure condition.")]
    public static partial void LogMaxAttemptsReached(
        this ILogger logger,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegistrarHostUnresolved",
        Level = LogLevel.Warning,
        Message = "SIPRegistrationAgent could not resolve {RegistrarHost}.")]
    public static partial void LogRegistrarHostUnresolved(
        this ILogger logger,
        string registrarHost);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegisterInitiating",
        Level = LogLevel.Debug,
        Message = "Initiating registration to {RegistrarHost} at {RegistrarSIPEndPoint} for {SIPAccountAOR}.")]
    public static partial void LogRegisterInitiating(
        this ILogger logger,
        string registrarHost,
        SIPEndPoint registrarSIPEndPoint,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegistrationNoAuthHeader",
        Level = LogLevel.Warning,
        Message = "Registration failed with {Status} but no authentication header was supplied for {SIPAccountAOR}.")]
    public static partial void LogRegistrationNoAuthHeader(
        this ILogger logger,
        SIPResponseStatusCodesEnum status,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "ServerResponse",
        Level = LogLevel.Debug,
        Message = "Server response {SipResponseStatus} received for {SipAccountAOR}.")]
    public static partial void LogServerResponse(
        this ILogger logger,
        SIPResponseStatusCodesEnum sipResponseStatus,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegistrationTooShortExpiry",
        Level = LogLevel.Warning,
        Message = "Registration for {SIPAccountAOR} had a too short expiry, updated to {Expiry} and trying again.")]
    public static partial void LogRegistrationTooShortExpiry(
        this ILogger logger,
        SIPURI sipAccountAOR,
        long expiry);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegistrationUnequivocalFailure",
        Level = LogLevel.Warning,
        Message = "Registration unequivocal failure with {Status} for {SipAccountAOR}{Action}.")]
    public static partial void LogRegistrationUnequivocalFailure(
        this ILogger logger,
        SIPResponseStatusCodesEnum status,
        SIPURI sipAccountAOR,
        string action);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegistrationFailed",
        Level = LogLevel.Warning,
        Message = "Registration failed with {Status} for {SipAccountAOR}.")]
    public static partial void LogRegistrationFailed(
        this ILogger logger,
        SIPResponseStatusCodesEnum status,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "ServerAuthResponse",
        Level = LogLevel.Debug,
        Message = "Server auth response {Status} received for {SIPAccountAOR}.")]
    public static partial void LogServerAuthResponse(
        this ILogger logger,
        SIPResponseStatusCodesEnum status,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "InitialRegistrarResolutionFailed",
        Level = LogLevel.Warning,
        Message = "Could not resolve {RegistrarHost} when sending initial registration request.")]
    public static partial void LogInitialRegistrarResolutionFailed(
        this ILogger logger,
        string registrarHost);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegistrarResolutionFailed",
        Level = LogLevel.Warning,
        Message = "Could not resolve {RegistrarHost}.")]
    public static partial void LogRegistrarResolutionFailed(
        this ILogger logger,
        string registrarHost);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegistrationUnequivocalFailureWithExit",
        Level = LogLevel.Warning,
        Message = "Registration unequivocal failure with {Status} for {SIPAccountAOR}. No further registration attempts will be made: {Exit}.")]
    public static partial void LogRegistrationUnequivocalFailureWithExit(
        this ILogger logger,
        SIPResponseStatusCodesEnum status,
        SIPURI sipAccountAOR,
        bool exit);

    [LoggerMessage(
        EventId = 0,
        EventName = "RegistrationFailedCapitalSIP",
        Level = LogLevel.Warning,
        Message = "Registration failed with {Status} for {SIPAccountAOR}.")]
    public static partial void LogRegistrationFailedCapitalSIP(
        this ILogger logger,
        SIPResponseStatusCodesEnum status,
        SIPURI sipAccountAOR);

    [LoggerMessage(
        EventId = 0,
        EventName = "NotifierStopping",
        Level = LogLevel.Debug,
        Message = "Stopping SIP notifier user agent for user {AuthUsername} and resource URI {ResourceURI}.")]
    public static partial void LogNotifierStopping(
        this ILogger logger,
        string authUsername,
        SIPURI resourceURI);

    [LoggerMessage(
        EventId = 0,
        EventName = "GotNotificationRequest",
        Level = LogLevel.Debug,
        Message = "SIPNotifierClient GotNotificationRequest for {Method} {URI} {CSeq}.")]
    public static partial void LogGotNotificationRequest(
        this ILogger logger,
        SIPMethodsEnum method,
        SIPURI uri,
        long cSeq);

    [LoggerMessage(
        EventId = 0,
        EventName = "DuplicateNotifyReceived",
        Level = LogLevel.Warning,
        Message = "A duplicate NOTIFY request received by SIPNotifierClient for subscription Call-ID {CallID}.")]
    public static partial void LogDuplicateNotifyReceived(
        this ILogger logger,
        string callID);

    [LoggerMessage(
        EventId = 0,
        EventName = "NotifierStarting",
        Level = LogLevel.Debug,
        Message = "SIPNotifierClient starting for {ResourceURI} and event package {EventPackage}.")]
    public static partial void LogNotifierStarting(
        this ILogger logger,
        SIPURI resourceURI,
        SIPEventPackagesEnum eventPackage);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResubscribeScheduling",
        Level = LogLevel.Debug,
        Message = "Rescheduling next attempt for a successful subscription to {ResourceURI} in {Expiry}s.")]
    public static partial void LogResubscribeScheduling(
        this ILogger logger,
        SIPURI resourceURI,
        double expiry);

    [LoggerMessage(
        EventId = 0,
        EventName = "SubscriptionAttemptsHalted",
        Level = LogLevel.Warning,
        Message = "Subscription attempts to {ResourceURI} for {EventPackage} have been halted.")]
    public static partial void LogSubscriptionAttemptsHalted(
        this ILogger logger,
        SIPURI resourceURI,
        SIPEventPackagesEnum eventPackage);

    [LoggerMessage(
        EventId = 0,
        EventName = "SubMaxAttemptsReached",
        Level = LogLevel.Warning,
        Message = "Subscription to {SubscribeURI} reached the maximum number of allowed attempts without a failure condition.")]
    public static partial void LogSubMaxAttemptsReached(
        this ILogger logger,
        SIPURI subscribeURI);

    [LoggerMessage(
        EventId = 0,
        EventName = "SubscribeIntervalTooBrief",
        Level = LogLevel.Warning,
        Message = "A subscribe request was rejected with IntervalTooBrief, adjusting expiry to {Expiry} and trying again.")]
    public static partial void LogSubscribeIntervalTooBrief(
        this ILogger logger,
        double expiry);

    [LoggerMessage(
        EventId = 0,
        EventName = "AuthAttempt",
        Level = LogLevel.Debug,
        Message = "Attempting authentication for subscribe request for event package {EventPackage} and {ResourceURI}.")]
    public static partial void LogAuthAttempt(
        this ILogger logger,
        SIPEventPackagesEnum eventPackage,
        SIPURI resourceURI);

    [LoggerMessage(
        EventId = 0,
        EventName = "AuthSuccessful",
        Level = LogLevel.Debug,
        Message = "Authenticating subscribe request for event package {EventPackage} and {ResourceURI} was successful.")]
    public static partial void LogAuthSuccessful(
        this ILogger logger,
        SIPEventPackagesEnum eventPackage,
        SIPURI resourceURI);

    [LoggerMessage(
        EventId = 0,
        EventName = "CancelWithReason",
        Level = LogLevel.Debug,
        Message = "B2BUserAgent server call was cancelled with reason {CancelReason}.")]
    public static partial void LogCancelWithReason(
        this ILogger logger,
        string cancelReason);

    [LoggerMessage(
        EventId = 0,
        EventName = "B2BUserAgentCancel",
        Level = LogLevel.Debug,
        Message = "SIPB2BUserAgent Cancel.")]
    public static partial void LogB2BUserAgentCancel(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientCallFailed",
        Level = LogLevel.Debug,
        Message = "B2BUserAgent client call failed {Error}.")]
    public static partial void LogClientCallFailed(
        this ILogger logger,
        string error);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientCallAnswered",
        Level = LogLevel.Debug,
        Message = "B2BUserAgent client call answered {ShortDescription}.")]
    public static partial void LogClientCallAnswered(
        this ILogger logger,
        string shortDescription);

    [LoggerMessage(
        EventId = 0,
        EventName = "DisabledSIPAccountForMethod",
        Level = LogLevel.Warning,
        Message = "SIP account {SIPUsername}@{SIPDomain} is disabled for {Method}.")]
    public static partial void LogDisabledSIPAccountForMethod(
        this ILogger logger,
        string sipUsername,
        string sipDomain,
        SIPMethodsEnum method);

    [LoggerMessage(
        EventId = 0,
        EventName = "StaleNonceAuthenticationFailure",
        Level = LogLevel.Warning,
        Message = "Authentication failed stale nonce for realm={SIPDomain}, username={SIPUsername}, uri={URI}, nonce={Nonce}, method={Method}.")]
    public static partial void LogStaleNonceAuthenticationFailure(
        this ILogger logger,
        string sipDomain,
        string sipUsername,
        string uri,
        string nonce,
        SIPMethodsEnum method);

    [LoggerMessage(
        EventId = 0,
        EventName = "TokenCheckFailed",
        Level = LogLevel.Warning,
        Message = "Authentication token check failed for realm={SIPDomain}, username={SIPUsername}, uri={Uri}, nonce={RequestNonce}, method={SipRequestMethod}.")]
    public static partial void LogTokenCheckFailed(
        this ILogger logger,
        string sipDomain,
        string sipUsername,
        string uri,
        string requestNonce,
        SIPMethodsEnum sipRequestMethod);

    [LoggerMessage(
        EventId = 0,
        EventName = "ParseCustomHeaderMissingColon",
        Level = LogLevel.Warning,
        Message = "ParseCustomHeaders skipping custom header due to missing colon, {CustomHeader}.")]
    public static partial void LogParseCustomHeaderMissingColon(
        this ILogger logger,
        string customHeader);

    [LoggerMessage(
        EventId = 0,
        EventName = "ParseCustomHeaderNonPermitted",
        Level = LogLevel.Warning,
        Message = "ParseCustomHeaders skipping custom header due to an non-permitted string in header name, {CustomHeader}.")]
    public static partial void LogParseCustomHeaderNonPermitted(
        this ILogger logger,
        string customHeader);

    [LoggerMessage(
        EventId = 0,
        EventName = "SetRemoteDescFromACK",
        Level = LogLevel.Warning,
        Message = "Error setting remote description from ACK {SetRemoteResult}.")]
    public static partial void LogSetRemoteDescFromACK(
        this ILogger logger,
        SetDescriptionResultEnum setRemoteResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "ExceptionParseCustomHeaders",
        Level = LogLevel.Error,
        Message = "Exception ParseCustomHeaders ({CustomHeaders}). {Message}")]
    public static partial void LogExceptionParseCustomHeaders(
        this ILogger logger,
        string customHeaders,
        string message,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "GetInviteRequestCustomHeaderParseError",
        Level = LogLevel.Error,
        Message = "Exception Parsing CustomHeader for GetInviteRequest. {CustomHeaders}",
        SkipEnabledCheck = true)]
    private static partial void LogGetInviteRequestCustomHeaderParseErrorUnchecked(
        this ILogger logger,
        Exception exception,
        string customHeaders);

    public static void LogGetInviteRequestCustomHeaderParseError(
        this ILogger logger,
        Exception exception,
        List<string> customHeaders)
    {
        if (logger.IsEnabled(LogLevel.Error))
        {
            LogGetInviteRequestCustomHeaderParseErrorUnchecked(logger, exception, string.Join(',', customHeaders));
        }
    }
}
