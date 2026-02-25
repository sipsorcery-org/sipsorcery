using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;

namespace SIPSorcery.Sys;

internal static partial class SysNetLoggingExtensions
{
    [LoggerMessage(
        EventId = 0,
        EventName = "CreateBoundSocketStart",
        Level = LogLevel.Debug,
        Message = "CreateBoundSocket attempting to create and bind socket(s) on {BindEndPoint} using protocol {Protocol}.")]
    public static partial void LogCreateBoundSocketStart(
        this ILogger logger,
        EndPoint? bindEndPoint,
        ProtocolType protocol);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateBoundSocketEvenPortClose",
        Level = LogLevel.Debug,
        Message = "CreateBoundSocket even port required, closing socket on {LocalEndPoint}, max port reached request new bind.")]
    public static partial void LogCreateBoundSocketEvenPortClose(
        this ILogger logger,
        EndPoint? localEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateBoundSocketEvenPortRetry",
        Level = LogLevel.Debug,
        Message = "CreateBoundSocket even port required, closing socket on {LocalEndPoint} and retrying on {NextPort}.")]
    public static partial void LogCreateBoundSocketEvenPortRetry(
        this ILogger logger,
        EndPoint? localEndPoint,
        int nextPort);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateBoundSocketSuccessDualMode",
        Level = LogLevel.Debug,
        Message = "CreateBoundSocket successfully bound on {LocalEndPoint}, dual mode {DualMode}.")]
    public static partial void LogCreateBoundSocketSuccessDualMode(
        this ILogger logger,
        EndPoint? localEndPoint,
        bool dualMode);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateBoundSocketSuccess",
        Level = LogLevel.Debug,
        Message = "CreateBoundSocket successfully bound on {LocalEndPoint}.")]
    public static partial void LogCreateBoundSocketSuccess(
        this ILogger logger,
        EndPoint? localEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "WSLBindCheck",
        Level = LogLevel.Debug,
        Message = "WSL detected, carrying out bind check on 0.0.0.0:{Port}.")]
    public static partial void LogWSLBindCheck(
        this ILogger logger,
        int port);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateRtpSocketStart",
        Level = LogLevel.Debug,
        Message = "CreateRtpSocket attempting to create and bind RTP socket(s) on {BindEndPoint}.")]
    public static partial void LogCreateRtpSocketStart(
        this ILogger logger,
        IPEndPoint bindEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateRtpSocketBindFailed",
        Level = LogLevel.Warning,
        Message = "CreateRtpSocket failed to create and bind RTP socket(s) on {BindEndPoint}, bind attempt {BindAttempt}.")]
    public static partial void LogCreateRtpSocketBindFailed(
        this ILogger logger,
        IPEndPoint bindEndPoint,
        int bindAttempt);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateRtpSocketSuccessDualMode",
        Level = LogLevel.Debug,
        Message = "Successfully bound RTP socket {LocalEndPoint} (dual mode {DualMode}) and control socket {ControlEndPoint} (dual mode {ControlDualMode}).")]
    public static partial void LogCreateRtpSocketSuccessDualMode(
        this ILogger logger,
        EndPoint localEndPoint,
        bool dualMode,
        EndPoint controlEndPoint,
        bool controlDualMode);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateRtpSocketSuccess",
        Level = LogLevel.Debug,
        Message = "Successfully bound RTP socket {LocalEndPoint} and control socket {ControlEndPoint}.")]
    public static partial void LogCreateRtpSocketSuccess(
        this ILogger logger,
        EndPoint localEndPoint,
        EndPoint controlEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateRtpSocketSingleSuccessDualMode",
        Level = LogLevel.Debug,
        Message = "Successfully bound RTP socket {LocalEndPoint} (dual mode {DualMode}).")]
    public static partial void LogCreateRtpSocketSingleSuccessDualMode(
        this ILogger logger,
        EndPoint localEndPoint,
        bool dualMode);

    [LoggerMessage(
        EventId = 0,
        EventName = "CreateRtpSocketSingleSuccess",
        Level = LogLevel.Debug,
        Message = "Successfully bound RTP socket {LocalEndPoint}.")]
    public static partial void LogCreateRtpSocketSingleSuccess(
        this ILogger logger,
        EndPoint localEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "DualModeSupportCheckFailed",
        Level = LogLevel.Warning,
        Message = "A socket 'receive from' attempt on a dual mode socket failed (dual mode RTP sockets will not be used) with {Message}")]
    public static partial void LogDualModeSupportCheckFailed(
        this ILogger logger,
        string message,
        Exception ex);

    [LoggerMessage(
        EventId = 0,
        EventName = "SocketBindAddressInUse",
        Level = LogLevel.Warning,
        Message = "Address already in use exception attempting to bind socket, attempt {BindAttempts}.")]
    public static partial void LogSocketBindAddressInUse(
        this ILogger logger,
        int bindAttempts);

    [LoggerMessage(
        EventId = 0,
        EventName = "SocketBindAccessDenied",
        Level = LogLevel.Warning,
        Message = "Access denied exception attempting to bind socket, attempt {BindAttempts}.")]
    public static partial void LogSocketBindAccessDenied(
        this ILogger logger,
        int bindAttempts);

    [LoggerMessage(
        EventId = 0,
        EventName = "SocketBindException",
        Level = LogLevel.Error,
        Message = "SocketException in NetServices.CreateBoundSocket. {ErrorMessage}")]
    public static partial void LogSocketBindException(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SocketBindInitialException",
        Level = LogLevel.Error,
        Message = "Exception in NetServices.CreateBoundSocket attempting the initial socket bind on address {BindAddress}.")]
    public static partial void LogSocketBindInitialException(
        this ILogger logger,
        IPAddress bindAddress,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "UdpConnectionResetDisableFailed",
        Level = LogLevel.Warning,
        Message = "CreateBoundSocket was unable to disable UDP connection reset handling on {LogEndPoint}. Continuing with bound socket.")]
    public static partial void LogUdpConnectionResetDisableFailed(
        this ILogger logger,
        EndPoint logEndPoint,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "UdpConnectionResetDisableNotSupported",
        Level = LogLevel.Warning,
        Message = "CreateBoundSocket does not support disabling UDP connection reset handling on {LogEndPoint}. Continuing with bound socket.")]
    public static partial void LogUdpConnectionResetDisableNotSupported(
        this ILogger logger,
        EndPoint logEndPoint,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "NetworkCertificateError",
        Level = LogLevel.Error,
        Message = "Exception loading network certificate. {ErrorMessage}")]
    public static partial void LogNetworkCertificateError(
        this ILogger logger,
        string errorMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "PendingTransactions",
        Level = LogLevel.Debug,
        Message = "== Pending Transactions ==",
        SkipEnabledCheck = true)]
    private static partial void LogPendingTransactionsUnchecked(
        this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "PendingTransaction",
        Level = LogLevel.Debug,
        Message = "Pending transaction {RequestMethod} {TransactionState} {TotalSeconds}s {Uri} ({Id}).",
        SkipEnabledCheck = true)]
    private static partial void LogPendingTransactionUnchecked(
        this ILogger logger,
        SIPMethodsEnum requestMethod,
        SIPTransactionStatesEnum transactionState,
        string totalSeconds,
        SIPURI uri,
        string id);

    public static void LogPendingTransactions(
        this ILogger logger,
        IReadOnlyDictionary<string, SIPTransaction> transactions)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogPendingTransactionsUnchecked();

            var now = DateTime.Now;
            foreach (var (_, transaction) in transactions)
            {
                logger.LogPendingTransactionUnchecked(
                    transaction.TransactionRequest.Method,
                    transaction.TransactionState,
                    now.Subtract(transaction.Created).TotalSeconds.ToString("0.##"),
                    transaction.TransactionRequestURI,
                    transaction.TransactionId);
            }
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "UASInviteUnexpectedState",
        Level = LogLevel.Warning,
        Message = "InviteServer Transaction entered an unexpected transaction state {TransactionState}.")]
    public static partial void LogUASInviteUnexpectedState(
        this ILogger logger,
        SIPTransactionStatesEnum transactionState);

    [LoggerMessage(
        EventId = 0,
        EventName = "ClientInviteUnexpectedState",
        Level = LogLevel.Warning,
        Message = "InviteClient Transaction entered an unexpected transaction state {TransactionState}.")]
    public static partial void LogClientInviteUnexpectedState(
        this ILogger logger,
        SIPTransactionStatesEnum transactionState);

    [LoggerMessage(
        EventId = 0,
        EventName = "NonInviteUnexpectedState",
        Level = LogLevel.Warning,
        Message = "NonInvite Transaction entered an unexpected transaction state {TransactionState}.")]
    public static partial void LogNonInviteUnexpectedState(
        this ILogger logger,
        SIPTransactionStatesEnum transactionState);

    [LoggerMessage(
        EventId = 0,
        EventName = "UnrecognisedTransactionType",
        Level = LogLevel.Warning,
        Message = "Unrecognised transaction type {TransactionType}.")]
    public static partial void LogUnrecognisedTransactionType(
        this ILogger logger,
        SIPTransactionTypesEnum transactionType);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTransactionSendFailed",
        Level = LogLevel.Warning,
        Message = "SIP transaction send failed in state {TransactionState} with error {SendResult}.")]
    public static partial void LogSIPTransactionSendFailed(
        this ILogger logger,
        SIPTransactionStatesEnum transactionState,
        SocketError sendResult);

    [LoggerMessage(
        EventId = 0,
        EventName = "PendingTransactionsException",
        Level = LogLevel.Error,
        Message = "Exception processing pending transactions.")]
    public static partial void LogPendingTransactionsException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "RemoveExpiredTransactionException",
        Level = LogLevel.Error,
        Message = "Exception RemoveExpiredTransaction.")]
    public static partial void LogRemoveExpiredTransactionException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTransactionException",
        Level = LogLevel.Error,
        Message = "Exception SIPTransaction.")]
    public static partial void LogSIPTransactionException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "NewReliableProvisionalResponse",
        Level = LogLevel.Warning,
        Message = "A new reliable provisional response is being sent but the previous one was not yet acknowledged.")]
    public static partial void LogNewReliableProvisionalResponse(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "GetInformationalResponseException",
        Level = LogLevel.Error,
        Message = "Exception GetInformationalResponse.")]
    public static partial void LogGetInformationalResponseException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "PRACKMatched",
        Level = LogLevel.Debug,
        Message = "PRACK request matched the current outstanding provisional response, setting as delivered.")]
    public static partial void LogPRACKMatched(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "AckRetransmitNoStoredRequest",
        Level = LogLevel.Warning,
        Message = "An ACK retransmit was required but there was no stored ACK request to send.")]
    public static partial void LogAckRetransmitNoStoredRequest(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResendAckRequestException",
        Level = LogLevel.Error,
        Message = "Exception ResendAckRequest.")]
    public static partial void LogResendAckRequestException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "PrackRetransmitNoStoredRequest",
        Level = LogLevel.Warning,
        Message = "A PRACK retransmit was required but there was no stored PRACK request to send.")]
    public static partial void LogPrackRetransmitNoStoredRequest(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResendPrackRequestException",
        Level = LogLevel.Error,
        Message = "Exception ResendPrackRequest.")]
    public static partial void LogResendPrackRequestException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "UASInviteUnexpectedResponse",
        Level = LogLevel.Warning,
        Message = "UASInviteTransaction received unexpected response, {ReasonPhrase} from {RemoteEndPoint}, ignoring.")]
    public static partial void LogUASInviteUnexpectedResponse(
        this ILogger logger,
        string reasonPhrase,
        SIPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "UASInviteCancelInvalidState",
        Level = LogLevel.Warning,
        Message = "A request was made to cancel transaction {TransactionId} that was not in the calling, trying or proceeding states, state={TransactionState}.")]
    public static partial void LogUASInviteCancelInvalidState(
        this ILogger logger,
        string transactionId,
        SIPTransactionStatesEnum transactionState);

    [LoggerMessage(
        EventId = 0,
        EventName = "UACInviteUnexpectedRequest",
        Level = LogLevel.Warning,
        Message = "UACInviteTransaction received unexpected request, {Method} from {RemoteEndPoint}, ignoring.")]
    public static partial void LogUACInviteUnexpectedRequest(
        this ILogger logger,
        SIPMethodsEnum method,
        SIPEndPoint remoteEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "UACInformationResponseException",
        Level = LogLevel.Error,
        Message = "Exception UACInviteTransaction_TransactionInformationResponseReceived.")]
    public static partial void LogUACInformationResponseException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "UACFinalResponseException",
        Level = LogLevel.Error,
        Message = "Exception UACInviteTransaction_TransactionFinalResponseReceived.")]
    public static partial void LogUACFinalResponseException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "GetResponseException",
        Level = LogLevel.Error,
        Message = "Exception SIPResponse.GetResponse.")]
    public static partial void LogGetResponseException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketOnOpen",
        Level = LogLevel.Debug,
        Message = "SIPMessagWebSocketBehavior.OnOpen.")]
    public static partial void LogWebSocketOnOpen(this ILogger logger);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketOnMessage",
        Level = LogLevel.Debug,
        Message = "SIPMessagWebSocketBehavior.OnMessage: bytes received {Length}.")]
    public static partial void LogWebSocketOnMessage(
        this ILogger logger,
        int length);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketOnClose",
        Level = LogLevel.Debug,
        Message = "SIPMessagWebSocketBehavior.OnClose: reason {Reason}, was clean {WasClean}.")]
    public static partial void LogWebSocketOnClose(
        this ILogger logger,
        string reason,
        bool wasClean);

    [LoggerMessage(
        EventId = 0,
        EventName = "WebSocketOnError",
        Level = LogLevel.Debug,
        Message = "SIPMessagWebSocketBehavior.OnError: reason {Message}.")]
    public static partial void LogWebSocketOnError(
        this ILogger logger,
        string message);

    [LoggerMessage(
        EventId = 0,
        EventName = "RequestReceived",
        Level = LogLevel.Debug,
        Message = "Request received: {LocalEP}<-{RemoteEP} {StatusLine}.",
        SkipEnabledCheck = false)]
    private static partial void LogRequestReceivedUnchecked(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        string statusLine);

    [LoggerMessage(
        EventName = "RequestReceivedDetailed",
        Level = LogLevel.Trace,
        Message = "Request received: {LocalEP}<-{RemoteEP}: {Request}.",
        SkipEnabledCheck = false)]
    private static partial void LogRequestReceivedDetailedUnchecked(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        SIPRequest request);

    public static void LogRequestReceived(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        SIPRequest request)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRequestReceivedUnchecked(logger, localEP, remoteEP, request.StatusLine);

            if (logger.IsEnabled(LogLevel.Trace))
            {
                LogRequestReceivedDetailedUnchecked(logger, localEP, remoteEP, request);
            }
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RequestSent",
        Level = LogLevel.Debug,
        Message = "Request sent: {LocalEP}->{RemoteEP} {StatusLine}.",
        SkipEnabledCheck = true)]
    private static partial void LogRequestSentUnchecked(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        string statusLine);

    [LoggerMessage(
        EventId = 0,
        EventName = "RequestSentDetailed",
        Level = LogLevel.Debug,
        Message = "Request sent: {LocalEP}->{RemoteEP}: {Request}.",
        SkipEnabledCheck = true)]
    private static partial void LogRequestSentDetailedUnchecked(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        SIPRequest request);

    public static void LogRequestSent(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        SIPRequest request)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogRequestSentUnchecked(logger, localEP, remoteEP, request.StatusLine);

            if (logger.IsEnabled(LogLevel.Trace))
            {
                LogRequestSentDetailedUnchecked(logger, localEP, remoteEP, request);
            }
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ResponseReceived",
        Level = LogLevel.Debug,
        Message = "Response received: {LocalEP}<-{RemoteEP} {ShortDescription}.",
        SkipEnabledCheck = true)]
    private static partial void LogResponseReceivedUnchecked(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        string shortDescription);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResponseReceivedDetailed",
        Level = LogLevel.Debug,
        Message = "Response received: {LocalEP}<-{RemoteEP}: {Response}.",
        SkipEnabledCheck = true)]
    private static partial void LogResponseReceivedDetailedUnchecked(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        SIPResponse response);

    public static void LogResponseReceived(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        SIPResponse response)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogResponseReceivedUnchecked(logger, localEP, remoteEP, response.ShortDescription);

            if (logger.IsEnabled(LogLevel.Trace))
            {
                LogResponseReceivedDetailedUnchecked(logger, localEP, remoteEP, response);
            }
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "ResponseSent",
        Level = LogLevel.Debug,
        Message = "Response sent: {LocalEP}->{RemoteEP}: {ShortDescription}.",
        SkipEnabledCheck = true)]
    private static partial void LogResponseSentUnchecked(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        string shortDescription);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResponseSentDetailed",
        Level = LogLevel.Debug,
        Message = "Response sent: {LocalEP}->{RemoteEP} {Response}.",
        SkipEnabledCheck = true)]
    private static partial void LogResponseSentDetailedUnchecked(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        SIPResponse response);

    public static void LogResponseSent(
        this ILogger logger,
        SIPEndPoint localEP,
        SIPEndPoint remoteEP,
        SIPResponse response)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            LogResponseSentUnchecked(logger, localEP, remoteEP, response.ShortDescription);

            if (logger.IsEnabled(LogLevel.Trace))
            {
                LogResponseSentDetailedUnchecked(logger, localEP, remoteEP, response);
            }
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "RequestRetransmit",
        Level = LogLevel.Debug,
        Message = "Request retransmit {Count} for request {StatusLine}, initial transmit {InitialTransmit}s ago.")]
    public static partial void LogRequestRetransmit(
        this ILogger logger,
        int count,
        string statusLine,
        string initialTransmit);

    [LoggerMessage(
        EventId = 0,
        EventName = "ResponseRetransmit",
        Level = LogLevel.Debug,
        Message = "Response retransmit {Count} for response {ShortDescription}, initial transmit {InitialTransmit}s ago.")]
    public static partial void LogResponseRetransmit(
        this ILogger logger,
        int count,
        string shortDescription,
        string initialTransmit);

    // SIPUserField logging
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUserFieldToStringException",
        Level = LogLevel.Error,
        Message = "Exception SIPUserField ToString.")]
    public static partial void LogSIPUserFieldToStringException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUserFieldToParameterlessStringException",
        Level = LogLevel.Error,
        Message = "Exception SIPUserField ToParameterlessString.")]
    public static partial void LogSIPUserFieldToParameterlessStringException(
        this ILogger logger,
        Exception exception);

    // SIPURI logging
    [LoggerMessage(
        EventId = 0,
        EventName = "FailedParseUserParametersWarning",
        Level = LogLevel.Warning,
        Message = "Failed to parse UserParameters.")]
    public static partial void LogFailedParseUserParametersWarning(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ParseSIPURIStringException",
        Level = LogLevel.Error,
        Message = "Exception ParseSIPURI ({Uri}).")]
    public static partial void LogParseSIPURIStringException(
        this ILogger logger,
        string uri,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPURIToStringException",
        Level = LogLevel.Error,
        Message = "Exception SIPURI ToString.")]
    public static partial void LogSIPURIToStringException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPURIParameterlessStringException",
        Level = LogLevel.Error,
        Message = "Exception SIPURI ParameterlessString.")]
    public static partial void LogSIPURIParameterlessStringException(
        this ILogger logger,
        Exception exception);

    // SIPParameterlessURI logging
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPParameterlessURIToStringException",
        Level = LogLevel.Error,
        Message = "Exception SIPParameterlessURI ToString.")]
    public static partial void LogSIPParameterlessURIToStringException(
        this ILogger logger,
        Exception exception);

    // SIPParameters logging
    [LoggerMessage(
        EventId = 0,
        EventName = "GetKeyValuePairsFromQuotedException",
        Level = LogLevel.Error,
        Message = "Exception GetKeyValuePairsFromQuoted.")]
    public static partial void LogGetKeyValuePairsFromQuotedException(
        this ILogger logger,
        Exception exception);

    // SIPDialogue logging
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPDialogueHangupException",
        Level = LogLevel.Error,
        Message = "Exception SIPDialogue Hangup.")]
    public static partial void LogSIPDialogueHangupException(
        this ILogger logger,
        Exception exception);

    // SIPHeader logging
    [LoggerMessage(
        EventId = 0,
        EventName = "ParseSIPHeadersException",
        Level = LogLevel.Error,
        Message = "Exception ParseSIPHeaders.")]
    public static partial void LogParseSIPHeadersException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPHeaderToStringException",
        Level = LogLevel.Error,
        Message = "Exception SIPHeader ToString.")]
    public static partial void LogSIPHeaderToStringException(
        this ILogger logger,
        Exception exception);

    // SIPTransport logging
    [LoggerMessage(
        EventId = 0,
        EventName = "AddSIPChannelException",
        Level = LogLevel.Error,
        Message = "Exception AddSIPChannel.")]
    public static partial void LogAddSIPChannelException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTransportShutdownException",
        Level = LogLevel.Error,
        Message = "Exception SIPTransport Shutdown.")]
    public static partial void LogSIPTransportShutdownException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTransportReceiveMessageException",
        Level = LogLevel.Error,
        Message = "Exception SIPTransport ReceiveMessage.")]
    public static partial void LogSIPTransportReceiveMessageException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTransportProcessReceiveQueueException",
        Level = LogLevel.Error,
        Message = "Exception SIPTransport ProcessReceiveQueue.")]
    public static partial void LogSIPTransportProcessReceiveQueueException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPMessageReceivedException",
        Level = LogLevel.Error,
        Message = "Exception SIPMessageReceived.")]
    public static partial void LogSIPMessageReceivedException(
        this ILogger logger,
        Exception exception);

    // SIPResponse logging
    [LoggerMessage(
        EventId = 0,
        EventName = "ParseSIPResponseBufferException",
        Level = LogLevel.Error,
        Message = "Exception ParseSIPResponse from buffer ({RawMessage}).")]
    public static partial void LogParseSIPResponseBufferException(
        this ILogger logger,
        string rawMessage,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "ParseSIPResponseStringException",
        Level = LogLevel.Error,
        Message = "Exception ParseSIPResponse from string ({SipMessageStr}).")]
    public static partial void LogParseSIPResponseStringException(
        this ILogger logger,
        string sipMessageStr,
        Exception exception);

    // SIPRequest logging
    [LoggerMessage(
        EventId = 0,
        EventName = "ParseSIPRequestBufferException",
        Level = LogLevel.Error,
        Message = "Exception parsing SIP Request from buffer ({RawMessage}).")]
    public static partial void LogParseSIPRequestBufferException(
        this ILogger logger,
        string rawMessage,
        Exception exception);

    // SIPClientWebSocketChannel logging
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPClientWebSocketCloseWarning",
        Level = LogLevel.Warning,
        Message = "Exception SIPClientWebSocketChannel Close.")]
    public static partial void LogSIPClientWebSocketCloseWarning(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPClientWebSocketProcessingReceiveTasksException",
        Level = LogLevel.Error,
        Message = "Exception SIPClientWebSocketChannel processing receive tasks.")]
    public static partial void LogSIPClientWebSocketProcessingReceiveTasksException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPClientWebSocketMonitorReceiveTasksException",
        Level = LogLevel.Error,
        Message = "Exception SIPClientWebSocketChannel.MonitorReceiveTasks.")]
    public static partial void LogSIPClientWebSocketMonitorReceiveTasksException(
        this ILogger logger,
        Exception exception);

    // SIPWebSocketChannel logging (note: uses 'Logger' not 'logger')
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPWebSocketCloseWarning",
        Level = LogLevel.Warning,
        Message = "Exception SIPWebSocketChannel Close.")]
    public static partial void LogSIPWebSocketCloseWarning(
        this ILogger logger,
        Exception exception);

    // SIPTCPChannel logging
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTCPProcessStreamReceiveException",
        Level = LogLevel.Error,
        Message = "Exception processing SIP {ProtDescr} stream receive from {RemoteEndPoint}.")]
    public static partial void LogSIPTCPProcessStreamReceiveException(
        this ILogger logger,
        string protDescr,
        EndPoint remoteEndPoint,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTCPConnectClientAsyncException",
        Level = LogLevel.Error,
        Message = "Exception ConnectClientAsync.")]
    public static partial void LogSIPTCPConnectClientAsyncException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTCPChannelSendException",
        Level = LogLevel.Error,
        Message = "Exception SIPTCPChannel Send to {DstSIPEndPoint}.")]
    public static partial void LogSIPTCPChannelSendException(
        this ILogger logger,
        SIPEndPoint dstSIPEndPoint,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTCPOnSIPStreamDisconnectedException",
        Level = LogLevel.Error,
        Message = "Exception OnSIPStreamDisconnected.")]
    public static partial void LogSIPTCPOnSIPStreamDisconnectedException(
        this ILogger logger,
        Exception exception);

    // SIPUDPChannel logging
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUDPChannelReceiveException",
        Level = LogLevel.Error,
        Message = "Exception SIPUDPChannel.Receive.")]
    public static partial void LogSIPUDPChannelReceiveException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUDPChannelEndReceiveFromException",
        Level = LogLevel.Error,
        Message = "Exception SIPUDPChannel EndReceiveFrom.")]
    public static partial void LogSIPUDPChannelEndReceiveFromException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUDPChannelSendAsyncException",
        Level = LogLevel.Error,
        Message = "Exception SIPUDPChannel.SendAsync.")]
    public static partial void LogSIPUDPChannelSendAsyncException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUDPChannelEndSendToException",
        Level = LogLevel.Error,
        Message = "Exception SIPUDPChannel EndSendTo.")]
    public static partial void LogSIPUDPChannelEndSendToException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUDPChannelCloseWarning",
        Level = LogLevel.Warning,
        Message = "Exception SIPUDPChannel Close.")]
    public static partial void LogSIPUDPChannelCloseWarning(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUDPChannelExpireFailedSendsException",
        Level = LogLevel.Error,
        Message = "Exception SIPUDPChannel.ExpireFailedSends.")]
    public static partial void LogSIPUDPChannelExpireFailedSendsException(
        this ILogger logger,
        Exception exception);

    // SIPTLSChannel logging
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPTLSOnReadCallbackWarning",
        Level = LogLevel.Warning,
        Message = "Exception SIPTLSChannel OnReadCallback.")]
    public static partial void LogSIPTLSOnReadCallbackWarning(
        this ILogger logger,
        Exception exception);

    // SIPRequest logging (additional)
    [LoggerMessage(
        EventId = 0,
        EventName = "SIPRequestToStringException",
        Level = LogLevel.Error,
        Message = "Exception SIPRequest ToString.")]
    public static partial void LogSIPRequestToStringException(
        this ILogger logger,
        Exception exception);

    // UdpReceiver:146 - BeginReceiveFrom socket error
    [LoggerMessage(
        EventId = 0,
        EventName = "UdpReceiverBeginReceiveFromSocketError",
        Level = LogLevel.Warning,
        Message = "Socket error {SocketErrorCode} in UdpReceiver.BeginReceiveFrom. {Message}")]
    public static partial void LogUdpReceiverBeginReceiveFromSocketError(
        this ILogger logger,
        int socketErrorCode,
        string message);

    // UdpReceiver:238,246 - EndReceiveFrom socket error (both catches use same message)
    [LoggerMessage(
        EventId = 0,
        EventName = "UdpReceiverEndReceiveFromSocketError",
        Level = LogLevel.Warning,
        Message = "SocketException UdpReceiver.EndReceiveFrom ({SocketErrorCode}). {ErrorMessage}")]
    public static partial void LogUdpReceiverEndReceiveFromSocketError(
        this ILogger logger,
        int socketErrorCode,
        string errorMessage);

    [LoggerMessage(
        EventId = 0,
        EventName = "SIPUDPChannelExpireCancelled",
        Level = LogLevel.Debug,
        Message = "SIP UDP Channel expire failed sends loop cancelled.")]
    public static partial void LogSIPUDPChannelExpireCancelled(
        this ILogger logger);
}
