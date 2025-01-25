using System;
using System.Net;
using DnsClient;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.Net
{
    internal static partial class NetStunLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerCreated",
            Level = LogLevel.Information,
            Message = "STUNListener created {Address}:{Port}.")]
        public static partial void LogStunListenerCreated(
            this ILogger logger,
            IPAddress address,
            int port);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunDeterminingPublicIP",
            Level = LogLevel.Debug,
            Message = "STUNClient attempting to determine public IP from {StunServer}.")]
        public static partial void LogStunDeterminingPublicIP(
            this ILogger logger,
            string stunServer);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunInitialResponse",
            Level = LogLevel.Debug,
            Message = "STUNClient Response to initial STUN message received from {ResponseEndPoint}.")]
        public static partial void LogStunInitialResponse(
            this ILogger logger,
            IPEndPoint responseEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunPublicIPResult",
            Level = LogLevel.Debug,
            Message = "STUNClient Public IP={PublicAddress} Port={PublicPort}.")]
        public static partial void LogStunPublicIPResult(
            this ILogger logger,
            IPAddress publicAddress,
            int publicPort);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerClosing",
            Level = LogLevel.Debug,
            Message = "Closing STUNListener.")]
        public static partial void LogStunListenerClosing(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerAdditionalSockets",
            Level = LogLevel.Debug,
            Message = "STUN Server additional sockets, primary={PrimaryEndPoint}, secondary={SecondaryEndPoint}.")]
        public static partial void LogStunServerAdditionalSockets(
            this ILogger logger,
            string primaryEndPoint,
            string secondaryEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerPrimaryException",
            Level = LogLevel.Debug,
            Message = "Exception STUNPrimaryReceived. {ErrorMessage}")]
        public static partial void LogStunServerPrimaryException(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerSecondaryException",
            Level = LogLevel.Debug,
            Message = "Exception STUNSecondaryReceived. {ErrorMessage}")]
        public static partial void LogStunServerSecondaryException(
            this ILogger logger,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunDnsSrvLookupFailure",
            Level = LogLevel.Debug,
            Message = "STUNDns SRV lookup failure for {Uri}. {ErrorMessage}")]
        public static partial void LogStunDnsSrvLookupFailure(
            this ILogger logger,
            STUNUri uri,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunDnsOsLookupFailed",
            Level = LogLevel.Warning,
            Message = "Operating System DNS lookup failed for {host}.")]
        public static partial void LogStunDnsOsLookupFailed(
            this ILogger logger,
            string host);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunDnsLookupFailure",
            Level = LogLevel.Warning,
            Message = "STUNDns lookup failure for {host} and query {queryType}. {errorMessage}")]
        public static partial void LogStunDnsLookupFailure(
            this ILogger logger,
            string host,
            QueryType queryType,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunClientReceiveError",
            Level = LogLevel.Warning,
            Message = "Exception STUNClient Receive. {errorMessage}")]
        public static partial void LogStunClientReceiveError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunClientTimeout",
            Level = LogLevel.Warning,
            Message = "STUNClient server response timed out after {timeout}s.")]
        public static partial void LogStunClientTimeout(
            this ILogger logger,
            int timeout);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunClientGetPublicIPError",
            Level = LogLevel.Error,
            Message = "Exception STUNClient GetPublicIPAddress. {errorMessage}")]
        public static partial void LogStunClientGetPublicIPError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerSendNotAccessible",
            Level = LogLevel.Warning,
            Message = "The STUNListener was not accessible when attempting to send a message to {destinationEndPoint}.",
            SkipEnabledCheck = true)]
        private static partial void LogStunListenerSendNotAccessibleUnchecked(
            this ILogger logger,
            string destinationEndPoint);

        public static void LogStunListenerSendNotAccessible(
            this ILogger logger,
            IPEndPoint destinationEndPoint)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogStunListenerSendNotAccessibleUnchecked(IPSocket.GetSocketString(destinationEndPoint));
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerCloseError",
            Level = LogLevel.Warning,
            Message = "Exception STUNListener Close. {errorMessage}")]
        public static partial void LogStunListenerCloseError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerReadError",
            Level = LogLevel.Error,
            Message = "Unable to read from STUNListener local end point {address}:{port}")]
        public static partial void LogStunListenerReadError(
            this ILogger logger,
            IPAddress address,
            int port);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerProcessError",
            Level = LogLevel.Error,
            Message = "Exception processing STUNListener MessageReceived. {errorMessage}")]
        public static partial void LogStunListenerProcessError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerEmptyDestination",
            Level = LogLevel.Error,
            Message = "An empty destination was specified to Send in STUNListener.")]
        public static partial void LogStunListenerEmptyDestination(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerSendError",
            Level = LogLevel.Error,
            Message = "Exception ({exceptionType}) STUNListener Send (sendto=>{destinationEndPoint}). {errorMessage}",
            SkipEnabledCheck = true)]
        private static partial void LogStunListenerSendErrorUnchecked(
            this ILogger logger,
            string exceptionType,
            string destinationEndPoint,
            string errorMessage,
            Exception exception);

        public static void LogStunListenerSendError(
            this ILogger logger,
            IPEndPoint destinationEndPoint,
            Exception exception)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogStunListenerSendErrorUnchecked(exception.GetType().ToString(), IPSocket.GetSocketString(destinationEndPoint), exception.Message, exception);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerConstructor",
            Level = LogLevel.Error,
            Message = "Exception STUNListener (ctor). {errorMessage}")]
        public static partial void LogStunListenerConstructor(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerDispose",
            Level = LogLevel.Error,
            Message = "Exception Disposing STUNListener. {errorMessage}")]
        public static partial void LogStunListenerDispose(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerInitSockets",
            Level = LogLevel.Error,
            Message = "Exception STUNListener InitialiseSockets. {errorMessage}")]
        public static partial void LogStunListenerInitSockets(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerListen",
            Level = LogLevel.Error,
            Message = "Exception STUNListener Listen. {errorMessage}")]
        public static partial void LogStunListenerListen(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunListenerListening",
            Level = LogLevel.Error,
            Message = "Exception listening in STUNListener. {errorMessage}")]
        public static partial void LogStunListenerListening(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerStop",
            Level = LogLevel.Error,
            Message = "Exception StunServer Stop. {errorMessage}")]
        public static partial void LogStunServerStop(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerFirePrimaryRequest",
            Level = LogLevel.Error,
            Message = "Exception FireSTUNPrimaryRequestInTraceEvent. {errorMessage}")]
        public static partial void LogStunServerFirePrimaryRequest(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerFireSecondaryRequest",
            Level = LogLevel.Error,
            Message = "Exception FireSTUNSecondaryRequestInTraceEvent. {errorMessage}")]
        public static partial void LogStunServerFireSecondaryRequest(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerFirePrimaryResponse",
            Level = LogLevel.Error,
            Message = "Exception FireSTUNPrimaryResponseOutTraceEvent. {errorMessage}")]
        public static partial void LogStunServerFirePrimaryResponse(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunServerFireSecondaryResponse",
            Level = LogLevel.Error,
            Message = "Exception FireSTUNSecondaryResponseOutTraceEvent. {errorMessage}")]
        public static partial void LogStunServerFireSecondaryResponse(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "StunAttributeLength",
            Level = LogLevel.Warning,
            Message = "The attribute length on a STUN parameter was greater than the available number of bytes. Type: {attributeType}")]
        public static partial void LogStunAttributeLength(
            this ILogger logger,
            STUNAttributeTypesEnum attributeType);

        [LoggerMessage(
            EventId = 0, 
            EventName = "StunMessageReceived", 
            Level = LogLevel.Debug, 
            Message = "STUN message received from {RemoteEndPoint}.")]
        public static partial void LogStunMessageReceived(
            this ILogger logger,
            string remoteEndPoint);

        [LoggerMessage(
            EventId = 0, 
            EventName = "StunMessageInvalid", 
            Level = LogLevel.Warning, 
            Message = "Invalid STUN message received from {RemoteEndPoint}.")]
        public static partial void LogStunMessageInvalid(
            this ILogger logger,
            string remoteEndPoint);
    }
}
