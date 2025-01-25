using System;
using System.Net;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP.App
{
    internal static partial class AppLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "SDPMangledForRequest",
            Level = LogLevel.Debug,
            Message = "SDP mangled for {Status} response from {RemoteSIPEndPoint}, adjusted address {RemoteEndPointAddress}.")]
        public static partial void LogSdpMangledRequest(
            this ILogger logger,
            SIPMethodsEnum status,
            SIPEndPoint remoteSipEndPoint,
            string remoteEndPointAddress);

        [LoggerMessage(
            EventId = 0,
            EventName = "SDPMangledForResponse",
            Level = LogLevel.Debug,
            Message = "SDP mangled for {Status} response from {RemoteSIPEndPoint}, adjusted address {RemoteEndPointAddress}.")]
        public static partial void LogSdpMangledResponse(
            this ILogger logger,
            SIPResponseStatusCodesEnum status,
            SIPEndPoint remoteSipEndPoint,
            IPAddress remoteEndPointAddress);

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpMangleEmptyValues",
            Level = LogLevel.Warning,
            Message = "Mangle SDP was called with an empty body or public IP address.")]
        public static partial void LogSdpMangleEmptyValues(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SdpMangleException",
            Level = LogLevel.Error,
            Message = "Exception MangleSDP. {errorMessage}")]
        public static partial void LogSdpMangleError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SipRequestMangleException",
            Level = LogLevel.Error,
            Message = "Exception MangleSDP. {errorMessage}")]
        public static partial void LogSipRequestMangleError(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "SipResponseMangleException",
            Level = LogLevel.Error,
            Message = "Exception MangleSIPResponse. {errorMessage}")]
        public static partial void LogSipResponseMangleError(
            this ILogger logger,
            string errorMessage,
            Exception exception);
    }
}
