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

    private bool _logBody = false;

    public HttpLoggingHandler(ILogger<HttpLoggingHandler> logger, bool logBody = false)
    {
        _logger = logger ?? _logger;
        _logBody = logBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Log request with better formatting
        var requestId = Guid.NewGuid().ToString("N").Substring(0, 8); // Short ID for correlation

        _logger.LogDebug("🚀 HTTP Request [{RequestId}]", requestId);
        _logger.LogDebug("  Method: {Method}", request.Method);
        _logger.LogDebug("  URL: {RequestUri}", request.RequestUri);

        // Log headers in a more readable format
        LogHeaders("  Request Headers:", request.Headers, SensitiveHeaders);

        if (request.Content != null)
        {
            LogHeaders("  Content Headers:", request.Content.Headers, null);

            if (_logBody)
            {
                var requestBody = await request.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogDebug("  Request Body:\n{RequestBody}", FormatBody(requestBody));
                }
            }
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        stopwatch.Stop();

        // Log response with better formatting
        var statusIcon = response.IsSuccessStatusCode ? "✅" : "❌";
        _logger.LogInformation("{StatusIcon} HTTP Response [{RequestId}] - {StatusCode} {ReasonPhrase} ({ElapsedMs}ms)",
            statusIcon, requestId, (int)response.StatusCode, response.ReasonPhrase, stopwatch.ElapsedMilliseconds);

        LogHeaders("  Response Headers:", response.Headers, null);

        if (response.Content != null)
        {
            LogHeaders("  Content Headers:", response.Content.Headers, SensitiveHeaders);

            if (_logBody)
            {
                var responseBody = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(responseBody))
                {
                    _logger.LogDebug("  Response Body:\n{ResponseBody}", FormatBody(responseBody));
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

        _logger.LogDebug(title);
        foreach (var header in headers)
        {
            var isExcluded = excludeHeaderNames is not null &&
                             excludeHeaderNames.Any(h => string.Equals(h, header.Key, StringComparison.OrdinalIgnoreCase));

            var value = isExcluded ? "[REDACTED]" : string.Join(", ", header.Value ?? Array.Empty<string>());
            _logger.LogDebug("    {HeaderName}: {HeaderValue}", header.Key, value);
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
        if (body.TrimStart().StartsWith("{") || body.TrimStart().StartsWith("["))
        {
            var parsed = body.FromJson<object>();
            if (parsed != null)
            {
                return parsed.ToJson();
            }
        }

        // For non-JSON or if JSON parsing failed, add some basic formatting
        return body.Length > 500 ? $"{body.Substring(0, 500)}... (truncated, {body.Length} total chars)" : body;
    }
}
