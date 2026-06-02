//-----------------------------------------------------------------------------
// Filename: OpenAIRestClientFactory.cs
//
// Description: OpenAI REST client factory for use in non-dependency injection scenarios.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26 May 2025  Aaron Clauson   Created, Dublin, Ireland.
// 02 Jun 2026  Aaron Clauson   Renamed from HttpClientFactory to OpenAIRestClientFactory.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.OpenAI.Realtime;

public class OpenAIRestClientFactory : IHttpClientFactory
{
    public const string OPENAI_REALTIME_BASE_URL = "https://api.openai.com/v1/realtime/";

    public const int OPENAI_HTTP_CLIENT_TIMEOUT_SECONDS = 15;

    /// <summary>
    /// Period to recycle the pooled HTTP clients. This is needed to ensure that DNS changes are picked up and to prevent stale connections.
    /// </summary>
    private const int RECYCLE_CLIENT_MINUTES = 2;

    private readonly string _openAiKey;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Lazy<HttpClient> _client;

    public OpenAIRestClientFactory(string openAiKey, ILoggerFactory? loggerFactory = null)
    {
        _openAiKey = openAiKey;
        _loggerFactory = loggerFactory;

        _client = new Lazy<HttpClient>(CreateConfiguredClient);
    }

    /// <summary>
    /// Returns a configured HttpClient instance for the OpenAI REST API. The client is lazily initialized and will be recycled after a period of time to ensure that DNS changes are picked up and to prevent stale connections. The name parameter is ignored as this factory only serves the OpenAI client. This method is thread-safe due to the use of Lazy&lt;T&gt;.
    /// </summary>
    /// <param name="name">name is part of the IHttpClientFactory contract but irrelevant here — this factory only ever serves the OpenAI client.</param>
    /// <returns>A configured HttpClient instance for the OpenAI REST API.</returns>
    public HttpClient CreateClient(string name) => _client.Value;

    private HttpClient CreateConfiguredClient()
    {
        var sockets = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(RECYCLE_CLIENT_MINUTES),
        };

        HttpMessageHandler handler = sockets;

        if (_loggerFactory is not null)
        {
            var logger = _loggerFactory.CreateLogger<HttpLoggingHandler>();
            handler = new HttpLoggingHandler(logger) { InnerHandler = sockets };
        }

        var client = new HttpClient(handler);

        Configure(client, _openAiKey);

        return client;
    }

    public static void Configure(HttpClient client, string openAiKey)
    {
        client.BaseAddress = new Uri(OPENAI_REALTIME_BASE_URL);
        client.Timeout = TimeSpan.FromSeconds(OPENAI_HTTP_CLIENT_TIMEOUT_SECONDS);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAiKey);
    }
}
