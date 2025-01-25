using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    internal static partial class SipLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "SIPTransportQueueFull",
            Level = LogLevel.Warning,
            Message = "SIPTransport queue full new message from {RemoteEndPoint} being discarded.")]
        public static partial void LogSIPTransportQueueFull(
            this ILogger logger,
            SIPEndPoint remoteEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPTransportShutdownError",
            Level = LogLevel.Error,
            Message = "Exception SIPTransport Shutdown. {ErrorMessage}")]
        private static partial void LogSIPTransportShutdownErrorImpl(
            this ILogger logger,
            Exception excp,
            string errorMessage);

        public static void LogSIPTransportShutdownError(

            this ILogger logger,
            Exception excp)
        {
            LogSIPTransportShutdownErrorImpl(logger, excp, excp.Message);
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPTransportReceiveMessage",
            Level = LogLevel.Error,
            Message = "Exception SIPTransport ReceiveMessage. {ErrorMessage}")]
        private static partial void LogSIPTransportReceiveMessageImpl(
            this ILogger logger,
            Exception excp,
            string errorMessage);

        public static void LogSIPTransportReceiveMessage(

            this ILogger logger,
            Exception excp)
        {
            LogSIPTransportReceiveMessageImpl(logger, excp, excp.Message);
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPTransportProcessReceiveQueue",
            Level = LogLevel.Error,
            Message = "Exception SIPTransport ProcessReceiveQueue. {ErrorMessage}")]
        private static partial void LogSIPTransportProcessReceiveQueueImpl(
            this ILogger logger,
            Exception excp,
            string errorMessage);

        public static void LogSIPTransportProcessReceiveQueue(

            this ILogger logger,
            Exception excp)
        {
            LogSIPTransportProcessReceiveQueueImpl(logger, excp, excp.Message);
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPMessageReceived",
            Level = LogLevel.Error,
            Message = "Exception SIPMessageReceived. {ErrorMessage}")]
        private static partial void LogSIPMessageReceivedImpl(
            this ILogger logger,
            Exception excp,
            string errorMessage);

        public static void LogSIPMessageReceived(

            this ILogger logger,
            Exception excp)
        {
            LogSIPMessageReceivedImpl(logger, excp, excp.Message);
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPRequestIn",
            Level = LogLevel.Debug,
            Message = "SIP request received: {LocalEndPoint}<-{RemoteEndPoint} {StatusLine}")]
        public static partial void LogSIPRequestIn(
            this ILogger logger,
            SIPEndPoint localEndPoint,
            SIPEndPoint remoteEndPoint,
            string statusLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPRequestInRequest",
            Level = LogLevel.Trace,
            Message = "Request: {Request}")]
        public static partial void LogSIPRequestInRequest(
            this ILogger logger,
            SIPRequest request);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPRequestOut",
            Level = LogLevel.Debug,
            Message = "SIP request sent: {LocalEndPoint}->{RemoteEndPoint} {StatusLine}")]
        public static partial void LogSIPRequestOut(
            this ILogger logger,
            SIPEndPoint localEndPoint,
            SIPEndPoint remoteEndPoint,
            string statusLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPRequestOutRequest",
            Level = LogLevel.Trace,
            Message = "Request sent: {Request}")]
        public static partial void LogSIPRequestOutRequest(
            this ILogger logger,
            SIPRequest request);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPResponseIn",
            Level = LogLevel.Debug,
            Message = "SIP response received: {LocalEndPoint}<-{RemoteEndPoint} {ShortDescription}")]
        public static partial void LogSIPResponseIn(
            this ILogger logger,
            SIPEndPoint localEndPoint,
            SIPEndPoint remoteEndPoint,
            string shortDescription);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPResponseInRequest",
            Level = LogLevel.Trace,
            Message = "Response received: {Response}")]
        public static partial void LogSIPResponseInRequest(
            this ILogger logger,
            SIPResponse response);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPResponseOut",
            Level = LogLevel.Debug,
            Message = "SIP response sent: {LocalEndPoint}->{RemoteEndPoint} {ShortDescription}")]
        public static partial void LogSIPResponseOut(
            this ILogger logger,
            SIPEndPoint localEndPoint,
            SIPEndPoint remoteEndPoint,
            string shortDescription);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPResponseOutRequest",
            Level = LogLevel.Trace,
            Message = "Response sent: {Response}")]
        public static partial void LogSIPResponseOutRequest(
            this ILogger logger,
            SIPResponse response);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPRequestRetransmit",
            Level = LogLevel.Debug,
            Message = "SIP request retransmit {Count} for request {StatusLine}, initial transmit {InitialTransmit}s ago.")]
        public static partial void LogSIPRequestRetransmit(
            this ILogger logger,
            int count,
            string statusLine,
            double initialTransmit);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPRequestRetransmitRequest",
            Level = LogLevel.Trace,
            Message = "Request retransmitted: {Response}")]
        public static partial void LogSIPRequestRetransmitRequest(
            this ILogger logger,
            SIPRequest response);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPResponseRetransmit",
            Level = LogLevel.Debug,
            Message = "SIP response retransmit {Count} for response {ShortDescription}, initial transmit {InitialTransmit}s ago.")]
        public static partial void LogSIPResponseRetransmit(
            this ILogger logger,
            int count,
            string shortDescription,
            double initialTransmit);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPResponseRetransmitResponse",
            Level = LogLevel.Trace,
            Message = "Response retransmitted: {Response}")]
        public static partial void LogSIPResponseRetransmitRequest(
            this ILogger logger,
            SIPResponse response);

        [LoggerMessage(
            EventId = 0,
            EventName = "DigestAlgorithm",
            Level = LogLevel.Warning,
            Message = "SIPAuthorisationDigest did not recognised digest algorithm value of {algorithm}, defaulting to {defaultAlgorithm}.")]
        public static partial void LogDigestAlgorithm(
            this ILogger logger,
            string algorithm,
            DigestAlgorithmsEnum defaultAlgorithm);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnknownSIPMethod",
            Level = LogLevel.Warning,
            Message = "Unknown SIP method received {UnknownMethod}.")]
        public static partial void LogUnknownSIPMethod(
            this ILogger logger,
            string unknownMethod);

        [LoggerMessage(
            EventId = 0,
            EventName = "ParseSIPRequestError",
            Level = LogLevel.Error,
            Message = "Exception parsing SIP Request: {SipMessage}. {ErrorMessage}")]
        public static partial void LogParseSIPRequestError(
            this ILogger logger,
            string sipMessage,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPParameterlessURIError",
            Level = LogLevel.Error,
            Message = "Exception SIPParameterlessURI ToString. {ErrorMessage}")]
        public static partial void LogSIPParameterlessURIError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "GetKeyValuePairsError",
            Level = LogLevel.Error,
            Message = "Exception GetKeyValuePairsFromQuoted. {ErrorMessage}")]
        public static partial void LogGetKeyValuePairsError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPRequestParseError",
            Level = LogLevel.Error,
            Message = "Exception parsing SIP Request: {SipMessage}. {ErrorMessage}")]
        public static partial void LogSIPRequestParseError(
            this ILogger logger,
            string sipMessage,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ParseSIPRequestError",
            Level = LogLevel.Error,
            Message = "Exception ParseSIPRequest: {SipMessage}. {ErrorMessage}")]
        public static partial void LogParseSIPRequestError(
            this ILogger logger,
            string sipMessage,
            string errorMessage);

        [LoggerMessage(
            EventId = 0,
            EventName = "ParseSipRequestError", 
            Level = LogLevel.Error,
            Message = "Exception ParseSIPRequest: {SipMessage}. {ErrorMessage}")]
        public static partial void LogParseSipRequestError(
            this ILogger logger,
            string sipMessage,
            string errorMessage);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPRequestToStringError",
            Level = LogLevel.Error,
            Message = "Exception SIPRequest ToString. {ErrorMessage}")]
        public static partial void LogSIPRequestToStringError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "MessageParseError",
            Level = LogLevel.Warning,
            Message = "Error ParseSIPMessage, no end of line character found for the first line.")]
        public static partial void LogMessageParseError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "MissingSemiColon",
            Level = LogLevel.Warning,
            Message = "Via header missing semi-colon: {header}")]
        public static partial void LogMissingSemiColon(
            this ILogger logger,
            string header);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidSIPHeader", 
            Level = LogLevel.Warning,
            Message = "Invalid SIP header, ignoring {headerLine}.")]
        public static partial void LogInvalidSIPHeader(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPHeaderToString",
            Level = LogLevel.Error,
            Message = "Exception SIPHeader ToString. {ErrorMessage}")]
        public static partial void LogSIPHeaderToString(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidViaHeader",
            Level = LogLevel.Warning,
            Message = "Via header missing branch: {header}.")]
        public static partial void LogInvalidViaHeader(
            this ILogger logger,
            string header);

        [LoggerMessage(
            EventId = 0,
            EventName = "HangupError",
            Level = LogLevel.Error,
            Message = "Exception SIPDialogue Hangup. {ErrorMessage}")]
        public static partial void LogHangupError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "EmptyCSeq",
            Level = LogLevel.Warning,
            Message = "The " + SIPHeaders.SIP_HEADER_CSEQ + " was empty.")]
        public static partial void LogEmptyCSeq(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidCSeq",
            Level = LogLevel.Warning,
            Message = SIPHeaders.SIP_HEADER_RELIABLE_ACK + " did not contain a valid integer for the CSeq being acknowledged, {HeaderLine}")]
        public static partial void LogInvalidCSeq(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "EmptyRackMethod",
            Level = LogLevel.Warning,
            Message = "There was no " + SIPHeaders.SIP_HEADER_RELIABLE_ACK + " method, {HeaderLine}")]
        public static partial void LogEmptyRackMethod(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidRSeqValue",
            Level = LogLevel.Warning,
            Message = "The Rseq value was not a valid integer. {HeaderLine}.")]
        public static partial void LogInvalidRSeqValue(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "HistoryInfoSortError",
            Level = LogLevel.Warning,
            Message = "could not sort History-Info header")]
        public static partial void LogHistoryInfoSortError(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "HeaderParseError",
            Level = LogLevel.Error,
            Message = "Error parsing SIP header {HeaderLine}. {ErrorMessage}.")]
        public static partial void LogHeaderParseError(
            this ILogger logger,
            string headerLine,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ParseSIPHeadersError",
            Level = LogLevel.Error,
            Message = "Exception ParseSIPHeaders. {ErrorMessage}")]
        public static partial void LogParseSIPHeadersError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidCSeqInteger",
            Level = LogLevel.Warning,
            Message = SIPHeaders.SIP_HEADER_CSEQ + " did not contain a valid integer, {HeaderLine}.")]
        public static partial void LogInvalidCSeqInteger(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "MissingCSeqMethod",
            Level = LogLevel.Warning,
            Message = "There was no " + SIPHeaders.SIP_HEADER_CSEQ + " method, {HeaderLine}.")]
        public static partial void LogMissingCSeqMethod(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidExpiresInteger",
            Level = LogLevel.Warning,
            Message = "The Expires value was not a valid integer. {HeaderLine}")]
        public static partial void LogInvalidExpiresInteger(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidMinExpiresInteger",
            Level = LogLevel.Warning,
            Message = "The Min-Expires value was not a valid integer. {HeaderLine}")]
        public static partial void LogInvalidMinExpiresInteger(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidMaxForwardsInteger",
            Level = LogLevel.Warning,
            Message = "The " + SIPHeaders.SIP_HEADER_MAXFORWARDS + " could not be parsed as a valid integer, {HeaderLine}")]
        public static partial void LogInvalidMaxForwardsInteger(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidContentLengthInteger",
            Level = LogLevel.Warning,
            Message = "The " + SIPHeaders.SIP_HEADER_CONTENTLENGTH + " could not be parsed as a valid integer.")]
        public static partial void LogInvalidContentLengthInteger(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidUnknownHeader",
            Level = LogLevel.Warning,
            Message = "Invalid SIP header, ignoring {UnknownHeader}")]
        public static partial void LogInvalidUnknownHeader(
            this ILogger logger,
            string unknownHeader);

        [LoggerMessage(
            EventId = 0,
            EventName = "EmptyRAck",
            Level = LogLevel.Warning,
            Message = "The " + SIPHeaders.SIP_HEADER_RELIABLE_ACK + " was empty.")]
        public static partial void LogEmptyRAck(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "InvalidRSeqInteger",
            Level = LogLevel.Warning,
            Message = SIPHeaders.SIP_HEADER_RELIABLE_ACK + " did not contain a valid integer for the RSeq being acknowledged, {HeaderLine}")]
        public static partial void LogInvalidRSeqInteger(
            this ILogger logger,
            string headerLine);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnrecognizedDigestAlgorithm",
            Level = LogLevel.Warning,
            Message = "SIPAuthorisationDigest did not recognised digest algorithm value of {algorithm}, defaulting to {defaultAlgorithm}")]
        public static partial void LogUnrecognizedDigestAlgorithm(
            this ILogger logger, 
            string algorithm,
            DigestAlgorithmsEnum defaultAlgorithm);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnknownMethodReceived", 
            Level = LogLevel.Warning,
            Message = "Unknown SIP method received {UnknownMethod}")]
        public static partial void LogUnknownMethodReceived(
            this ILogger logger,
            string unknownMethod);

        [LoggerMessage(
            EventId = 0,
            EventName = "ParseSIPResponseError",  
            Level = LogLevel.Error,
            Message = "Exception ParseSIPResponse: {SipMessage}. {ErrorMessage}")]
        public static partial void LogParseSIPResponseError(
            this ILogger logger,
            string sipMessage,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ToParameterlessStringError",
            Level = LogLevel.Error,
            Message = "Exception SIPURI ToParamaterlessString. {ErrorMessage}")]
        public static partial void LogToParameterlessStringError(
            this ILogger logger,
            string errorMessage,
            Exception exception); 

        [LoggerMessage(
            EventId = 0,
            EventName = "ParseUserParametersError",
            Level = LogLevel.Warning,
            Message = "Failed to parse UserParameters, error: {ErrorMessage}")]
        public static partial void LogParseUserParametersError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPURIToStringError",
            Level = LogLevel.Error,
            Message = "Exception SIPURI ToString. {ErrorMessage}")]
        public static partial void LogSIPURIToStringError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "GetKeyValuePairsQuotedError",
            Level = LogLevel.Error,
            Message = "Exception GetKeyValuePairsFromQuoted. {ErrorMessage}")]
        public static partial void LogGetKeyValuePairsQuotedError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "ParseSIPURIError",
            Level = LogLevel.Error,
            Message = "Exception ParseSIPURI (URI={Uri}). {ErrorMessage}")]
        public static partial void LogParseSIPURIError(
            this ILogger logger,
            string uri,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPResponseGetError",
            Level = LogLevel.Error,
            Message = "Exception SIPResponse.GetResponse. {ErrorMessage}")]
        public static partial void LogSIPResponseGetError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ParseRawSIPRequestError",
            Level = LogLevel.Error,
            Message = "Exception parsing SIP Request: {SipMessage}. {ErrorMessage}")]
        public static partial void LogParseRawSIPRequestError(
            this ILogger logger,
            string sipMessage,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "UserParametersParseError",
            Level = LogLevel.Warning,
            Message = "Failed to parse UserParameters, error: {ErrorMessage}")]
        public static partial void LogUserParametersParseError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "UserParametersParseWarning",
            Level = LogLevel.Warning, 
            Message = "Failed to parse UserParameters, error: {ErrorMessage}")]
        public static partial void LogUserParametersParseWarning(
            this ILogger logger,
            string ErrorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "TransactionInStatelessMode",
            Level = LogLevel.Warning, 
            Message = "The SIP transport was requested to add a transaction in stateless mode (noop).")]
        public static partial void LogTransactionInStatelessModeWarning(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "ChannelNotFound",
            Level = LogLevel.Warning, 
            Message = "An existing SIP channel could not be found to send response {ShortDescription}.")]
        public static partial void LogChannelNotFoundWarning(
            this ILogger logger,
            string ShortDescription);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnknownChannelTransport",
            Level = LogLevel.Warning, 
            Message = "Don't know how to create SIP channel for transport {protocol}.")]
        public static partial void LogUnknownChannelTransportWarning(
            this ILogger logger,
            string protocol);

        [LoggerMessage(
            EventId = 0,
            EventName = "NoViaHeader",
            Level = LogLevel.Warning, 
            Message = "There was no top Via header on a SIP response from {RemoteSIPEndPoint} in SendResponseAsync, response dropped.")]
        public static partial void LogNoViaHeaderWarning(
            this ILogger logger,
            SIPEndPoint RemoteSIPEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "ResendingFinalResponse",
            Level = LogLevel.Warning, 
            Message = "Resending final response for {Method}, {URI}, cseq={CSeq}.")]
        public static partial void LogResendingFinalResponseWarning(
            this ILogger logger,
            SIPMethodsEnum Method,
            SIPURI URI,
            int CSeq);

        [LoggerMessage(
            EventId = 0, 
            EventName = "TransactionExists",
            Level = LogLevel.Warning, 
            Message = "Transaction already exists, ignoring duplicate request, {Method} {URI}.")]
        public static partial void LogTransactionExistsWarning(
            this ILogger logger,
            SIPMethodsEnum Method,
            string URI);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SipUserFieldToString",
            Level = LogLevel.Error, 
            Message = "Exception SIPUserField ToString. {Message}")]
        public static partial void LogSIPUserFieldToStringError(
            this ILogger logger,
            string Message,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "SipUserFieldToParameterlessString",
            Level = LogLevel.Error, 
            Message = "Exception SIPUserField ToParameterlessString. {Message}")]
        public static partial void LogSIPUserFieldToParameterlessStringError(
            this ILogger logger,
            string Message,
            Exception exception);

        [LoggerMessage(
            EventId = 0, 
            EventName = "AddSipChannelError",
            Level = LogLevel.Error, 
            Message = "Exception AddSIPChannel. {ErrorMessage}")]
        public static partial void LogAddSIPChannelError(
            this ILogger logger,
            string ErrorMessage,
            Exception exception);
    }
}
