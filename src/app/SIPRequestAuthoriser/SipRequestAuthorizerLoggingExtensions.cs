using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP.App
{
    internal static partial class SipRequestAuthorizerLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "SipAccountDisabled",
            Level = LogLevel.Warning,
            Message = "SIP account {sipUsername}@{sipDomain} is disabled for {method}.")]
        public static partial void LogSipAccountDisabled(
            this ILogger logger,
            string sipUsername,
            string sipDomain,
            SIPMethodsEnum method);

        [LoggerMessage(
            EventId = 0,
            EventName = "AuthenticationStaleNonce",
            Level = LogLevel.Warning,
            Message = "Authentication failed stale nonce for realm={sipDomain}, username={sipUsername}, uri={uri}, nonce={nonce}, method={method}.")]
        public static partial void LogAuthenticationStaleNonce(
            this ILogger logger,
            string sipDomain,
            string sipUsername,
            string uri,
            string nonce,
            SIPMethodsEnum method);

        [LoggerMessage(
            EventId = 0,
            EventName = "AuthenticationTokenCheckFailed",
            Level = LogLevel.Warning,
            Message = "Authentication token check failed for realm={sipDomain}, username={sipUsername}, uri={uri}, nonce={requestNonce}, method={sipRequestMethod}.")]
        public static partial void LogAuthenticationTokenCheckFailed(
            this ILogger logger,
            string sipDomain,
            string sipUsername,
            string uri,
            string requestNonce,
            SIPMethodsEnum sipRequestMethod);

        [LoggerMessage(
            EventId = 0,
            EventName = "AuthoriseSipRequestError",
            Level = LogLevel.Error,
            Message = "Exception AuthoriseSIPRequest. {errorMessage}")]
        public static partial void LogAuthoriseSipRequestError(
            this ILogger logger,
            string errorMessage,
            Exception exception);
    }
}
