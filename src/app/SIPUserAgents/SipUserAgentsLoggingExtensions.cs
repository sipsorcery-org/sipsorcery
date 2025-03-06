using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    internal static partial class SipUserAgentsLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "B2BUserAgentServerCallCancelled",
            Level = LogLevel.Debug,
            Message = "B2BUserAgent server call was cancelled with reason {CancelReason}")]
        public static partial void LogB2BUserAgentServerCallCancelled(
            this ILogger logger,
            string? cancelReason);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SIPB2BUserAgentCallCancelled", 
            Level = LogLevel.Debug, 
            Message = "SIPB2BUserAgent Cancel.")]
        public static partial void LogSIPB2BUserAgentCallCancelled(
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
            EventName = "OutboundProxy", 
            Level = LogLevel.Debug, 
            Message = "SIPClientUserAgent Call using alternate outbound proxy of {ServerEndPoint}.")]
        public static partial void LogOutboundProxy(
            this ILogger logger,
            string serverEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RouteSet", 
            Level = LogLevel.Debug, 
            Message = "Route set for call {RouteSet}.")]
        public static partial void LogRouteSet(
            this ILogger logger,
            string routeSet);

        [LoggerMessage(
            EventId = 0, 
            EventName = "DNSLookup", 
            Level = LogLevel.Debug, 
            Message = "SIPClientUserAgent attempting to resolve {Host}.")]
        public static partial void LogDNSLookup(
            this ILogger logger,
            string host);

        [LoggerMessage(
            EventId = 0, 
            EventName = "DNSFailure", 
            Level = LogLevel.Debug, 
            Message = "SIPClientUserAgent DNS failure resolving {Host} in {Duration:0.##}ms. Call cannot proceed.")]
        public static partial void LogDNSFailure(
            this ILogger logger,
            string host,
            double duration);

        [LoggerMessage(
            EventId = 0, 
            EventName = "DNSSuccess", 
            Level = LogLevel.Debug, 
            Message = "SIPClientUserAgent resolved {Host} to {Result} in {Duration:0.##}ms.")]
        public static partial void LogDNSSuccess(
            this ILogger logger,
            string host,
            string result,
            double duration);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CommencingCall", 
            Level = LogLevel.Debug, 
            Message = "UAC commencing call to {CanonicalAddress}.")]
        public static partial void LogCommencingCall(
            this ILogger logger,
            string canonicalAddress);

        [LoggerMessage(
            EventId = 0, 
            EventName = "InvalidOutboundProxy", 
            Level = LogLevel.Debug, 
            Message = "Error an outbound proxy value was not recognised in SIPClientUserAgent Call. {RouteSet}.")]
        public static partial void LogInvalidOutboundProxy(
            this ILogger logger,
            string routeSet);

        [LoggerMessage(
            EventId = 0, 
            EventName = "EmptyCallBody", 
            Level = LogLevel.Debug, 
            Message = "Body on UAC call was empty.")]
        public static partial void LogEmptyCallBody(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CallCancelled", 
            Level = LogLevel.Debug, 
            Message = "Cancelling forwarded call leg {Uri}, server transaction has not been created yet no CANCEL request required.")]
        public static partial void LogCallCancelled(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CallCancelRetry", 
            Level = LogLevel.Debug, 
            Message = "Call {Uri} has already been cancelled once, trying again.")]
        public static partial void LogCallCancelRetry(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CallCancelResponse", 
            Level = LogLevel.Debug, 
            Message = "Call {Uri} has already responded to CANCEL, probably overlap in messages not re-sending.")]
        public static partial void LogCallCancelResponse(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CallCancelSending", 
            Level = LogLevel.Debug, 
            Message = "Cancelling forwarded call leg, sending CANCEL to {URI}.")]
        public static partial void LogCallCancelSending(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "Response", 
            Level = LogLevel.Debug, 
            Message = "Response {StatusCode} {ReasonPhrase} for {URI}.")]
        public static partial void LogResponse(
            this ILogger logger,
            string statusCode,
            string reasonPhrase,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CallCancelledAndAnswered", 
            Level = LogLevel.Debug, 
            Message = "A cancelled call to {Uri} has been answered AND has already been hungup, no further action being taken.")]
        public static partial void LogCallCancelledAndAnswered(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CallCancelledHangingUp", 
            Level = LogLevel.Debug, 
            Message = "A cancelled call to {Uri} has been answered, hanging up.")]
        public static partial void LogCallCancelledHangingUp(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NoContactHeader", 
            Level = LogLevel.Debug, 
            Message = "No contact header provided on response for cancelled call to {Uri} no further action.")]
        public static partial void LogNoContactHeader(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NoCredentials", 
            Level = LogLevel.Debug, 
            Message = "Forward leg failed, authentication was requested but no credentials were available.")]
        public static partial void LogNoCredentials(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "InformationResponse", 
            Level = LogLevel.Debug, 
            Message = "Information response {StatusCode} {ReasonPhrase} for {URI}.")]
        public static partial void LogInformationResponse(
            this ILogger logger,
            string statusCode,
            string reasonPhrase,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ServerFinalResponseException", 
            Level = LogLevel.Debug, 
            Message = "Exception ServerFinalResponseReceived. {ErrorMessage}")]
        public static partial void LogServerFinalResponseException(
            this ILogger logger,
            string errorMessage);

        [LoggerMessage(
            EventId = 0, 
            EventName = "AuthenticatedResponse", 
            Level = LogLevel.Debug, 
            Message = "Server response {StatusCode} {ReasonPhrase} received for authenticated {Method} to {Uri}.", 
            SkipEnabledCheck = true)]
        private static partial void LogAuthenticatedResponseUnchecked(
            this ILogger logger,
            SIPResponseStatusCodesEnum statusCode,
            string reasonPhrase,
            SIPMethodsEnum method,
            string uri);

        public static void LogAuthenticatedResponse(
            this ILogger logger,
            SIPResponseStatusCodesEnum statusCode,
            string reasonPhrase,
            SIPMethodsEnum method,
            string uri)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogAuthenticatedResponseUnchecked(
                    statusCode,
                    reasonPhrase.IsNullOrBlank() ? statusCode.ToString() : reasonPhrase,
                    method,
                    uri);
            }
        }

        [LoggerMessage(
            EventId = 0, 
            EventName = "NonInviteAuthenticatedByIP", 
            Level = LogLevel.Debug, 
            Message = "{Method} request from {RemoteEndPoint} successfully authenticated by IP address.")]
        public static partial void LogNonInviteAuthenticatedByIP(
            this ILogger logger,
            SIPMethodsEnum method,
            SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NonInviteAuthenticatedByDigest", 
            Level = LogLevel.Debug, 
            Message = "{Method} request from {RemoteEndPoint} successfully authenticated by digest.")]
        public static partial void LogNonInviteAuthenticatedByDigest(
            this ILogger logger,
            SIPMethodsEnum method,
            SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NonInviteNotAuthenticated", 
            Level = LogLevel.Debug, 
            Message = "{TransactionRequestMethod} request not authenticated for {SipUsername}@{SipDomain}, responding with {ErrorResponse}.")]
        public static partial void LogNonInviteNotAuthenticated(
            this ILogger logger,
            SIPMethodsEnum transactionRequestMethod,
            string sipUsername,
            string sipDomain,
            SIPResponseStatusCodesEnum errorResponse);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NonInviteRequestSucceeded", 
            Level = LogLevel.Debug, 
            Message = "{Method} request succeeded with a response status of {StatusCode} {ReasonPhrase}.")]
        public static partial void LogNonInviteRequestSucceeded(
            this ILogger logger,
            SIPMethodsEnum method,
            int statusCode,
            string reasonPhrase);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NonInviteRequestFailed", 
            Level = LogLevel.Debug, 
            Message = "{Method} request failed with a response status of {StatusCode} {ReasonPhrase}.")]
        public static partial void LogNonInviteRequestFailed(
            this ILogger logger,
            SIPMethodsEnum method,
            int statusCode,
            string reasonPhrase);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NotifierClientStopped", 
            Level = LogLevel.Debug, 
            Message = "Stopping SIP notifier user agent for user {AuthUsername} and resource URI {ResourceURI}.")]
        public static partial void LogNotifierClientStopped(
            this ILogger logger,
            string authUsername,
            SIPURI resourceURI);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NotifierClientStarting", 
            Level = LogLevel.Debug, 
            Message = "SIPNotifierClient starting for {ResourceURI} and event package {EventPackage}.")]
        public static partial void LogNotifierClientStarting(
            this ILogger logger,
            SIPURI resourceURI,
            SIPEventPackagesEnum eventPackage);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ReschedulingNextAttempt", 
            Level = LogLevel.Debug, 
            Message = "Rescheduling next attempt for a successful subscription to {ResourceURI} in {Expiry}s.")]
        public static partial void LogReschedulingNextAttempt(
            this ILogger logger,
            SIPURI resourceURI,
            long expiry);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NotificationRequest", 
            Level = LogLevel.Debug, 
            Message = "SIPNotifierClient GotNotificationRequest for {Method} {URI} {CSeq}.")]
        public static partial void LogNotificationRequest(
            this ILogger logger,
            SIPMethodsEnum method,
            SIPURI uri,
            int cseq);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationAgentStarting", 
            Level = LogLevel.Debug, 
            Message = "Starting SIPRegistrationUserAgent for {SipAccountAOR}, callback period {CallbackPeriodSeconds}s.")]
        public static partial void LogRegistrationAgentStarting(
            this ILogger logger,
            SIPURI sipAccountAOR,
            long callbackPeriodSeconds);

        [LoggerMessage(
            EventId = 0, 
            EventName = "StartingRegistration", 
            Level = LogLevel.Debug, 
            Message = "Starting registration for {SipAccountAOR}.")]
        public static partial void LogStartingRegistration(
            this ILogger logger,
            SIPURI sipAccountAOR);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationSuccessful", 
            Level = LogLevel.Debug, 
            Message = "SIPRegistrationUserAgent was successful, scheduling next registration to {SipAccountAOR} in {RefreshTime}s.")]
        public static partial void LogRegistrationSuccessful(
            this ILogger logger,
            SIPURI sipAccountAOR,
            long refreshTime);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationTemporarilyFailed", 
            Level = LogLevel.Debug, 
            Message = "SIPRegistrationUserAgent temporarily failed, scheduling next registration to {SipAccountAOR} in {RetryInterval}s.")]
        public static partial void LogRegistrationTemporarilyFailed(
            this ILogger logger,
            SIPURI sipAccountAOR,
            int retryInterval);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationAgentStopping", 
            Level = LogLevel.Debug, 
            Message = "Stopping SIP registration user agent for {SipAccountAOR}.")]
        public static partial void LogRegistrationAgentStopping(
            this ILogger logger,
            SIPURI sipAccountAOR);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationHostResolutionFailed", 
            Level = LogLevel.Warning, 
            Message = "Could not resolve {RegistrarHost} when sending initial registration request.")]
        public static partial void LogRegistrationHostResolutionFailed(
            this ILogger logger,
            string registrarHost);

        [LoggerMessage(
            EventId = 0, 
            EventName = "InitiatingRegistration", 
            Level = LogLevel.Debug, 
            Message = "Initiating registration to {RegistrarHost} at {RegistrarSIPEndPoint} for {SipAccountAOR}.")]
        public static partial void LogInitiatingRegistration(
            this ILogger logger,
            string registrarHost,
            SIPEndPoint registrarSIPEndPoint,
            SIPURI sipAccountAOR);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ServerResponseReceived", 
            Level = LogLevel.Debug, 
            Message = "Server response {SipResponseStatus} received for {SipAccountAOR}.")]
        public static partial void LogServerResponseReceived(
            this ILogger logger,
            SIPResponseStatusCodesEnum sipResponseStatus,
            SIPURI sipAccountAOR);

        [LoggerMessage(
            EventId = 0, 
            EventName = "InvalidUASResponse", 
            Level = LogLevel.Debug, 
            Message = "UAS call was passed an invalid response status of {ProgressStatus}({ProgressStatusValue}), ignoring.")]
        public static partial void LogInvalidUASResponse(
            this ILogger logger,
            SIPResponseStatusCodesEnum progressStatus,
            int progressStatusValue);

        [LoggerMessage(
            EventId = 0, 
            EventName = "IgnoreProgressResponse", 
            Level = LogLevel.Debug, 
            Message = "UAS call ignoring progress response with status of {ProgressStatus}({ProgressStatusValue}) as already in {TransactionState}.")]
        public static partial void LogIgnoreProgressResponse(
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
            EventName = "UASAlreadyAnswered", 
            Level = LogLevel.Debug, 
            Message = "UAS Answer was called on an already answered call, ignoring.")]
        public static partial void LogUASAlreadyAnswered(
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
            EventName = "CallAuthenticatedByIP", 
            Level = LogLevel.Debug, 
            Message = "New call from {RemoteEndPoint} successfully authenticated by IP address.")]
        public static partial void LogCallAuthenticatedByIP(
            this ILogger logger,
            SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CallAuthenticatedByDigest", 
            Level = LogLevel.Debug, 
            Message = "New call from {RemoteEndPoint} successfully authenticated by digest.")]
        public static partial void LogCallAuthenticatedByDigest(
            this ILogger logger,
            SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "AttemptingSubscribeAuthentication", 
            Level = LogLevel.Debug, 
            Message = "Attempting authentication for subscribe request for event package {EventPackage} and {ResourceURI}.")]
        public static partial void LogAttemptingSubscribeAuthentication(
            this ILogger logger,
            SIPEventPackagesEnum eventPackage,
            SIPURI resourceURI);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationMaxAttempts", 
            Level = LogLevel.Debug, 
            Message = "Registration to {SIPAccountAOR} reached the maximum number of allowed attempts without a failure condition.")]
        public static partial void LogRegistrationMaxAttemptsDebug(
            this ILogger logger,
            SIPURI sipAccountAOR);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationMaxAttempts", 
            Level = LogLevel.Warning, 
            Message = "Registration to {SIPAccountAOR} reached the maximum number of allowed attempts without a failure condition.")]
        public static partial void LogRegistrationMaxAttemptsWarning(
            this ILogger logger,
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
            EventName = "RegistrationTooShortExpiry", 
            Level = LogLevel.Debug, 
            Message = "Registration for {SIPAccountAOR} had a too short expiry, updated to {Expiry} and trying again.")]
        public static partial void LogRegistrationTooShortExpiryDebug(
            this ILogger logger,
            SIPURI sipAccountAOR,
            long expiry);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationTooShortExpiry", 
            Level = LogLevel.Warning, 
            Message = "Registration for {SIPAccountAOR} had a too short expiry, updated to {Expiry} and trying again.")]
        public static partial void LogRegistrationTooShortExpiryWarning(
            this ILogger logger,
            SIPURI sipAccountAOR,
            long expiry);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UASRejectInvalidResponseStatus", 
            Level = LogLevel.Debug, 
            Message = "UAS Reject was passed an invalid response status of {FailureStatusCode}({FailureStatusValue}), ignoring.")]
        public static partial void LogUASRejectInvalidResponseStatus(
            this ILogger logger,
            SIPResponseStatusCodesEnum failureStatusCode,
            int failureStatusValue);

        [LoggerMessage(
            EventId = 0, 
            EventName = "Response", 
            Level = LogLevel.Debug, 
            Message = "Response {StatusCode} {ReasonPhrase} for {TransactionRequestURI}.")]
        public static partial void LogResponse(
            this ILogger logger,
            int statusCode,
            string reasonPhrase,
            SIPURI transactionRequestURI);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ServerUserAgentCancellationRequest", 
            Level = LogLevel.Debug, 
            Message = "SIPServerUserAgent got cancellation request.")]
        public static partial void LogServerUserAgentCancellationRequest(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ServerUserAgentClientFailed", 
            Level = LogLevel.Debug, 
            Message = "SIPServerUserAgent client failed with {FailureReason} in transaction state {TransactionState}.")]
        public static partial void LogServerUserAgentClientFailed(
            this ILogger logger,
            SocketError failureReason,
            SIPTransactionStatesEnum transactionState);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentRingTimeout", 
            Level = LogLevel.Debug, 
            Message = "Setting ring timeout of {RingTimeout}s.")]
        public static partial void LogUserAgentRingTimeout(
            this ILogger logger,
            int ringTimeout);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentSetRemoteDescription", 
            Level = LogLevel.Debug, 
            Message = "Set remote description for early media result {SetDescriptionResult}.")]
        public static partial void LogUserAgentSetRemoteDescription(
            this ILogger logger,
            SetDescriptionResultEnum setDescriptionResult);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentIncomingCall", 
            Level = LogLevel.Information, 
            Message = "Incoming call request: {LocalSIPEndPoint}<-{RemoteEndPoint}, uri:{URI}.")]
        public static partial void LogUserAgentIncomingCall(
            this ILogger logger,
            SIPEndPoint localSIPEndPoint,
            SIPEndPoint remoteEndPoint,
            SIPURI uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransferResult", 
            Level = LogLevel.Debug, 
            Message = "Result of calling transfer destination {TransferResult}.")]
        public static partial void LogUserAgentTransferResult(
            this ILogger logger,
            bool transferResult);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransferSucceeded", 
            Level = LogLevel.Debug, 
            Message = "Transfer succeeded, hanging up original call.")]
        public static partial void LogUserAgentTransferSucceeded(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentCancelled", 
            Level = LogLevel.Debug, 
            Message = "The incoming call has been cancelled.")]
        public static partial void LogUserAgentCallCancelled(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransferRequest", 
            Level = LogLevel.Debug, 
            Message = "Transfer request received, referred by {ReferredBy}, refer to {ReferToUserField}.")]
        public static partial void LogUserAgentTransferRequest(
            this ILogger logger,
            string referredBy,
            SIPUserField referToUserField);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransferRejected", 
            Level = LogLevel.Debug, 
            Message = "Transfer request was rejected by application.")]
        public static partial void LogUserAgentTransferRejected(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransferDestinationUri", 
            Level = LogLevel.Debug, 
            Message = "Calling transfer destination URI {ReferToUri}.")]
        private static partial void LogUserAgentTransferDestinationUriUnchecked(
            this ILogger logger,
            string referToUri);

        public static void LogUserAgentTransferDestinationUri(

            this ILogger logger,
            SIPURI referToUri)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogUserAgentTransferDestinationUriUnchecked(referToUri.ToParameterlessString());
            }
        }

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentReInviteResponse", 
            Level = LogLevel.Debug, 
            Message = "Re-INVITE response received for original Call-ID, disregarding.")]
        public static partial void LogUserAgentReInviteResponse(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentNoDialog", 
            Level = LogLevel.Warning, 
            Message = "No dialog available, re-INVITE request cannot be sent.")]
        public static partial void LogUserAgentNoDialog(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentReInviteReceived", 
            Level = LogLevel.Debug, 
            Message = "Re-INVITE request received {StatusLine}.")]
        public static partial void LogUserAgentReInviteReceived(
            this ILogger logger,
            string statusLine);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentUnableToCreateAnswer", 
            Level = LogLevel.Warning, 
            Message = "Unable to create an answer for the re-INVITE request.")]
        public static partial void LogUserAgentUnableToCreateAnswer(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentUnableToSetRemoteDescription", 
            Level = LogLevel.Warning, 
            Message = "Unable to set remote description from reINVITE request {SetRemoteResult}")]
        public static partial void LogUserAgentUnableToSetRemoteDescription(
            this ILogger logger,
            SetDescriptionResultEnum setRemoteResult);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransferNoReferTo", 
            Level = LogLevel.Warning, 
            Message = "A REFER request was received from {RemoteSIPEndPoint} without a Refer-To header.")]
        public static partial void LogUserAgentTransferNoReferTo(
            this ILogger logger,
            SIPEndPoint remoteSIPEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransferNoDialog", 
            Level = LogLevel.Warning, 
            Message = "A REFER request was received from {RemoteSIPEndPoint} when there was no dialog or the dialog was not in a ready state.")]
        public static partial void LogUserAgentTransferNoDialog(
            this ILogger logger,
            SIPEndPoint remoteSIPEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentAttendedTransferInviteReceived", 
            Level = LogLevel.Debug, 
            Message = "INVITE for attended transfer received, Replaces CallID {ReplacesCallID}, our dialog Call-ID {DialogCallID}.")]
        public static partial void LogUserAgentAttendedTransferInviteReceived(
            this ILogger logger,
            string replacesCallID,
            string dialogCallID);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentAttendedTransferInvalidDialog", 
            Level = LogLevel.Debug, 
            Message = "The attended transfer INVITE's Replaces header did not match the current dialog, rejecting.")]
        public static partial void LogUserAgentAttendedTransferInvalidDialog(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentAttendedTransferProceeding", 
            Level = LogLevel.Debug, 
            Message = "Proceeding with attended transfer INVITE received from {RemoteEndPoint}.")]
        public static partial void LogUserAgentAttendedTransferProceeding(
            this ILogger logger,
            SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentCurrentCallOnHold", 
            Level = LogLevel.Debug, 
            Message = "Current call placed on hold.")]
        public static partial void LogUserAgentCurrentCallOnHold(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentAttendedTransferAnswered", 
            Level = LogLevel.Debug, 
            Message = "Attended transfer was successfully answered, hanging up original call.")]
        public static partial void LogUserAgentAttendedTransferAnswered(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentAttendedTransferFailed", 
            Level = LogLevel.Debug, 
            Message = "Attended transfer answer failed, taking original call off hold.")]
        public static partial void LogUserAgentAttendedTransferFailed(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentDialogNotCreated", 
            Level = LogLevel.Warning, 
            Message = "The attempt to answer a call failed as the dialog was not created. The likely cause is the ACK not being received in time.")]
        public static partial void LogUserAgentDialogNotCreated(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentCallAnswered", 
            Level = LogLevel.Information, 
            Message = "Call attempt to {Uri} was answered; no media update from early media.")]
        public static partial void LogUserAgentCallAnsweredNoMediaUpdate(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentRtpChannelClosed", 
            Level = LogLevel.Warning, 
            Message = "RTP channel was closed with reason {CloseReason}.")]
        public static partial void LogUserAgentRtpChannelClosed(
            this ILogger logger,
            string closeReason);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentRtpTimeout", 
            Level = LogLevel.Warning, 
            Message = "RTP has timed out for media {MediaType} hanging up call.")]
        public static partial void LogUserAgentRtpTimeout(
            this ILogger logger,
            SDPMediaTypesEnum mediaType);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransferError", 
            Level = LogLevel.Error, 
            Message = "Exception processing transfer request. {ErrorMessage}")]
        public static partial void LogUserAgentTransferError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentBlindTransferUnavailable", 
            Level = LogLevel.Warning, 
            Message = "Blind transfer was called on the SIPUserAgent when no dialogue was available.")]
        public static partial void LogUserAgentBlindTransferUnavailable(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentAttendedTransferUnavailable", 
            Level = LogLevel.Warning, 
            Message = "Attended transfer was called on the SIPUserAgent when no dialogue was available.")]
        public static partial void LogUserAgentAttendedTransferUnavailable(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SubscribeAuthenticationSuccess", 
            Level = LogLevel.Debug, 
            Message = "Authenticating subscribe request for event package {EventPackage} and {ResourceURI} was successful.")]
        public static partial void LogSubscribeAuthenticationSuccess(
            this ILogger logger,
            SIPEventPackagesEnum eventPackage,
            SIPURI resourceURI);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentCallFailed", 
            Level = LogLevel.Debug, 
            Message = "Call attempt to {Uri} failed with {ErrorMessage}.")]
        public static partial void LogUserAgentCallFailed(
            this ILogger logger,
            string uri,
            string errorMessage);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentCallAnswered", 
            Level = LogLevel.Debug, 
            Message = "Call attempt to {Uri} was answered.")]
        public static partial void LogUserAgentCallAnswered(
            this ILogger logger,
            string uri);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RtpEvent", 
            Level = LogLevel.Debug, 
            Message = "RTP event {RtpEventID}.")]
        public static partial void LogRtpEvent(
            this ILogger logger,
            byte rtpEventId);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RtpEventEnded", 
            Level = LogLevel.Debug, 
            Message = "RTP end of event {RtpEventID}.")]
        public static partial void LogRtpEventEnded(
            this ILogger logger,
            byte rtpEventId);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentClientCallTrying", 
            Level = LogLevel.Debug, 
            Message = "Call attempt to {Uri} received a trying response {ShortDescription}.")]
        public static partial void LogUserAgentClientCallTrying(
            this ILogger logger,
            string uri,
            string shortDescription);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentClientCallRinging", 
            Level = LogLevel.Debug, 
            Message = "Call attempt to {Uri} received a ringing response {ShortDescription}.")]
        public static partial void LogUserAgentClientCallRinging(
            this ILogger logger,
            string uri,
            string shortDescription);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentWarningReInviteFailed", 
            Level = LogLevel.Debug, 
            Message = "Re-INVITE request failed with response {ShortDescription}.")]
        public static partial void LogUserAgentWarningReInviteFailed(
            this ILogger logger,
            string shortDescription);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentWarningCallAnswerFailed", 
            Level = LogLevel.Debug, 
            Message = "Call attempt was answered with {ShortDescription} ({SetDescriptionResult}) but an {ErrorText}.")]
        public static partial void LogUserAgentWarningCallAnswerFailed(
            this ILogger logger,
            string shortDescription,
            SetDescriptionResultEnum setDescriptionResult,
            string errorText);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationUpdateExpiry", 
            Level = LogLevel.Debug, 
            Message = "Expiry for registration agent for {SipAccountAOR} updated from {OldExpiry} to {NewExpiry}.")]
        public static partial void LogRegistrationUpdateExpiry(
            this ILogger logger,
            SIPURI sipAccountAOR,
            long oldExpiry,
            int newExpiry);

        [LoggerMessage(
            EventId = 0, 
            EventName = "MediaSessionStartFailure", 
            Level = LogLevel.Error, 
            Message = "error occurred whilst starting the MediaSession")]
        public static partial void LogMediaSessionStartFailure(
            this ILogger logger,
            Exception ex);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ClientCallRingingHandlerFailed", 
            Level = LogLevel.Debug, 
            Message = "ClientCallRingingHandler failed, call will be cancelled.")]
        public static partial void LogClientCallRingingHandlerFailed(
            this ILogger logger,
            Exception ex);

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
            EventName = "CallTransferAccepted", 
            Level = LogLevel.Information, 
            Message = "Call transfer was accepted by remote server.")]
        public static partial void LogCallTransferAccepted(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Warning, 
            Message = "Call not authenticated for {sipUsername}@{sipDomain}, responding with {errorResponse}.")]
        public static partial void LogCallNotAuthenticated(
            this ILogger logger,
            string sipUsername,
            string sipDomain,
            SIPResponseStatusCodesEnum errorResponse);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Warning, 
            Message = "Attempt to send {method} to {uri} failed with {failureReason}.")]
        public static partial void LogClientCallFailure(
            this ILogger logger,
            SIPMethodsEnum method,
            string uri,
            SocketError failureReason);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Warning, 
            Message = "ParseCustomHeaders skipping custom header due to missing colon, {customHeader}.")]
        public static partial void LogCustomHeaderMissingColon(
            this ILogger logger,
            string customHeader);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Error, 
            Message = "Exception ParseCustomHeaders ({CustomHeaders}). {Message}")]
        public static partial void LogParseCustomHeadersError(
            this ILogger logger,
            string customHeaders,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Error, 
            Message = "Exception SIPNotifierClient Stop. {errorMessage}")]
        public static partial void LogNotifierClientStopError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Error, 
            Message = "Exception SIPNotifierClient StartSubscription. {errorMessage}")]
        public static partial void LogStartSubscriptionError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Error, 
            Message = "Exception ByServerFinalResponseReceived. {errorMessage}")]
        public static partial void LogByServerFinalResponseError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Error, 
            Message = "Exception SendNonInviteRequest. {errorMessage}")]
        public static partial void LogSendNonInviteRequestError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SIPRegistrarResolutionFailed", 
            Level = LogLevel.Warning, 
            Message = "SIPRegistrationAgent could not resolve {RegistrarHost}")]
        public static partial void LogSIPRegistrarResolutionFailed(
            this ILogger logger,
            string registrarHost);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ParseCustomHeaderError2", 
            Level = LogLevel.Error, 
            Message = "Exception Parsing CustomHeader for GetInviteRequest. {ErrorMessage} {CustomHeaders}", 
            SkipEnabledCheck = true)]
        private static partial void LogParseCustomHeaderError2Unchecked(
            this ILogger logger,
            string customHeaders,
            string errorMessage,
            Exception exception);

        public static void LogParseCustomHeaderError2(

            this ILogger logger,
            IEnumerable<string> customHeaders,
            string errorMessage,
            Exception exception)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogParseCustomHeaderError2Unchecked(
                    string.Join(", ", customHeaders),
                    errorMessage,
                    exception);
            }
        }

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationStopError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPRegistrationUserAgent Stop. {ErrorMessage}")]
        public static partial void LogRegistrationStopError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ServerResponseReceivedError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPRegistrationUserAgent ServerResponseReceived ({RemoteEndPoint}). {ErrorMessage}")]
        public static partial void LogServerResponseReceivedError(
            this ILogger logger,
            SIPEndPoint remoteEndPoint,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "AuthResponseReceivedError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPRegistrationUserAgent AuthResponseReceived. {ErrorMessage}")]
        public static partial void LogAuthResponseReceivedError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Error, 
            Message = "Exception SIPServerUserAgent AuthenticateCall. {ErrorMessage}", 
            EventName = "AuthenticateCallError")]
        public static partial void LogAuthenticateCallError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Error, 
            Message = "Exception DoRegistration Start.", 
            EventName = "DoRegistrationError")]
        public static partial void LogDoRegistrationError(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            Level = LogLevel.Warning, 
            Message = "Registration failed with {Status} for {SIPAccountAOR}.", 
            EventName = "RegistrationFailurePermanent")]
        public static partial void LogRegistrationFailedPermanent(
            this ILogger logger,
            SIPResponseStatusCodesEnum status,
            SIPURI sipAccountAOR);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UnequivocalFailure", 
            Level = LogLevel.Warning, 
            Message = "Registration unequivocal failure with {Status} for {SipAccountAOR}{Action}.")]
        public static partial void LogRegistrationUnequivocalFailure(
            this ILogger logger,
            SIPResponseStatusCodesEnum status,
            SIPURI sipAccountAOR,
            string action);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ExceptionInitialRegister", 
            Level = LogLevel.Error, 
            Message = "Exception SendInitialRegister to {RegistrarHost}. {ErrorMessage}")]
        public static partial void LogInitialRegisterError(
            this ILogger logger,
            string registrarHost,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ExceptionResolveRegistrar", 
            Level = LogLevel.Warning, 
            Message = "Could not resolve {RegistrarHost}.")]
        public static partial void LogRegistrarResolutionWarning(
            this ILogger logger,
            string registrarHost);

        [LoggerMessage(
            EventId = 0, 
            EventName = "AuthenticationNotSupplied", 
            Level = LogLevel.Warning, 
            Message = "Registration failed with {Status} but no authentication header was supplied for {SIPAccountAOR}.")]
        public static partial void LogAuthenticationHeaderMissing(
            this ILogger logger,
            SIPResponseStatusCodesEnum status,
            SIPURI sipAccountAOR);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentClientCallException", 
            Level = LogLevel.Error, 
            Message = "Exception UserAgentClient Call. {ErrorMessage}")]
        public static partial void LogUserAgentClientCallError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CancelServerCall", 
            Level = LogLevel.Error, 
            Message = "Exception CancelServerCall. {ErrorMessage}")]
        public static partial void LogCancelServerCall(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ExceptionHangup", 
            Level = LogLevel.Error, 
            Message = "Exception SIPClientUserAgent Hangup. {ErrorMessage}")]
        public static partial void LogHangupException(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "GetRequestError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPNonInviteClientUserAgent GetRequest. {ErrorMessage}")]
        public static partial void LogGetRequestError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NonInviteClientSendError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPNonInviteClientUserAgent SendRequest to {Uri}. {ErrorMessage}")]
        public static partial void LogNonInviteClientSendError(
            this ILogger logger,
            string uri,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ParseCustomHeadersNonInvite", 
            Level = LogLevel.Error, 
            Message = "Exception Parsing CustomHeader for SIPNonInviteClientUserAgent GetRequest. {ErrorMessage} {CustomHeaders}", 
            SkipEnabledCheck = true)]
        private static partial void LogParseCustomHeadersNonInviteUnchecked(
            this ILogger logger,
            string errorMessage,
            string customHeaders,
            Exception exception);

        public static void LogParseCustomHeadersNonInvite(

            this ILogger logger,
            IEnumerable<string> customHeaders,
            string errorMessage,
            Exception exception)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogParseCustomHeadersNonInviteUnchecked(
                    errorMessage,
                    string.Join(", ", customHeaders),
                    exception);
            }
        }

        [LoggerMessage(
            EventId = 0, 
            EventName = "UASAnswerError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPServerUserAgent Answer. {ErrorMessage}")]
        public static partial void LogUASAnswerError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UASRejectError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPServerUserAgent Reject. {ErrorMessage}")]
        public static partial void LogUASRejectError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NonInviteAuthenticateCallError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPNonInviteUserAgent AuthenticateCall. {ErrorMessage}")]
        internal static partial void LogNonInviteAuthenticateCallError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SIPServerUserAgentRedirectError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPServerUserAgent Redirect. {ErrorMessage}")]
        public static partial void LogSIPServerUserAgentRedirectError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SubscriptionHalted", 
            Level = LogLevel.Warning, 
            Message = "Subscription attempts to {ResourceURI} for {EventPackage} have been halted.")]
        public static partial void LogSubscriptionHalted(
            this ILogger logger,
            SIPURI resourceURI,
            SIPEventPackagesEnum eventPackage);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SubscriptionMaxAttempts", 
            Level = LogLevel.Warning, 
            Message = "Subscription to {SubscribeURI} reached the maximum number of allowed attempts without a failure condition.")]
        public static partial void LogSubscriptionMaxAttemptsReached(
            this ILogger logger,
            SIPURI subscribeURI);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SubscriptionIntervalAdjusted", 
            Level = LogLevel.Warning, 
            Message = "A subscribe request was rejected with IntervalTooBrief, adjusting expiry to {Expiry} and trying again.")]
        public static partial void LogSubscriptionIntervalAdjusted(
            this ILogger logger,
            long expiry);

        [LoggerMessage(
            EventId = 0, 
            EventName = "DuplicateNotifyRequest", 
            Level = LogLevel.Warning, 
            Message = "A duplicate NOTIFY request received by SIPNotifierClient for subscription Call-ID {CallID}.")]
        public static partial void LogDuplicateNotifyRequest(
            this ILogger logger,
            string CallID);

        [LoggerMessage(
            EventId = 0, 
            EventName = "NotifierClientSubscribeError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPNotifierClient Subscribe. {ErrorMessage}")]
        public static partial void LogNotifierClientSubscribeError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "DialogueNotAvailable", 
            Level = LogLevel.Warning, 
            Message = "Transfer was called on the SIPUserAgent when no dialogue was available.")]
        public static partial void LogDialogueNotAvailable(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ProgressOnAnsweredCall", 
            Level = LogLevel.Warning, 
            Message = "SIPServerUserAgent Progress fired on already answered call.")]
        public static partial void LogProgressOnAnsweredCall(
            this ILogger logger);

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
            EventName = "ProgressError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPServerUserAgent Progress. {Message}")]
        public static partial void LogProgressError(
            this ILogger logger,
            string message,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "AlreadyRejected", 
            Level = LogLevel.Warning, 
            Message = "SIPServerUserAgent Reject fired on already answered call.")]
        public static partial void LogAlreadyRejected(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RejectingAuthCall", 
            Level = LogLevel.Warning, 
            Message = "Rejecting authentication required call for {TransactionRequestFrom}, SIP account not found.")]
        public static partial void LogRejectingAuthCall(
            this ILogger logger,
            SIPUserField transactionRequestFrom);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SIPServerUserAgentHangupError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPServerUserAgent Hangup. {ErrorMessage}")]
        public static partial void LogSIPServerUserAgentHangupError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SetRemoteDescriptionInviteFailed", 
            Level = LogLevel.Warning, 
            Message = "Error setting remote description from INVITE {SetRemoteResult}.")]
        public static partial void LogSetRemoteDescriptionInviteFailed(
            this ILogger logger,
            SetDescriptionResultEnum setRemoteResult);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SetRemoteDescriptionAckFailed", 
            Level = LogLevel.Warning, 
            Message = "Error setting remote description from ACK {SetRemoteResult}.")]
        public static partial void LogSetRemoteDescriptionAckFailed(
            this ILogger logger,
            SetDescriptionResultEnum setRemoteResult);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CallTransferTimeout", 
            Level = LogLevel.Warning, 
            Message = "Call transfer request timed out after {TimeoutMilliseconds}ms.")]
        public static partial void LogCallTransferTimeout(
            this ILogger logger,
            double timeoutMilliseconds);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SubscriptionFinalResponseReceived", 
            Level = LogLevel.Error, 
            Message = "Exception SubscriptionFinalResponseReceived {ErrorMessage}")]
        public static partial void LogSubscriptionFinalResponseReceivedError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SubscribeTransactionFinalResponseError", 
            Level = LogLevel.Error, 
            Message = "Exception SubscribeTransactionFinalResponseReceived. {ErrorMessage}")]
        public static partial void LogSubscribeTransactionFinalResponseError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "RegistrationUnequivocalFailure", 
            Level = LogLevel.Warning, 
            Message = "Registration unequivocal failure with {Status} for {SIPAccountAOR}. No further registration attempts will be made: {Exit}.")]
        public static partial void LogRegistrationUnequivocalFailure(
            this ILogger logger,
            SIPResponseStatusCodesEnum Status,
            SIPURI SIPAccountAOR,
            bool Exit);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ByeRequestFailure", 
            Level = LogLevel.Warning, 
            Message = "Bye request for {Uri} failed with {Reason}.")]
        public static partial void LogByeRequestFailure(
            this ILogger logger,
            string uri,
            SocketError reason);

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
            EventName = "CallAnswerFailed", 
            Level = LogLevel.Warning, 
            Message = "Call attempt was answered with failure response {ShortDescription}.")]
        public static partial void LogCallAnswerFailed(
            this ILogger logger,
            string shortDescription);

        [LoggerMessage(
            EventId = 0, 
            EventName = "CancelError", 
            Level = LogLevel.Error, 
            Message = "ClientCallRingingHandler call could not be cancelled.")]
        public static partial void LogCancelError(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentTransactionError", 
            Level = LogLevel.Error, 
            Message = "Exception SIPUserAgent.SIPTransportRequestReceived. {ErrorMessage}")]
        public static partial void LogUserAgentTransactionError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentMediaError", 
            Level = LogLevel.Error, 
            Message = "MediaSession can't process the re-INVITE request.")]
        public static partial void LogUserAgentMediaError(
            this ILogger logger,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentError", 
            Level = LogLevel.Error, 
            Message = "User agent error: {ErrorMessage}")]
        public static partial void LogUserAgentError(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentStart", 
            Level = LogLevel.Debug, 
            Message = "User agent {Id} starting")]
        public static partial void LogUserAgentStart(
            this ILogger logger,
            string id);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserAgentStop", 
            Level = LogLevel.Debug, 
            Message = "User agent {Id} stopped")]
        public static partial void LogUserAgentStop(
            this ILogger logger,
            string id);
    }
}
