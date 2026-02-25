//-----------------------------------------------------------------------------
// Filename: HttpLoggingHandler.cs
//
// Description: Provides a logging hook for the HttpClient.
//
// References:
// See https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-requests?view=aspnetcore-7.0#outgoing-request-middleware.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26 Feb 2026  Aaron Clauson   Created, Dublin, Ireland.
// 28 May 2026  Aaron Clauson   Moved from the OpenAI.Realtime project into the
//                              main library to allow re-use.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TinyJson;

#nullable enable

namespace SIPSorcery.Sys;

/// <summary>
/// This class can be used to log events that occur in the HttpClient operation.
/// </summary>
public class HttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger _logger = NullLogger.Instance;

    // Sensitive headers to redact.
    private static readonly string[] SensitiveHeaders = ["Authorization"];

    private readonly bool _logBody = false;

    public HttpLoggingHandler(ILogger<HttpLoggingHandler> logger, bool logBody = false)
    {
        _logger = logger ?? _logger;
        _logBody = logBody;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_logger.IsEnabled(LogLevel.Information))
        {
            return base.SendAsync(request, cancellationToken);
        }

        return SendAsyncWithLogging(request, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsyncWithLogging(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestId = RandomNumberGenerator.GetHexString(8);

        var logDebugEnabled = _logger.IsEnabled(LogLevel.Debug);

        if (logDebugEnabled)
        {
            // Log request with better formatting.
            _logger.LogHttpRequestMessage(requestId);
            _logger.LogHttpMethodMessage(request.Method);
            _logger.LogHttpRequestUriMessage(request.RequestUri);

            // Log headers in a more readable format.
            LogHeaders("  Request Headers:", request.Headers, SensitiveHeaders);

            if (request.Content != null)
            {
                LogHeaders("  Content Headers:", request.Content.Headers, null);

                if (_logBody)
                {
                    var requestBody = await request.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(requestBody))
                    {
                        _logger.LogHttpRequestBodyMessage(FormatBody(requestBody));
                    }
                }
            }
        }

        var startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        var elapsedMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(startTimestamp);

        // Log response with better formatting.
        var statusIcon = response.IsSuccessStatusCode ? "✅" : "❌";
        _logger.LogHttpResponseMessage(statusIcon, requestId, (int)response.StatusCode, response.ReasonPhrase ?? "", elapsedMilliseconds.TotalMilliseconds);

        if (logDebugEnabled)
        {
            LogHeaders("  Response Headers:", response.Headers, null);

            if (response.Content != null)
            {
                LogHeaders("  Content Headers:", response.Content.Headers, SensitiveHeaders);

                if (_logBody)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(responseBody))
                    {
                        _logger.LogHttpResponseBodyMessage(FormatBody(responseBody));
                    }
                }
            }
        }

        return response;
    }

    private void LogHeaders(string title, HttpHeaders headers, string[]? excludeHeaderNames)
    {
        if (headers == null || !headers.Any())
        {
            return;
        }

        _logger.LogHttpHeadersTitleMessage(title);
        foreach (var header in headers)
        {
            var isExcluded = excludeHeaderNames is not null &&
                             excludeHeaderNames.Any(h => string.Equals(h, header.Key, StringComparison.OrdinalIgnoreCase));

            var value = isExcluded ? "[REDACTED]" : string.Join(", ", header.Value ?? Array.Empty<string>());
            _logger.LogHttpHeaderValueMessage(header.Key, value);
        }
    }

    private static string FormatBody(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return body;
        }

        // Try to normalise the body if it looks like JSON. Uses the bundled TinyJson
        // (JSONParser/JSONWriter) which is available on every target framework, unlike
        // System.Text.Json that is not present for net462/netstandard2.0. TinyJson
        // returns null on parse failure rather than throwing, so an invalid body simply
        // falls through to the raw output below.
        var trimmedBody = body.AsSpan().TrimStart();
        if (!trimmedBody.IsEmpty && trimmedBody[0] is '{' or '[')
        {
            var parsed = body.FromJson<object>();
            if (parsed != null)
            {
                return parsed.ToJson();
            }
        }

        const int maxLoggedBodyChars = 500;
        // For non-JSON or if JSON parsing failed, add some basic formatting
        if (body.Length <= maxLoggedBodyChars)
        {
            return body;
        }

        var truncatedBody = new StringBuilder(maxLoggedBodyChars + 64);
        truncatedBody
            .Append(body, 0, maxLoggedBodyChars)
            .Append("... (truncated, ")
            .Append(body.Length)
            .Append(" total chars)");

        return truncatedBody.ToString();
    }
}
