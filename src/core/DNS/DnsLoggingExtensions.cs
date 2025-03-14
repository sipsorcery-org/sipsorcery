using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    internal static partial class DnsLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "DnsSrvResolve",
            Level = LogLevel.Debug,
            Message = "SIP DNS SRV for {uri} resolved to {host} and port {port}.")]
        public static partial void DnsSrvResolved(
            this ILogger logger,
            SIPURI uri,
            string host,
            int port);

        [LoggerMessage(
            EventId = 0,
            EventName = "DnsSrvError",
            Level = LogLevel.Warning,
            Message = "SIPDNS exception on SRV lookup. {errorMessage}.")]
        public static partial void DnsSrvException(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "DnsAAAALookupError",
            Level = LogLevel.Warning,
            Message = "SIPDNS exception on AAAA lookup. {errorMessage}.")]
        public static partial void DnsAAAAException(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "DnsALookupError", 
            Level = LogLevel.Warning,
            Message = "SIPDNS exception on A lookup. {errorMessage}.")]
        public static partial void DnsAException(
            this ILogger logger,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "DnsOSLookupFailed",
            Level = LogLevel.Warning,
            Message = "Operating System DNS lookup failed for {hostAddress}.")]
        public static partial void DnsOSLookupFailed(
            this ILogger logger,
            string hostAddress);
    }
}
