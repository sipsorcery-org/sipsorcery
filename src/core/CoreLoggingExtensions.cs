using System;
using System.Net;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.SIP
{
    internal static partial class CoreLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "CreatingSIPChannel",
            Level = LogLevel.Debug,
            Message = "Creating SIP Channel for {SipSocketNode}.")]
        public static partial void LogCreatingSIPChannel(
            this ILogger logger,
            string sipSocketNode);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreatingUDPChannel",
            Level = LogLevel.Debug,
            Message = "attempting to create SIP UDP channel for {SipEndPoint}.")]
        public static partial void LogCreatingUDPChannel(
            this ILogger logger,
            IPEndPoint sipEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreatingTCPChannel",
            Level = LogLevel.Debug,
            Message = "attempting to create SIP TCP channel for {SipEndPoint}.")]
        public static partial void LogCreatingTCPChannel(
            this ILogger logger,
            IPEndPoint sipEndPoint);

        [LoggerMessage(
            EventId = 0,
            EventName = "MissingCertificatePath",
            Level = LogLevel.Warning,
            Message = "Could not create SIPTLSChannel from XML configuration node as no {CertificatePathParameter} attribute was present.")]
        public static partial void LogMissingCertificatePath(
            this ILogger logger,
            string certificatePathParameter);

        [LoggerMessage(
            EventId = 0,
            EventName = "CreatingTLSChannel",
            Level = LogLevel.Debug,
            Message = "attempting to create SIP TLS channel for {SipEndPoint} and certificate type of {CertificateType} at {CertificatePath}.",
            SkipEnabledCheck = true)]
        public static partial void LogCreatingTLSChannel(
            this ILogger logger,
            IPEndPoint sipEndPoint,
            string certificateType,
            string certificatePath);

        [LoggerMessage(
            EventId = 0,
            EventName = "CertificateLoadFailed",
            Level = LogLevel.Warning,
            Message = "A SIP TLS channel was not created because the certificate could not be loaded.")]
        public static partial void LogCertificateLoadFailed(
            this ILogger logger);

        [LoggerMessage(
            EventId = 0,
            EventName = "UnsupportedProtocol",
            Level = LogLevel.Warning,
            Message = "Could not create a SIP channel for protocol {Protocol}.")]
        public static partial void LogUnsupportedProtocol(
            this ILogger logger,
            SIPProtocolsEnum protocol);

        [LoggerMessage(
            EventId = 0,
            EventName = "SIPChannelError",
            Level = LogLevel.Warning,
            Message = "Exception SIPTransportConfig Adding SIP Channel for {SipEndPoint}. {ErrorMessage}.")]
        public static partial void LogSIPChannelError(
            this ILogger logger,
            SIPEndPoint sipEndPoint,
            string errorMessage,
            Exception exception);

        [LoggerMessage(
            EventId = 0,
            EventName = "CertificateLoadedFromFile",
            Level = LogLevel.Debug,
            Message = "Server Certificate loaded from file, Subject={Subject}, valid={Valid}.")]
        public static partial void LogCertificateLoadedFromFile(
            this ILogger logger,
            string subject,
            bool valid);

        [LoggerMessage(
            EventId = 0,
            EventName = "LoadCertificateException",
            Level = LogLevel.Error,
            Message = "Exception LoadCertificate. {ErrorMessage}")]
        public static partial void LoadCertificateError(
            this ILogger logger,
            string errorMessage,
            Exception exception);
    }
}
