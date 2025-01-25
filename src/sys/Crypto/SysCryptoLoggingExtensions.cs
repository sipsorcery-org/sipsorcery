using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using System;

namespace SIPSorcery.Sys
{
    internal static partial class SysCryptoLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "CertificateStoreOpen",
            Level = LogLevel.Debug,
            Message = "Certificate store {StoreLocation} opened")]
        public static partial void LogCertificateStoreOpen(
            this ILogger logger,
            StoreLocation storeLocation);

        [LoggerMessage(
            EventId = 0,
            EventName = "CertificateLoaded",
            Level = LogLevel.Debug,
            Message = "X509 certificate loaded from current user store, subject={Subject}, valid={Valid}.")]
        public static partial void LogCertificateLoaded(
            this ILogger logger,
            string subject,
            bool valid);

        [LoggerMessage(
            EventId = 0,
            EventName = "NonExistentHashFile",
            Level = LogLevel.Error,
            Message = "Cannot open a non-existent file for a hash operation, {filePath}.")]
        public static partial void LogNonExistentHashFile(
            this ILogger logger,
            string filePath);

        [LoggerMessage(
            EventId = 0,
            EventName = "EmptyHashFile",
            Level = LogLevel.Error,
            Message = "Cannot perform a hash operation on an empty file, {filePath}.")]
        public static partial void LogEmptyHashFile(
            this ILogger logger,
            string filePath);

        [LoggerMessage(
            EventId = 0,
            EventName = "CertificateNotFound",
            Level = LogLevel.Warning,
            Message = "X509 certificate with subject name={certificateSubject}, not found in {storeLocation} store.")]
        public static partial void LogCertificateNotFound(
            this ILogger logger,
            string certificateSubject,
            StoreLocation storeLocation);

        [LoggerMessage(
            EventId = 0,
            EventName = "CertificateLoadError",
            Level = LogLevel.Error,
            Message = "Exception loading certificate. {ErrorMessage}")]
        public static partial void LogCertificateLoadError(
            this ILogger logger,
            string errorMessage, 
            Exception exception);
    }
}
