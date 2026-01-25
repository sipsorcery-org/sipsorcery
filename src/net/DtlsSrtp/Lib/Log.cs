// SharpSRTP
// Copyright (C) 2025 Lukas Volf
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE 
// SOFTWARE.

using System;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;

namespace SIPSorcery.Net.SharpSRTP
{
    public static class Log
    {
        // wire up the sipsorcery's logger
        public static ILogger Logger { get; } = SIPSorcery.LogFactory.CreateLogger("sipsorcery.Net.SharpSRTP");

        public static void LogDtlsServerAlertRaised(this ILogger logger, short alertLevel, short alertDescription, string message, Exception cause)
        {
            SharpSrtpLoggingExtensions.LogDtlsServerAlertRaisedImpl(
                logger,
                AlertLevel.GetText(alertLevel),
                AlertDescription.GetText(alertDescription),
                message,
                cause);
        }

        public static void LogDtlsServerAlertReceived(this ILogger logger, short level, short alertDescription)
        {
            SharpSrtpLoggingExtensions.LogDtlsServerAlertReceivedImpl(
                logger,
                AlertLevel.GetText(level),
                AlertDescription.GetText(alertDescription));
        }

        public static void LogDtlsServerNegotiated(this ILogger logger, ProtocolVersion serverVersion)
        {
            SharpSrtpLoggingExtensions.LogDtlsServerNegotiatedImpl(logger, serverVersion.ToString());
        }

        public static void LogDtlsServerCertificateChainReceived(this ILogger logger, int chainLength)
        {
            SharpSrtpLoggingExtensions.LogDtlsServerCertificateChainReceivedImpl(logger, chainLength);
        }

        public static void LogDtlsServerCertificateFingerprint(this ILogger logger, string fingerprint, string subject)
        {
            SharpSrtpLoggingExtensions.LogDtlsServerCertificateFingerprintImpl(logger, fingerprint, subject);
        }

        public static void LogDtlsServerAlpn(this ILogger logger, string alpnProtocol)
        {
            SharpSrtpLoggingExtensions.LogDtlsServerAlpnImpl(logger, alpnProtocol);
        }

        public static void LogDtlsServerTlsServerEndPoint(this ILogger logger, string tlsServerEndPoint)
        {
            SharpSrtpLoggingExtensions.LogDtlsServerTlsServerEndPointImpl(logger, tlsServerEndPoint);
        }

        public static void LogDtlsServerTlsUnique(this ILogger logger, string tlsUnique)
        {
            SharpSrtpLoggingExtensions.LogDtlsServerTlsUniqueImpl(logger, tlsUnique);
        }

        public static void LogDtlsClientAlertRaised(this ILogger logger, short alertLevel, short alertDescription, string message, Exception cause)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientAlertRaisedImpl(
                logger,
                AlertLevel.GetText(alertLevel),
                AlertDescription.GetText(alertDescription),
                message,
                cause);
        }

