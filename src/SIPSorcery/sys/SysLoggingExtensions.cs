using System;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys;

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

    [LoggerMessage(
        EventId = 0,
        EventName = "NetworkAddressChangedUnsupported",
        Level = LogLevel.Warning,
        Message = "NetworkChange.NetworkAddressChanged is not supported on this runtime; the local-address cache will not auto-invalidate on adapter changes.")]
    internal static partial void LogNetworkAddressChangedUnsupported(
        this ILogger logger,
        Exception exception);
}
