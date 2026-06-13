//-----------------------------------------------------------------------------
// Filename: CloudflareTurnCommand.cs
//
// Description: The "sipsorcery cloudflare turn" verb. Requests short lived TURN
// credentials from the Cloudflare Realtime TURN API and then runs a relay only
// ICE gather against the Cloudflare TURN server to confirm a relay candidate is
// allocated. Answers "are my Cloudflare TURN credentials valid and does relay
// actually work from here".
//
// See: https://developers.cloudflare.com/realtime/turn/
// Credentials: https://developers.cloudflare.com/realtime/turn/generate-credentials/
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
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;

namespace SIPSorcery.Cli.Commands;

public sealed class CloudflareTurnCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 15;
    private const int DEFAULT_TTL_SECONDS = 120;
    private const string CLOUDFLARE_TURN_BASE_URL = "https://rtc.live.cloudflare.com/v1/turn/";

    private const string TURN_URL_TLS = "turns:turn.cloudflare.com:443";
    private const string TURN_URL_UDP = "turn:turn.cloudflare.com:3478?transport=udp";
    private const string TURN_URL_TCP = "turn:turn.cloudflare.com:3478?transport=tcp";

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// The credential itself is deliberately not included.
    /// </summary>
    private sealed record TurnResult(
        bool Success,
        string KeyId,
        int Ttl,
        string TurnUrl,
        string? Username,
        int RelayCandidates,
        long GatheringMs,
        string? Error);

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

    public CloudflareTurnCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var keyIdOption = new Option<string?>("--key-id")
        {
            Description = "The Cloudflare TURN key ID. Defaults to the CLOUDFLARE_TURN_KEY_ID environment variable."
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "The Cloudflare TURN key API token. Defaults to the CLOUDFLARE_API_TOKEN environment variable."
        };

        var ttlOption = new Option<int>("--ttl")
        {
            Description = "The requested credential lifetime in seconds.",
            DefaultValueFactory = _ => DEFAULT_TTL_SECONDS
        };

        var transportOption = new Option<string>("--transport")
        {
            Description = "The TURN transport to probe: tls (turns:443), udp or tcp (turn:3478).",
            DefaultValueFactory = _ => "tls"
        };

        var command = new Command("turn", "Fetch Cloudflare TURN credentials and verify a relay candidate can be allocated.");
        command.Options.Add(keyIdOption);
        command.Options.Add(tokenOption);
        command.Options.Add(ttlOption);
        command.Options.Add(transportOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(keyIdOption),
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(ttlOption),
            parseResult.GetValue(transportOption)!,
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string? keyId, string? token, int ttl, string transport,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(CloudflareTurnCommand));

        keyId ??= Environment.GetEnvironmentVariable("CLOUDFLARE_TURN_KEY_ID");
        token ??= Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");

        if (string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(token))
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId ?? string.Empty, ttl, string.Empty, null, 0, 0,
                    "A TURN key ID and API token are required (--key-id/--token or CLOUDFLARE_TURN_KEY_ID/CLOUDFLARE_API_TOKEN)."),
                ExitCodes.InvalidArgument);
        }

        string turnUrl = transport.ToLowerInvariant() switch
        {
            "tls" => TURN_URL_TLS,
            "udp" => TURN_URL_UDP,
            "tcp" => TURN_URL_TCP,
            _ => string.Empty
        };

        if (turnUrl.Length == 0)
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId, ttl, string.Empty, null, 0, 0,
                    $"Unknown --transport value \"{transport}\". Expected tls, udp or tcp."),
                ExitCodes.InvalidArgument);
        }

        string? username;
        string credential;

        try
        {
            using var httpClient = new HttpClient { BaseAddress = new Uri(CLOUDFLARE_TURN_BASE_URL), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
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
                return WriteResult(asJson,
                    new TurnResult(false, keyId, ttl, turnUrl, null, 0, 0,
                        $"The Cloudflare TURN API returned HTTP {(int)response.StatusCode}. {detail}".TrimEnd()),
                    ExitCodes.Failed);
            }

            var iceServers = await response.Content.ReadFromJsonAsync<CloudflareIceServersResponse>(cancellationToken: ct).ConfigureAwait(false);

            var server = iceServers?.IceServers.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Username));
            if (server == null)
            {
                return WriteResult(asJson,
                    new TurnResult(false, keyId, ttl, turnUrl, null, 0, 0,
                        "The Cloudflare TURN API response did not contain credentials."),
                    ExitCodes.Failed);
            }

            username = server.Username;
            credential = server.Credential;
            logger.LogDebug("Obtained Cloudflare TURN credentials for username {Username}.", username);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId, ttl, turnUrl, null, 0, 0, "Cancelled or the credentials request timed out."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId, ttl, turnUrl, null, 0, 0, excp.Message),
                ExitCodes.TransportError);
        }

        // Relay only ICE gather against the Cloudflare TURN server with the fetched credentials.
        var iceServer = new RTCIceServer
        {
            urls = turnUrl,
            username = username,
            credential = credential,
            credentialType = RTCIceCredentialType.password
        };

        var iceChannel = new RtpIceChannel(
            null,
            RTCIceComponent.rtp,
            new List<RTCIceServer> { iceServer },
            RTCIceTransportPolicy.relay,
            includeAllInterfaceAddresses: true);

        try
        {
            var gatheringComplete = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            iceChannel.OnIceGatheringStateChange += (state) =>
            {
                if (state == RTCIceGatheringState.complete)
                {
                    gatheringComplete.TrySetResult(true);
                }
            };

            iceChannel.OnIceCandidateError += (candidate, error) => logger.LogWarning("ICE candidate error: {Error}", error);

            var stopwatch = Stopwatch.StartNew();
            iceChannel.StartGathering();

            // A relay candidate can appear before gathering formally completes, so also resolve early
            // once at least one relay candidate is present.
            var relaySeen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            iceChannel.OnIceCandidate += (candidate) =>
            {
                if (candidate.type == RTCIceCandidateType.relay)
                {
                    relaySeen.TrySetResult(true);
                }
            };

            await Task.WhenAny(
                Task.WhenAny(gatheringComplete.Task, relaySeen.Task),
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            stopwatch.Stop();

            int relayCount = iceChannel.Candidates.Count(x => x.type == RTCIceCandidateType.relay);

            string? error = relayCount == 0
                ? "No relay candidate was allocated from the Cloudflare TURN server (credentials valid but relay failed, check egress to the TURN transport)."
                : null;

            return WriteResult(asJson,
                new TurnResult(error == null, keyId, ttl, turnUrl, username, relayCount, stopwatch.ElapsedMilliseconds, error),
                error == null ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new TurnResult(false, keyId, ttl, turnUrl, username, 0, 0, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            iceChannel.Close("turn probe complete");
        }
    }

    private static int WriteResult(bool asJson, TurnResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"Cloudflare TURN OK: obtained credentials for key {result.KeyId} and allocated " +
                $"{result.RelayCandidates} relay candidate(s) via {result.TurnUrl} in {result.GatheringMs}ms.");
        }
        else
        {
            Console.Error.WriteLine($"Cloudflare TURN check failed: {result.Error}");
        }

        return exitCode;
    }
}
