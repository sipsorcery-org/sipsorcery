//-----------------------------------------------------------------------------
// Filename: CloudflareTurnApiClientFactory.cs
//
// Description: Cloudflare TURN API client factory.
//
// See: https://developers.cloudflare.com/realtime/turn/generate-credentials/
// To generate the Cloudflare TURN key API token see: https://developers.cloudflare.com/realtime/turn/generate-credentials/#create-a-turn-key
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 02 Jun 2026  Aaron Clauson   Created, Wexford, Ireland.
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

namespace SIPSorcery.Examples;

public class CloudflareTurnApiClientFactory : IHttpClientFactory
{
    public const string CLOUDFLARE_TURN_BASE_URL = "https://rtc.live.cloudflare.com/v1/turn/";

    public const int CLOUDFLARE_HTTP_CLIENT_TIMEOUT_SECONDS = 15;

    /// <summary>
    /// Period to recycle the pooled HTTP clients. This is needed to ensure that DNS changes are picked up and to prevent stale connections.
    /// </summary>
    private const int RECYCLE_CLIENT_MINUTES = 2;

    private readonly string _cloudflareTurnKeyApiToken;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly Lazy<HttpClient> _client;

    public CloudflareTurnApiClientFactory(string cloudflareTurnKeyApiToken, ILoggerFactory? loggerFactory = null)
    {
        _cloudflareTurnKeyApiToken = cloudflareTurnKeyApiToken;
        _loggerFactory = loggerFactory;

        _client = new Lazy<HttpClient>(CreateConfiguredClient);
    }

    /// <summary>
    /// Returns a configured HttpClient instance for the Cloudflare TURN HTTP API. The client is lazily initialized and will be recycled after a period of time to ensure that DNS changes are picked up and to prevent stale connections. The name parameter is ignored as this factory only serves the Cloudflare TURN client. This method is thread-safe due to the use of Lazy&lt;T&gt;.
    /// </summary>
    /// <param name="name">name is part of the IHttpClientFactory contract but irrelevant here — this factory only ever serves the Cloudflare TURN HTTP API client.</param>
    /// <returns>A configured HttpClient instance for the Cloudflare TURN HTTP API.</returns>
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

        Configure(client, _cloudflareTurnKeyApiToken);

        return client;
    }

    public static void Configure(HttpClient client, string cloudflareTurnKeyApiToken)
    {
        client.BaseAddress = new Uri(CLOUDFLARE_TURN_BASE_URL);
        client.Timeout = TimeSpan.FromSeconds(CLOUDFLARE_HTTP_CLIENT_TIMEOUT_SECONDS);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cloudflareTurnKeyApiToken);
    }
}
