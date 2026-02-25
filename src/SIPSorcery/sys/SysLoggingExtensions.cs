using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace SIPSorcery.Sys;

internal static partial class SysLoggingExtensions
{
    [LoggerMessage(
        EventId = 0,
        EventName = "StorageTypeUnknown",
        Level = LogLevel.Error,
        Message = "StorageTypesConverter {StorageType} unknown.")]
    public static partial void LogStorageTypeUnknown(
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
    public static partial void LogNetworkAddressChangedUnsupported(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 0,
        EventName = "HttpRequest",
        Level = LogLevel.Debug,
        Message = "HTTP Request received [{RequestId}].")]
    public static partial void LogHttpRequestMessage(
        this ILogger logger,
        string? requestId);

    [LoggerMessage(
        EventId = 0,
        EventName = "HttpMethod",
        Level = LogLevel.Debug,
        Message = "HTTP method is {HttpMethod}.")]
    public static partial void LogHttpMethodMessage(
        this ILogger logger,
        HttpMethod httpMethod);

    [LoggerMessage(
        EventId = 0,
        EventName = "HttpRequestUri",
        Level = LogLevel.Debug,
        Message = "HTTP request URI is {RequestUri}.")]
    public static partial void LogHttpRequestUriMessage(
        this ILogger logger,
        Uri? requestUri);

    [LoggerMessage(
        EventId = 0,
        EventName = "HttpResponse",
        Level = LogLevel.Information,
        Message = "{StatusIcon} HTTP Response [{RequestId}] - {StatusCode} {ReasonPhrase} ({ElapsedMilliseconds}ms)")]
    public static partial void LogHttpResponseMessage(
        this ILogger logger,
        string statusIcon,
        string requestId,
        int statusCode,
        string reasonPhrase,
        double elapsedMilliseconds);

    [LoggerMessage(
        EventId = 0,
        EventName = "HttpRequestBody",
        Level = LogLevel.Debug,
        Message = "Request Body:\n{RequestBody}")]
    public static partial void LogHttpRequestBodyMessage(
        this ILogger logger,
        string requestBody);

    [LoggerMessage(
        EventId = 0,
        EventName = "HttpResponseBody",
        Level = LogLevel.Debug,
        Message = "Response Body:\n{ResponseBody}")]
    public static partial void LogHttpResponseBodyMessage(
        this ILogger logger,
        string responseBody);

    [LoggerMessage(
        EventId = 0,
        EventName = "HttpHeadersTitle",
        Level = LogLevel.Debug,
        Message = "{Title}")]
    public static partial void LogHttpHeadersTitleMessage(
        this ILogger logger,
        string title);

    [LoggerMessage(
        EventId = 0,
        EventName = "HttpHeaderValue",
        Level = LogLevel.Debug,
        Message = "    {HeaderName}: {HeaderValue}")]
    public static partial void LogHttpHeaderValueMessage(
        this ILogger logger,
        string headerName,
        string headerValue);

    [LoggerMessage(
        EventId = 0,
        EventName = "CryptoHashNonExistentFile",
        Level = LogLevel.Error,
        Message = "Cannot open a non-existent file for a hash operation, {FilePath}.")]
    public static partial void LogCryptoHashNonExistentFile(
        this ILogger logger,
        string filePath);

    [LoggerMessage(
        EventId = 0,
        EventName = "CryptoHashEmptyFile",
        Level = LogLevel.Error,
        Message = "Cannot perform a hash operation on an empty file, {FilePath}.")]
    public static partial void LogCryptoHashEmptyFile(
        this ILogger logger,
        string filePath);

    [LoggerMessage(
        EventId = 0,
        EventName = "CertStoreOpened",
        Level = LogLevel.Debug,
        Message = "Certificate store {StoreLocation} opened")]
    public static partial void LogCertStoreOpened(
        this ILogger logger,
        StoreLocation storeLocation);

    [LoggerMessage(
        EventId = 0,
        EventName = "CertLoadedFromCurrentUserStore",
        Level = LogLevel.Debug,
        Message = "X509 certificate loaded from current user store, subject={Subject}, valid={Valid}.")]
    public static partial void LogCertLoadedFromCurrentUserStore(
        this ILogger logger,
        string subject,
        bool valid);

    [LoggerMessage(
        EventId = 0,
        EventName = "CertNotFoundInStore",
        Level = LogLevel.Warning,
        Message = "X509 certificate with subject name={CertificateSubject}, not found in {StoreLocation} store.")]
    public static partial void LogCertNotFoundInStore(
        this ILogger logger,
        string certificateSubject,
        StoreLocation storeLocation);
}
