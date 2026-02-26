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
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SIPSorcery.OpenAI.Realtime;

/// <summary>
/// This class can be used to log events that occur in the HttpClient operation.
/// </summary>
public class HttpLoggingHandler : DelegatingHandler
{
    private ILogger _logger = NullLogger.Instance;

    // Sensitive headers to redact.
    private static readonly string[] SensitiveHeaders = [ "Authorization" ];

    public HttpLoggingHandler(ILogger<HttpLoggingHandler> logger)
    {
        _logger = logger ?? _logger;
    }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Log request with better formatting
            var requestId = Guid.NewGuid().ToString("N")[..8]; // Short ID for correlation
        
            _logger.LogTrace("🚀 HTTP Request [{RequestId}]", requestId);
            _logger.LogTrace("  Method: {Method}", request.Method);
            _logger.LogTrace("  URL: {RequestUri}", request.RequestUri);
        
            // Log headers in a more readable format
            LogHeaders("  Request Headers:", request.Headers, SensitiveHeaders);
        
            if (request.Content != null)
            {
                LogHeaders("  Content Headers:", request.Content.Headers, null);
            
                var requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrEmpty(requestBody))
                {
                    _logger.LogTrace("  Request Body:\n{RequestBody}", FormatBody(requestBody));
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

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrEmpty(responseBody))
                {
                    _logger.LogTrace("  Response Body:\n{ResponseBody}", FormatBody(responseBody));
                }
            }

        return response;
    }

    private void LogHeaders(string title, HttpHeaders headers, string[]? excludeHeaderNames)
    {
        if (headers == null || !headers.Any()) return;

        _logger.LogTrace(title);
        foreach (var header in headers)
        {
            var isExcluded = excludeHeaderNames is not null &&
                             excludeHeaderNames.Any(h => string.Equals(h, header.Key, StringComparison.OrdinalIgnoreCase));

            var value = isExcluded ? "[REDACTED]" : string.Join(", ", header.Value ?? Array.Empty<string>());
            _logger.LogTrace("    {HeaderName}: {HeaderValue}", header.Key, value);
        }
    }

    private static string FormatBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return body;

        // Try to format as JSON if it looks like JSON
        if (body.TrimStart().StartsWith('{') || body.TrimStart().StartsWith('['))
        {
            try
            {
                var jsonDoc = System.Text.Json.JsonDocument.Parse(body);
                return System.Text.Json.JsonSerializer.Serialize(jsonDoc, new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
            }
            catch
            {
                // If JSON parsing fails, return as-is
            }
        }

        // For non-JSON or if JSON parsing failed, add some basic formatting
        return body.Length > 500 ? $"{body[..500]}... (truncated, {body.Length} total chars)" : body;
    }
}
