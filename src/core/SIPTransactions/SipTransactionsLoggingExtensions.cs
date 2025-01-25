using System;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    internal static partial class SipTransactionsLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "PrackRequestMatched",
            Level = LogLevel.Debug,
            Message = "PRACK request matched the current outstanding provisional response, setting as delivered.")]
        public static partial void LogPrackRequestMatched(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "PrackRetransmitMissing",
            Level = LogLevel.Warning,
            Message = "A PRACK retransmit was required but there was no stored PRACK request to send.")]
        public static partial void LogPrackRetransmitMissing(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "PendingTransactionsHeader",
            Level = LogLevel.Debug,
            Message = "=== Pending Transactions ===")]
        public static partial void LogPendingTransactionsHeader(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "PendingTransaction",
            Level = LogLevel.Debug,
            Message = "Pending transaction {TransactionRequestMethod} {TransactionState} {TransactionCreationTotalSeconds:0.##}s {TransactionRequestURI} ({TransactionId})",
            SkipEnabledCheck = true)]
        public static partial void LogPendingTransactionUnchecked(
            this ILogger logger,
            SIPMethodsEnum transactionRequestMethod,
            SIPTransactionStatesEnum transactionState,
            double transactionCreationTotalSeconds,
            SIPURI transactionRequestURI,
            string transactionId);
        public static void LogPendingTransaction(
            this ILogger logger,
            SIPTransaction transaction,
            DateTime now)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogPendingTransactionUnchecked(transaction.TransactionRequest.Method, transaction.TransactionState, now.Subtract(transaction.Created).TotalSeconds, transaction.TransactionRequestURI, transaction.TransactionId);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "TransactionSendFailed",
            Level = LogLevel.Warning,
            Message = "SIP transaction send failed in state {TransactionState} with error {SendResult}")]
        public static partial void LogTransactionSendFailed(
            this ILogger logger,
            SIPTransactionStatesEnum TransactionState,
            SocketError SendResult);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionProcessPendingTransactions",
            Level = LogLevel.Error,
            Message = "Exception processing pending transactions. {ErrorMessage}")]
        public static partial void LogExceptionProcessPendingTransactions(
            this ILogger logger,
            string ErrorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionSipTransaction",
            Level = LogLevel.Error,
            Message = "Exception SIPTransaction.{ErrorMessage}")]
        public static partial void LogExceptionSipTransaction(
            this ILogger logger,
            string ErrorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnexpectedResponse",
            Level = LogLevel.Warning,
            Message = "UASInviteTransaction received unexpected response, {ReasonPhrase} from {RemoteEndPoint}, ignoring.")]
        public static partial void LogUnexpectedResponse(
            this ILogger logger,
            string ReasonPhrase,
            string RemoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "CancelTransactionStateError",
            Level = LogLevel.Warning,
            Message = "A request was made to cancel transaction {TransactionId} that was not in the calling, trying or proceeding states, state={TransactionState}.")]
        public static partial void LogCancelTransactionStateError(
            this ILogger logger,
            string TransactionId,
            SIPTransactionStatesEnum TransactionState);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnexpectedRequest",
            Level = LogLevel.Warning,
            Message = "UACInviteTransaction received unexpected request, {Method} from {RemoteEndPoint}, ignoring.")]
        public static partial void LogUnexpectedRequest(
            this ILogger logger,
            SIPMethodsEnum Method,
            SIPEndPoint RemoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionInformationResponseReceived",
            Level = LogLevel.Error,
            Message = "Exception UACInviteTransaction_TransactionInformationResponseReceived. {ErrorMessage}")]
        public static partial void LogExceptionInformationResponseReceived(
            this ILogger logger,
            string ErrorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionFinalResponseReceived",
            Level = LogLevel.Error,
            Message = "Exception UACInviteTransaction_TransactionFinalResponseReceived. {ErrorMessage}")]
        public static partial void LogExceptionFinalResponseReceived(
            this ILogger logger,
            string ErrorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnexpectedInviteServerState",
            Level = LogLevel.Warning,
            Message = "InviteServer Transaction entered an unexpected transaction state {TransactionState}.")]
        public static partial void LogUnexpectedInviteServerState(
            this ILogger logger,
            SIPTransactionStatesEnum TransactionState);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnexpectedNonInviteState",
            Level = LogLevel.Warning,
            Message = "NonInvite Transaction entered an unexpected transaction state {TransactionState}.")]
        public static partial void LogUnexpectedNonInviteState(
            this ILogger logger,
            SIPTransactionStatesEnum TransactionState);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnrecognisedTransactionType",
            Level = LogLevel.Warning,
            Message = "Unrecognised transaction type {TransactionType}.")]
        public static partial void LogUnrecognisedTransactionType(
            this ILogger logger,
            SIPTransactionTypesEnum TransactionType);

        [LoggerMessage(
            EventId = 0,
            EventName = "PendingProvisionalResponseNotAcked",
            Level = LogLevel.Warning,
            Message = "A new reliable provisional response is being sent but the previous one was not yet acknowledged.")]
        public static partial void LogPendingProvisionalResponseNotAcked(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "AckRetransmitRequestMissing",
            Level = LogLevel.Warning,
            Message = "An ACK retransmit was required but there was no stored ACK request to send.")]
        public static partial void LogAckRetransmitRequestMissing(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionGetInformationalResponse",
            Level = LogLevel.Error,
            Message = "Exception GetInformationalResponse. {ErrorMessage}")]
        public static partial void LogExceptionGetInformationalResponse(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionProcessingPendingTransactions",
            Level = LogLevel.Error,
            Message = "Exception processing pending transactions. {ErrorMessage}")]
        public static partial void LogExceptionProcessingPendingTransactions(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionRemoveExpiredTransaction",
            Level = LogLevel.Error,
            Message = "Exception RemoveExpiredTransaction. {ErrorMessage}")]
        public static partial void LogExceptionRemoveExpiredTransaction(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionResendAckRequest",
            Level = LogLevel.Error,
            Message = "Exception ResendAckRequest: {ErrorMessage}")]
        public static partial void LogExceptionResendAckRequest(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionResendPrackRequest",
            Level = LogLevel.Error,
            Message = "Exception ResendPrackRequest: {ErrorMessage}")]
        public static partial void LogExceptionResendPrackRequest(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ExceptionProcessingPendingTransactionsMain",
            Level = LogLevel.Error,
            Message = "Exception processing pending transactions. {ErrorMessage}")]
        public static partial void LogExceptionProcessingPendingTransactionsMain(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TransactionTraceEventNonInvite",
            Level = LogLevel.Debug,
            Message = "Non-INVITE transaction created, state={State}, method={Method}, uri={Uri}, localTag={LocalTag}, remoteTag={RemoteTag}.")]
        public static partial void LogTransactionTraceEventNonInvite(
            this ILogger logger,
            SIPTransactionStatesEnum state,
            SIPMethodsEnum method,
            SIPURI uri,
            string localTag,
            string remoteTag);

        [LoggerMessage(
            EventId = 0,
            EventName = "TransactionTraceEvent",
            Level = LogLevel.Debug,
            Message = "INVITE transaction created, state={State}, uri={Uri}, localTag={LocalTag}, remoteTag={RemoteTag}.")]
        public static partial void LogTransactionTraceEvent(
            this ILogger logger,
            SIPTransactionStatesEnum state,
            SIPURI uri,
            string localTag,
            string remoteTag);
    }
}