        public static void LogDtlsClientAlertReceived(this ILogger logger, short level, short alertDescription)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientAlertReceivedImpl(
                logger,
                AlertLevel.GetText(level),
                AlertDescription.GetText(alertDescription));
        }

        public static void LogDtlsClientNegotiated(this ILogger logger, ProtocolVersion serverVersion)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientNegotiatedImpl(logger, serverVersion.ToString());
        }

        public static void LogDtlsClientAlpn(this ILogger logger, string alpnProtocol)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientAlpnImpl(logger, alpnProtocol);
        }

        public static void LogDtlsClientSessionResumed(this ILogger logger, string sessionId)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientSessionResumedImpl(logger, sessionId);
        }

        public static void LogDtlsClientSessionEstablished(this ILogger logger, string sessionId)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientSessionEstablishedImpl(logger, sessionId);
        }

        public static void LogDtlsClientTlsServerEndPoint(this ILogger logger, string tlsServerEndPoint)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientTlsServerEndPointImpl(logger, tlsServerEndPoint);
        }

        public static void LogDtlsClientTlsUnique(this ILogger logger, string tlsUnique)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientTlsUniqueImpl(logger, tlsUnique);
        }

        public static void LogDtlsClientServerCertificateChainReceived(this ILogger logger, int chainLength)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientServerCertificateChainReceivedImpl(logger, chainLength);
        }

        public static void LogDtlsClientServerCertificateFingerprint(this ILogger logger, string fingerprint, string subject)
        {
            SharpSrtpLoggingExtensions.LogDtlsClientServerCertificateFingerprintImpl(logger, fingerprint, subject);
        }
    }

    internal static partial class SharpSrtpLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsServerAlertRaised",
            Level = LogLevel.Debug,
            Message = "DTLS server raised alert: {AlertLevel}, {AlertDescription}> {Message}")]
        internal static partial void LogDtlsServerAlertRaisedImpl(
            ILogger logger,
            string alertLevel,
            string alertDescription,
            string message,
            Exception cause);

        [LoggerMessage(
            EventId = 1,
            EventName = "DtlsServerAlertReceived",
            Level = LogLevel.Debug,
            Message = "DTLS server received alert: {AlertLevel}, {AlertDescription}")]
        internal static partial void LogDtlsServerAlertReceivedImpl(
            ILogger logger,
            string alertLevel,
            string alertDescription);

        [LoggerMessage(
            EventId = 2,
            EventName = "DtlsServerNegotiated",
            Level = LogLevel.Debug,
            Message = "DTLS server negotiated {ServerVersion}")]
        internal static partial void LogDtlsServerNegotiatedImpl(
            ILogger logger,
            string serverVersion);

        [LoggerMessage(
            EventId = 3,
            EventName = "DtlsServerCertificateChainReceived",
            Level = LogLevel.Debug,
            Message = "DTLS server received client certificate chain of length {ChainLength}")]
        internal static partial void LogDtlsServerCertificateChainReceivedImpl(
            ILogger logger,
            int chainLength);

        [LoggerMessage(
            EventId = 4,
            EventName = "DtlsServerCertificateFingerprint",
            Level = LogLevel.Debug,
            Message = "    fingerprint:SHA-256 {Fingerprint} ({Subject})")]
        internal static partial void LogDtlsServerCertificateFingerprintImpl(
            ILogger logger,
            string fingerprint,
            string subject);

        [LoggerMessage(
            EventId = 5,
            EventName = "DtlsServerAlpn",
            Level = LogLevel.Debug,
            Message = "Server ALPN: {AlpnProtocol}")]
        internal static partial void LogDtlsServerAlpnImpl(
            ILogger logger,
            string alpnProtocol);

        [LoggerMessage(
            EventId = 6,
            EventName = "DtlsServerTlsServerEndPoint",
            Level = LogLevel.Debug,
            Message = "Server 'tls-server-end-point': {TlsServerEndPoint}")]
        internal static partial void LogDtlsServerTlsServerEndPointImpl(
            ILogger logger,
            string tlsServerEndPoint);

        [LoggerMessage(
            EventId = 7,
            EventName = "DtlsServerTlsUnique",
            Level = LogLevel.Debug,
            Message = "Server 'tls-unique': {TlsUnique}")]
        internal static partial void LogDtlsServerTlsUniqueImpl(
            ILogger logger,
            string tlsUnique);

        [LoggerMessage(
            EventId = 8,
            EventName = "DtlsClientAlertRaised",
            Level = LogLevel.Debug,
            Message = "DTLS client raised alert: {AlertLevel}, {AlertDescription}> {Message}")]
        internal static partial void LogDtlsClientAlertRaisedImpl(
            ILogger logger,
            string alertLevel,
            string alertDescription,
            string message,
            Exception cause);

        [LoggerMessage(
            EventId = 9,
            EventName = "DtlsClientAlertReceived",
            Level = LogLevel.Debug,
            Message = "DTLS client received alert: {AlertLevel}, {AlertDescription}")]
        internal static partial void LogDtlsClientAlertReceivedImpl(
            ILogger logger,
            string alertLevel,
            string alertDescription);

        [LoggerMessage(
            EventId = 10,
            EventName = "DtlsClientNegotiated",
            Level = LogLevel.Debug,
            Message = "DTLS client negotiated {ServerVersion}")]
        internal static partial void LogDtlsClientNegotiatedImpl(
            ILogger logger,
            string serverVersion);

        [LoggerMessage(
            EventId = 11,
            EventName = "DtlsClientAlpn",
            Level = LogLevel.Debug,
            Message = "Client ALPN: {AlpnProtocol}")]
        internal static partial void LogDtlsClientAlpnImpl(
            ILogger logger,
            string alpnProtocol);

        [LoggerMessage(
            EventId = 12,
            EventName = "DtlsClientSessionResumed",
            Level = LogLevel.Debug,
            Message = "Client resumed session: {SessionId}")]
        internal static partial void LogDtlsClientSessionResumedImpl(
            ILogger logger,
            string sessionId);

        [LoggerMessage(
            EventId = 13,
            EventName = "DtlsClientSessionEstablished",
            Level = LogLevel.Debug,
            Message = "Client established session: {SessionId}")]
        internal static partial void LogDtlsClientSessionEstablishedImpl(
            ILogger logger,
            string sessionId);

        [LoggerMessage(
            EventId = 14,
            EventName = "DtlsClientTlsServerEndPoint",
            Level = LogLevel.Debug,
            Message = "Client 'tls-server-end-point': {TlsServerEndPoint}")]
        internal static partial void LogDtlsClientTlsServerEndPointImpl(
            ILogger logger,
            string tlsServerEndPoint);

        [LoggerMessage(
            EventId = 15,
            EventName = "DtlsClientTlsUnique",
            Level = LogLevel.Debug,
            Message = "Client 'tls-unique': {TlsUnique}")]
        internal static partial void LogDtlsClientTlsUniqueImpl(
            ILogger logger,
            string tlsUnique);

        [LoggerMessage(
            EventId = 16,
            EventName = "DtlsClientServerCertificateChainReceived",
            Level = LogLevel.Debug,
            Message = "DTLS client received server certificate chain of length {ChainLength}")]
        internal static partial void LogDtlsClientServerCertificateChainReceivedImpl(
            ILogger logger,
            int chainLength);

        [LoggerMessage(
            EventId = 17,
            EventName = "DtlsClientServerCertificateFingerprint",
            Level = LogLevel.Debug,
            Message = "DTLS client fingerprint:SHA-256 {Fingerprint} ({Subject})")]
        internal static partial void LogDtlsClientServerCertificateFingerprintImpl(
            ILogger logger,
            string fingerprint,
            string subject);
    }
}
