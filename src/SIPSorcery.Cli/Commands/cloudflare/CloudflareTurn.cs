//-----------------------------------------------------------------------------
// Filename: CloudflareTurn.cs
//
// Description: Shared helpers for the Cloudflare Realtime TURN API: the common
// command options (--key-id, --token, --ttl, --transport), the TURN URL for a
// transport, and fetching short lived credentials as a ready to use RTCIceServer.
// Used by both the "cloudflare turn" verb and any other verb that wants to route
// a peer connection through Cloudflare TURN (e.g. "webrtc echo").
//
// See: https://developers.cloudflare.com/realtime/turn/generate-credentials/
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace SIPSorcery.Cli.Commands;

public static class CloudflareTurn
{
    public const int DEFAULT_TTL_SECONDS = 120;

    private const string BASE_URL = "https://rtc.live.cloudflare.com/v1/turn/";
    private const string URL_TLS = "turns:turn.cloudflare.com:443";
    private const string URL_UDP = "turn:turn.cloudflare.com:3478?transport=udp";
    private const string URL_TCP = "turn:turn.cloudflare.com:3478?transport=tcp";

    public sealed record FetchResult(RTCIceServer? IceServer, string? Username, string? Error);

    private sealed class CloudflareIceServer
    {
        public string Username { get; set; } = string.Empty;
        public string Credential { get; set; } = string.Empty;
        public List<string> Urls { get; set; } = [];
    }

    private sealed class CloudflareIceServersResponse
    {
        public List<CloudflareIceServer> IceServers { get; set; } = [];
    }

    // Option factories so every verb exposes identical Cloudflare TURN options.

    public static Option<string?> CreateKeyIdOption() => new("--key-id")
    {
        Description = "Cloudflare TURN key ID. Defaults to the CLOUDFLARE_TURN_KEY_ID environment variable."
    };

    public static Option<string?> CreateTokenOption() => new("--token")
    {
        Description = "Cloudflare TURN key API token. Defaults to the CLOUDFLARE_API_TOKEN environment variable."
    };

    public static Option<int> CreateTtlOption() => new("--ttl")
    {
        Description = "Requested TURN credential lifetime in seconds.",
        DefaultValueFactory = _ => DEFAULT_TTL_SECONDS
    };

    public static Option<string> CreateTransportOption() => new("--transport")
    {
        Description = "Cloudflare TURN transport: tls (turns:443), udp or tcp (turn:3478).",
        DefaultValueFactory = _ => "tls"
    };

    /// <summary>
    /// Applies the CLOUDFLARE_TURN_KEY_ID / CLOUDFLARE_API_TOKEN environment fallbacks.
    /// </summary>
    public static void ResolveCredentials(ref string? keyId, ref string? token)
    {
        keyId ??= Environment.GetEnvironmentVariable("CLOUDFLARE_TURN_KEY_ID");
        token ??= Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");
    }

    public static bool TryResolveTurnUrl(string transport, out string turnUrl, out string? error)
    {
        error = null;
        turnUrl = transport?.ToLowerInvariant() switch
        {
            "tls" => URL_TLS,
            "udp" => URL_UDP,
            "tcp" => URL_TCP,
            _ => string.Empty
        };

        if (turnUrl.Length == 0)
        {
            error = $"Unknown TURN transport \"{transport}\". Expected tls, udp or tcp.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Requests short lived credentials from the Cloudflare Realtime TURN API and returns them as an
    /// RTCIceServer for the given transport URL. The credential is never logged at info level.
    /// </summary>
    public static async Task<FetchResult> FetchIceServerAsync(string keyId, string token, int ttl, string turnUrl,
        int timeoutSeconds, ILogger logger, CancellationToken ct)
    {
        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(BASE_URL), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"keys/{keyId}/credentials/generate-ice-servers")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { ttl }), Encoding.UTF8, "application/json")
            };

            using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                string detail = body.Length > 200 ? body[..200] : body;
                return new FetchResult(null, null, $"The Cloudflare TURN API returned HTTP {(int)response.StatusCode}. {detail}".TrimEnd());
            }

            var iceServers = await response.Content.ReadFromJsonAsync<CloudflareIceServersResponse>(cancellationToken: ct).ConfigureAwait(false);

            var server = iceServers?.IceServers.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Username));
            if (server == null)
            {
                return new FetchResult(null, null, "The Cloudflare TURN API response did not contain credentials.");
            }

            logger.LogDebug("Obtained Cloudflare TURN credentials for username {Username}.", server.Username);

            var iceServer = new RTCIceServer
            {
                urls = turnUrl,
                username = server.Username,
                credential = server.Credential,
                credentialType = RTCIceCredentialType.password
            };

            return new FetchResult(iceServer, server.Username, null);
        }
        catch (OperationCanceledException)
        {
            return new FetchResult(null, null, "Cancelled or the credentials request timed out.");
        }
        catch (Exception excp)
        {
            return new FetchResult(null, null, excp.Message);
        }
    }
}
