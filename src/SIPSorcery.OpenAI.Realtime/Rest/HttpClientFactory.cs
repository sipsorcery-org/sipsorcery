//-----------------------------------------------------------------------------
// Filename: HttpClientFactory.cs
//
// Description: HTTP client factory for use in non-dependency injection scenarios.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26 May 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;

namespace SIPSorcery.OpenAI.Realtime;

public class HttpClientFactory : IHttpClientFactory
{
    private readonly string _openAiKey;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly ConcurrentDictionary<string, HttpClient> _clients
        = new ConcurrentDictionary<string, HttpClient>();

    public HttpClientFactory(string openAiKey, ILoggerFactory? loggerFactory = null)
    {
        _openAiKey = openAiKey;
        _loggerFactory = loggerFactory;
    }

    public HttpClient CreateClient(string name)
    {
        return _clients.GetOrAdd(name, _ =>
        {
            HttpClient? client = null;

            if (_loggerFactory == null)
            {
                client = new HttpClient();
            }
            else
            {
                var handler = new HttpClientHandler();
                var logger = _loggerFactory.CreateLogger<HttpLoggingHandler>();
                var loggingHandler = new HttpLoggingHandler(logger) { InnerHandler = handler };
                client =  new HttpClient(loggingHandler);                  
            }

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openAiKey);

            return client;
        });
    }
}
