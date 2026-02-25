using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Tls;
using SIPSorcery.Net.SharpSRTP;
using SIPSorcery.Net.SharpSRTP.DTLSSRTP;

namespace SIPSorcery.Net;

internal static partial class NetDtlsSrtpLoggingExtensions
{
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
        short alertLevel,
        short alertDescription,
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
        short alertLevel,
        short alertDescription,
        string message,
        Exception cause)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            var alertMessage = BuildAlertMessage(alertLevel, alertDescription, message, cause);

            logger.LogDtlsUnexpectedAlertUnchecked(alertMessage.ToString(), cause);
        }
    }

    private static StringBuilder BuildAlertMessage(short alertLevel, short alertDescription, string message, Exception cause)
    {
        var description = new StringBuilder();
        if (!string.IsNullOrEmpty(message))
        {
            description.Append(message);
        }
        if (cause is { })
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
        short alertLevel,
        string description)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDtlsReceivedCloseUnchecked(
                AlertLevel.GetText(alertLevel),
                description);
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
        short alertLevel,
        string description)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogDtlsReceivedUnexpectedAlertUnchecked(
                AlertLevel.GetText(alertLevel),
                description);
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
        short alertLevel,
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
        short alertLevel,
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
        EventName = "DtlsServerAlertRaised",
        Level = LogLevel.Debug,
        Message = "DTLS server raised alert: {AlertLevel}, {AlertDescription}> {Message}",
        SkipEnabledCheck = true)]
    private static partial void LogDtlsServerAlertRaisedUnchecked(
        this ILogger logger,
        string alertLevel,
        string alertDescription,
        string message,
        Exception cause);

    public static void LogDtlsServerAlertRaised(
        this ILogger logger,
        short alertLevel,
        short alertDescription,
        string message,
        Exception cause)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDtlsServerAlertRaisedUnchecked(
                AlertLevel.GetText(alertLevel),
                AlertDescription.GetText(alertDescription),
                message,
                cause);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsServerAlertReceived",
        Level = LogLevel.Debug,
        Message = "DTLS server received alert: {AlertLevel}, {AlertDescription}",
        SkipEnabledCheck = true)]
    private static partial void LogDtlsServerAlertReceivedUnchecked(
        this ILogger logger,
        string alertLevel,
        string alertDescription);

    public static void LogDtlsServerAlertReceived(
        this ILogger logger,
        short level,
        short alertDescription)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDtlsServerAlertReceivedUnchecked(
                AlertLevel.GetText(level),
                AlertDescription.GetText(alertDescription));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsServerNegotiated",
        Level = LogLevel.Debug,
        Message = "DTLS server negotiated {ServerVersion}")]
    public static partial void LogDtlsServerNegotiated(
        this ILogger logger,
        ProtocolVersion serverVersion);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsServerCertificateChainReceived",
        Level = LogLevel.Debug,
        Message = "DTLS server received client certificate chain of length {ChainLength}")]
    public static partial void LogDtlsServerCertificateChainReceived(
        this ILogger logger,
        int chainLength);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsServerCertificateFingerprint",
        Level = LogLevel.Debug,
        Message = "    fingerprint:SHA-256 {Fingerprint} ({Subject})")]
    public static partial void LogDtlsServerCertificateFingerprint(
        this ILogger logger,
        string fingerprint,
        string subject);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsServerAlpn",
        Level = LogLevel.Debug,
        Message = "Server ALPN: {AlpnProtocol}")]
    public static partial void LogDtlsServerAlpn(
        this ILogger logger,
        string alpnProtocol);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsServerTlsServerEndPoint",
        Level = LogLevel.Debug,
        Message = "Server 'tls-server-end-point': {TlsServerEndPoint}")]
    public static partial void LogDtlsServerTlsServerEndPoint(
        this ILogger logger,
        string tlsServerEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsServerTlsUnique",
        Level = LogLevel.Debug,
        Message = "Server 'tls-unique': {TlsUnique}")]
    public static partial void LogDtlsServerTlsUnique(
        this ILogger logger,
        string tlsUnique);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientAlertRaised",
        Level = LogLevel.Debug,
        Message = "DTLS client raised alert: {AlertLevel}, {AlertDescription}> {Message}",
        SkipEnabledCheck = true)]
    private static partial void LogDtlsClientAlertRaisedUnchecked(
        this ILogger logger,
        string alertLevel,
        string alertDescription,
        string message,
        Exception cause);

    public static void LogDtlsClientAlertRaised(
        this ILogger logger,
        short alertLevel,
        short alertDescription,
        string message,
        Exception cause)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDtlsClientAlertRaisedUnchecked(
                AlertLevel.GetText(alertLevel),
                AlertDescription.GetText(alertDescription),
                message,
                cause);
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientAlertReceived",
        Level = LogLevel.Debug,
        Message = "DTLS client received alert: {AlertLevel}, {AlertDescription}",
        SkipEnabledCheck = true)]
    private static partial void LogDtlsClientAlertReceivedUnchecked(
        this ILogger logger,
        string alertLevel,
        string alertDescription);

    public static void LogDtlsClientAlertReceived(
        this ILogger logger,
        short level,
        short alertDescription)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDtlsClientAlertReceivedUnchecked(
                AlertLevel.GetText(level),
                AlertDescription.GetText(alertDescription));
        }
    }

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientNegotiated",
        Level = LogLevel.Debug,
        Message = "DTLS client negotiated {ServerVersion}")]
    public static partial void LogDtlsClientNegotiated(
        this ILogger logger,
        ProtocolVersion serverVersion);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientAlpn",
        Level = LogLevel.Debug,
        Message = "Client ALPN: {AlpnProtocol}")]
    public static partial void LogDtlsClientAlpn(
        this ILogger logger,
        string alpnProtocol);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientSessionResumed",
        Level = LogLevel.Debug,
        Message = "Client resumed session: {SessionId}")]
    public static partial void LogDtlsClientSessionResumed(
        this ILogger logger,
        string sessionId);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientSessionEstablished",
        Level = LogLevel.Debug,
        Message = "Client established session: {SessionId}")]
    public static partial void LogDtlsClientSessionEstablished(
        this ILogger logger,
        string sessionId);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientTlsServerEndPoint",
        Level = LogLevel.Debug,
        Message = "Client 'tls-server-end-point': {TlsServerEndPoint}")]
    public static partial void LogDtlsClientTlsServerEndPoint(
        this ILogger logger,
        string tlsServerEndPoint);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientTlsUnique",
        Level = LogLevel.Debug,
        Message = "Client 'tls-unique': {TlsUnique}")]
    public static partial void LogDtlsClientTlsUnique(
        this ILogger logger,
        string tlsUnique);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientServerCertificateChainReceived",
        Level = LogLevel.Debug,
        Message = "DTLS client received server certificate chain of length {ChainLength}")]
    public static partial void LogDtlsClientServerCertificateChainReceived(
        this ILogger logger,
        int chainLength);

    [LoggerMessage(
        EventId = 0,
        EventName = "DtlsClientServerCertificateFingerprint",
        Level = LogLevel.Debug,
        Message = "DTLS client fingerprint:SHA-256 {Fingerprint} ({Subject})")]
    public static partial void LogDtlsClientServerCertificateFingerprint(
        this ILogger logger,
        string fingerprint,
        string subject);
}
