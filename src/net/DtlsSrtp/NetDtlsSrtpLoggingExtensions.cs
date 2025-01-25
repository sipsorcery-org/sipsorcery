using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Tls;

namespace SIPSorcery.Net
{
    internal static partial class NetDtlsSrtpLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "ClientCipherSuitNames",
            Level = LogLevel.Trace,
            Message = "Client offered cipher suites:\n {ClientCipherSuites}",
            SkipEnabledCheck = true)]
        private static partial void LogClientCipherSuitNames(
            this ILogger logger,
            string clientCipherSuites);

        [LoggerMessage(
            EventId = 0,
            EventName = "ServerCipherSuitNames",
            Level = LogLevel.Trace,
            Message = "Server offered cipher suites:\n {ServerCipherSuites}",
            SkipEnabledCheck = true)]
        private static partial void LogServerCipherSuitNames(
            this ILogger logger,
            string serverCipherSuites);

        public static void LogCipherSuitNames(

            this ILogger logger,
            int[] serverCipherSuites,
            int[] offeredCipherSuites)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                LogServerCipherSuitNames(logger, ConvertCipherSuitesToNames(serverCipherSuites));
                LogClientCipherSuitNames(logger, ConvertCipherSuitesToNames(offeredCipherSuites));

                static string ConvertCipherSuitesToNames(int[] cipherSuites)
                {
                    string[] cipherSuiteNames = new string[cipherSuites.Length];

                    for (int i = 0; i < cipherSuites.Length; i++)
                    {
                        if (DtlsUtils.CipherSuiteNames.TryGetValue(cipherSuites[i], out string value))
                        {
                            cipherSuiteNames[i] = value;
                        }
                        else
                        {
                            cipherSuiteNames[i] = cipherSuites[i].ToString();
                        }
                    }

                    return string.Join("\n ", cipherSuiteNames);
                }
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsHandshakeStart",
            Level = LogLevel.Debug,
            Message = "DTLS commencing handshake as {Role}.")]
        public static partial void LogDtlsHandshakeStartUnchecked(
            this ILogger logger,
            string role);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsHandshakeTimeout",
            Level = LogLevel.Warning,
            Message = "DTLS handshake as {Role} timed out waiting for handshake to complete.")]
        public static partial void LogDtlsHandshakeTimeout(
            this ILogger logger,
            string role,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsHandshakeFailed",
            Level = LogLevel.Warning,
            Message = "DTLS handshake as {Role} failed. {ErrorMessage}")]
        public static partial void LogDtlsHandshakeFailed(
            this ILogger logger,
            string role,
            string errorMessage,
            Exception ex);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsCloseNotification",
            Level = LogLevel.Debug,
            Message = "DTLS client raised close notification: {AlertMessage}")]
        private static partial void LogDtlsCloseNotificationUnchecked(
            this ILogger logger,
            string alertMessage,
            Exception exception);

        public static void LogDtlsCloseNotification(
            this ILogger logger,
            byte alertLevel,
            byte alertDescription,
            string message,
            Exception cause)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var alertMessage = BuildAlertMessage(alertLevel, alertDescription, message, cause);

                logger.LogDtlsCloseNotificationUnchecked(alertMessage.ToString(), cause);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsUnexpectedAlert",
            Level = LogLevel.Warning,
            Message = "DTLS client raised unexpected alert: {AlertMessage}")]
        private static partial void LogDtlsUnexpectedAlertUnchecked(
            this ILogger logger,
            string alertMessage,
            Exception exception);

        public static void LogDtlsUnexpectedAlert(
            this ILogger logger,
            byte alertLevel,
            byte alertDescription,
            string message,
            Exception cause)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                var alertMessage = BuildAlertMessage(alertLevel, alertDescription, message, cause);

                logger.LogDtlsUnexpectedAlertUnchecked(alertMessage.ToString(), cause);
            }
        }

        private static StringBuilder BuildAlertMessage(byte alertLevel, byte alertDescription, string message, Exception cause)
        {
            var description = new StringBuilder();
            if (!string.IsNullOrEmpty(message))
            {
                description.Append(message);
            }
            if (cause is not null)
            {
                description.Append(cause);
            }

            var alertMessage = new StringBuilder();
            alertMessage.Append(AlertLevel.GetText(alertLevel));
            alertMessage.Append(", ");
            alertMessage.Append(AlertDescription.GetText(alertDescription));
            if (description.Length > 0)
            {
                alertMessage.Append(", ");
                alertMessage.Append(description);
            }
            alertMessage.Append('.');
            return alertMessage;
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsReceivedCloseNotification",
            Level = LogLevel.Debug,
            Message = "DTLS client received close notification: {AlertLevel}, {Description}")]
        private static partial void LogDtlsReceivedCloseUnchecked(
            this ILogger logger,
            string alertLevel,
            string description);

        public static void LogDtlsReceivedClose(
            this ILogger logger,
            byte alertLevel,
            string description)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDtlsReceivedCloseUnchecked(AlertLevel.GetText(alertLevel), description);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsReceivedUnexpectedAlert",
            Level = LogLevel.Warning,
            Message = "DTLS client received unexpected alert: {AlertLevel}, {Description}")]
        private static partial void LogDtlsReceivedUnexpectedAlertUnchecked(
            this ILogger logger,
            string alertLevel,
            string description);

        public static void LogDtlsReceivedUnexpectedAlert(
            this ILogger logger,
            byte alertLevel,
            string description)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogDtlsReceivedUnexpectedAlertUnchecked(AlertLevel.GetText(alertLevel), description);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsServerNoMatchingCipherSuite",
            Level = LogLevel.Warning,
            Message = "DTLS server no matching cipher suite. Most likely issue is the client not supporting the server certificate's digital signature algorithm of {SignatureAlgorithm}.")]
        public static partial void LogDtlsServerNoMatchingCipherSuite(
            this ILogger logger,
            string signatureAlgorithm);

        [LoggerMessage(
            EventId = 1,
            EventName = "DtlsNoRenegotiation",
            Level = LogLevel.Warning,
            Message = "DTLS server received a client handshake without renegotiation support.")]
        public static partial void LogDtlsNoRenegotiation(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "SrtpSetupLocalCryptoFailed",
            Level = LogLevel.Error,
            Message = "Setup local crypto failed. No crypto attribute in {MessageType}.")]
        public static partial void LogSrtpSetupLocalCryptoFailedUnchecked(
            this ILogger logger,
            string messageType);

        [LoggerMessage(
            EventId = 0,
            EventName = "SrtpSetupRemoteCryptoFailed",
            Level = LogLevel.Error,
            Message = "Setup remote crypto failed. No crypto attribute in {MessageType}.")]
        public static partial void LogSrtpSetupRemoteCryptoFailedUnchecked(
            this ILogger logger,
            string messageType);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsHandshakeTimedOut",
            Level = LogLevel.Warning,
            Message = "DTLS transport timed out after {TimeoutMilliseconds}ms waiting for handshake from remote {Role}.")]
        public static partial void LogDtlsHandshakeTimedOut(
            this ILogger logger,
            int timeoutMilliseconds,
            string role);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsNoCertificate",
            Level = LogLevel.Warning,
            Message = "No certificate was set for " + nameof(DtlsSrtpServer) + ".")]
        public static partial void LogDtlsNoCertificate(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsSelectedCipherSuite",
            Level = LogLevel.Debug,
            Message = "Selected cipher suite: {CipherSuiteName}. Using {SignatureAlgorithm} certificate with fingerprint {Fingerprint}.")]
        public static partial void LogDtlsSelectedCipherSuite(
            this ILogger logger,
            string cipherSuiteName,
            string signatureAlgorithm,
            RTCDtlsFingerprint fingerprint);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsServerReceivedClose",
            Level = LogLevel.Debug,
            Message = "DTLS server received close notification: {AlertMsg}")]
        private static partial void LogDtlsServerReceivedCloseUnchecked(
            this ILogger logger,
            string alertMsg);

        public static void LogDtlsServerReceivedClose(
            this ILogger logger,
            byte alertLevel,
            string description)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var msg = $"{AlertLevel.GetText(alertLevel)}{((!string.IsNullOrEmpty(description)) ? $", {description}." : ".")}";
                logger.LogDtlsServerReceivedCloseUnchecked(msg);
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsServerReceivedUnexpectedAlert", 
            Level = LogLevel.Warning,
            Message = "DTLS server received unexpected alert: {AlertMsg}")]
        private static partial void LogDtlsServerReceivedUnexpectedAlertUnchecked(
            this ILogger logger,
            string alertMsg);

        public static void LogDtlsServerReceivedUnexpectedAlert(
            this ILogger logger,
            byte alertLevel, 
            string description)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                var msg = $"{AlertLevel.GetText(alertLevel)}{((!string.IsNullOrEmpty(description)) ? $", {description}." : ".")}";
                logger.LogDtlsServerReceivedUnexpectedAlertUnchecked(msg); 
            }
        }

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsHandshakeStartAsClient",
            Level = LogLevel.Debug,
            Message = "DTLS commencing handshake as client.")]
        public static partial void LogDtlsHandshakeStartAsClient(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "DtlsHandshakeStartAsServer",
            Level = LogLevel.Debug,
            Message = "DTLS commencing handshake as server.")]
        public static partial void LogDtlsHandshakeStartAsServer(
            this ILogger logger);
    }
}
