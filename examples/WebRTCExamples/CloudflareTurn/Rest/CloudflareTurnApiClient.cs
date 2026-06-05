//-----------------------------------------------------------------------------
// Filename: CloudflareTurnApiClient.cs
//
// Description: Used to send requests to create and revoke short term TURN credentials
// to the Cloudflare HTTP API.
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

using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading; 
using System.Threading.Tasks;
using LanguageExt;
using LanguageExt.Common;

namespace SIPSorcery.Examples;

public class CloudflareTurnApiClient : ICloudflareTurnApiClient 
{
    public const string CLOUDFLARE_HTTP_CLIENT_NAME = "cloudflare-turn";

    private readonly IHttpClientFactory _factory;

    public CloudflareTurnApiClient(IHttpClientFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a short-lived TURN credential.
    /// </summary>
    /// <param name="turnKeyID">A long term secret that can be used to manage unlimited short-term credentials.</param>
    /// <param name="secondsToLive">The maximum number of seconds the TURN session needs to be valid for. If the session
    /// finishes early the revoke credentials call can be used to invalidate the credentials.</param>
    public async Task<Either<Error, CloudflareIceServers>> CreateCredentialsAsync(
        string turnKeyID,
        int secondsToLive,
        CancellationToken ct = default)
    {
        var client = _factory.CreateClient(CLOUDFLARE_HTTP_CLIENT_NAME);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"keys/{turnKeyID}/credentials/generate-ice-servers");

        req.Content = new StringContent(
            JsonSerializer.Serialize(new { data = new { ttl = secondsToLive } }),
            Encoding.UTF8,
            "application/json");

        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Error.New($"CreateEphemeralKey failed [{res.StatusCode}]: {body}");
        }
        else
        {
            var iceServers = await res.Content.ReadFromJsonAsync<CloudflareIceServers>(cancellationToken: ct).ConfigureAwait(false);

            return iceServers != null
                ? iceServers
                : Error.New("Failed to parse JSON response from Cloudflare TURN API.");
        }
    }

    /// <summary>
    /// Revokes the short-lived TURN credential immediately. This can be used to invalidate credentials before the expiry time if the session finishes early.
    /// </summary>
    /// <param name="turnKeyID">A long term secret that can be used to manage unlimited short-term credentials.</param>
    /// <param name="username">The username of the short-term TURN credentials to revoke.</param>
    public async Task<Option<Error>> RevokeCredentialsAsync(
        string turnKeyID,
        string username,
        CancellationToken ct = default)
    {
        var client = _factory.CreateClient(CLOUDFLARE_HTTP_CLIENT_NAME);

        using var req = new HttpRequestMessage(HttpMethod.Post, $"keys/{turnKeyID}/credentials/{username}/revoke");

        using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Error.New($"Revoke Cloudflare TURN credentials failed [{res.StatusCode}]: {body}");
        }

        return Option<Error>.None;
    }
}
