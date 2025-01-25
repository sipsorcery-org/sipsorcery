using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys
{
    internal static partial class SysLoggingExtensions
    {
        [LoggerMessage(
            EventId = 0,
            EventName = "StorageTypeUnknown",
            Level = LogLevel.Error,
            Message = "StorageTypesConverter {storageType} unknown.")]
        internal static partial void LogStorageTypeUnknown(
            this ILogger logger,
            string storageType);

        [LoggerMessage(
            EventId = 0,
            EventName = "TimerDisposalError",
            Level = LogLevel.Error,
            Message = "Exception disposing timer. {ErrorMessage}")]
        public static partial void LogTimerDisposalError(
            this ILogger logger,
            string errorMessage,
            Exception exception);
    }
}
