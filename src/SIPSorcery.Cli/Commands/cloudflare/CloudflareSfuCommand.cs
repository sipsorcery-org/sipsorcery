//-----------------------------------------------------------------------------
// Filename: CloudflareSfuCommand.cs
//
// Description: The "sipsorcery cloudflare sfu" verb. Creates a Cloudflare
// Realtime SFU session, publishes a VP8 test pattern and OPUS music track to it
// over a full WebRTC connection, and reports whether the publisher peer
// connection connects and media flows up to the SFU. Answers "is my Cloudflare
// SFU app valid and can I push media to it from here".
//
// The Cloudflare SFU HTTP API is simple enough to call directly (the example
// project uses a generated Kiota client; here raw HTTP keeps the CLI lean):
//   POST apps/{appId}/sessions/new                 -> { sessionId }
//   POST apps/{appId}/sessions/{id}/tracks/new     -> { sessionDescription }
//   PUT  apps/{appId}/sessions/{id}/tracks/close   (teardown)
//
// See: https://developers.cloudflare.com/realtime/sfu/https-api/
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
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

public sealed class CloudflareSfuCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 15;
    private const int DEFAULT_MEDIA_DURATION_SECONDS = 5;
    private const string CLOUDFLARE_SFU_BASE_URL = "https://rtc.live.cloudflare.com/v1/apps/";
    private const string STUN_URL = "stun:stun.cloudflare.com";

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record SfuResult(
        bool Success,
        string AppId,
        string? SessionId,
        string ConnectionState,
        long? ConnectTimeMs,
        int? MediaDurationMs,
        string? Error);

    public CloudflareSfuCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var appIdOption = new Option<string?>("--app-id")
        {
            Description = "The Cloudflare Realtime SFU app ID. Defaults to the CLOUDFLARE_APPID environment variable."
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "The Cloudflare Realtime SFU app API token. Defaults to the CLOUDFLARE_API_TOKEN environment variable."
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to keep publishing after the connection is established.",
            DefaultValueFactory = _ => DEFAULT_MEDIA_DURATION_SECONDS
        };

        var command = new Command("sfu", "Publish a test pattern to a Cloudflare Realtime SFU app and verify the connection.");
        command.Options.Add(appIdOption);
        command.Options.Add(tokenOption);
        command.Options.Add(durationOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(appIdOption),
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string? appId, string? token, int durationSeconds,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(CloudflareSfuCommand));

        appId ??= Environment.GetEnvironmentVariable("CLOUDFLARE_APPID");
        token ??= Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");

        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(token))
        {
            return WriteResult(asJson,
                new SfuResult(false, appId ?? string.Empty, null, "new", null, null,
                    "An app ID and API token are required (--app-id/--token or CLOUDFLARE_APPID/CLOUDFLARE_API_TOKEN)."),
                ExitCodes.InvalidArgument);
        }

        using var httpClient = new HttpClient { BaseAddress = new Uri(CLOUDFLARE_SFU_BASE_URL), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var config = new RTCConfiguration { iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } } };
        var pc = new RTCPeerConnection(config);
        using var media = new MediaTestSource(opusOnly: false, logger);

        string? sessionId = null;

        try
        {
            // 1. Create the publisher session.
            using (var newSessionResponse = await httpClient.PostAsync($"{appId}/sessions/new", null, ct).ConfigureAwait(false))
            {
                if (!newSessionResponse.IsSuccessStatusCode)
                {
                    string body = await newSessionResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return WriteResult(asJson,
                        new SfuResult(false, appId, null, "new", null, null,
                            $"Creating the SFU session failed with HTTP {(int)newSessionResponse.StatusCode}. {Truncate(body)}".TrimEnd()),
                        ExitCodes.Failed);
                }

                using var sessionDoc = JsonDocument.Parse(await newSessionResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                sessionId = sessionDoc.RootElement.GetProperty("sessionId").GetString();
            }

            logger.LogDebug("Created Cloudflare SFU session {SessionId}.", sessionId);

            // 2. Build the publisher peer connection and its offer.
            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug("Publisher peer connection state changed to {State}.", state);
                if (state == RTCPeerConnectionState.connected)
                {
                    await media.StartAsync().ConfigureAwait(false);
                    connected.TrySetResult(true);
                }
                else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
                {
                    connected.TrySetResult(false);
                }
            };

            media.AddTracks(pc, MediaStreamStatusEnum.SendRecv);

            var offer = pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
            await pc.setLocalDescription(offer).ConfigureAwait(false);

            // 3. Push the local tracks. The track mids are taken from the generated offer so the
            // ordering the library produces (rather than a hardcoded guess) is always honoured.
            var parsedOffer = SDP.ParseSDPDescription(offer.sdp);
            var tracks = parsedOffer.Media.Select(m => new
            {
                location = "local",
                mid = m.MediaID,
                trackName = $"cli-{m.Media}",
                kind = m.Media.ToString()
            }).ToList();

            var stopwatch = Stopwatch.StartNew();

            using (var tracksResponse = await PostJsonAsync(httpClient, $"{appId}/sessions/{sessionId}/tracks/new",
                new { sessionDescription = new { type = "offer", sdp = offer.sdp }, tracks }, ct).ConfigureAwait(false))
            {
                if (!tracksResponse.IsSuccessStatusCode)
                {
                    string body = await tracksResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    return WriteResult(asJson,
                        new SfuResult(false, appId, sessionId, pc.connectionState.ToString(), null, null,
                            $"Pushing tracks failed with HTTP {(int)tracksResponse.StatusCode}. {Truncate(body)}".TrimEnd()),
                        ExitCodes.Failed);
                }

                using var tracksDoc = JsonDocument.Parse(await tracksResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                string? answerSdp = tracksDoc.RootElement.GetProperty("sessionDescription").GetProperty("sdp").GetString();

                var setAnswerResult = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = answerSdp });
                if (setAnswerResult != SetDescriptionResultEnum.OK)
                {
                    return WriteResult(asJson,
                        new SfuResult(false, appId, sessionId, pc.connectionState.ToString(), null, null,
                            $"The SDP answer from Cloudflare could not be applied: {setAnswerResult}."),
                        ExitCodes.Failed);
                }
            }

            // 4. Wait for the publisher connection to come up.
            var completed = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            if (completed != connected.Task || !await connected.Task.ConfigureAwait(false))
            {
                return WriteResult(asJson,
                    new SfuResult(false, appId, sessionId, pc.connectionState.ToString(), stopwatch.ElapsedMilliseconds, null,
                        completed == connected.Task
                            ? $"The publisher peer connection failed (state {pc.connectionState})."
                            : $"The publisher peer connection did not reach connected within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            long connectTimeMs = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Publisher connected in {ConnectTimeMs}ms, publishing for {Duration}s.", connectTimeMs, durationSeconds);

            await Task.Delay(TimeSpan.FromSeconds(durationSeconds), ct).ConfigureAwait(false);

            return WriteResult(asJson,
                new SfuResult(true, appId, sessionId, pc.connectionState.ToString(), connectTimeMs, durationSeconds * 1000, null),
                ExitCodes.Ok);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson,
                new SfuResult(false, appId, sessionId, pc.connectionState.ToString(), null, null, "Cancelled or a request timed out."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new SfuResult(false, appId, sessionId, pc.connectionState.ToString(), null, null, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            await CloseSessionTracksAsync(httpClient, appId, sessionId, pc, logger).ConfigureAwait(false);
            pc.Close("sfu probe complete");
        }
    }

    /// <summary>
    /// Closes the published tracks so Cloudflare reclaims the session promptly. The SFU API has no
    /// explicit delete session call; force closing the tracks stops the media without needing a
    /// WebRTC renegotiation.
    /// </summary>
    private static async Task CloseSessionTracksAsync(HttpClient httpClient, string appId, string? sessionId, RTCPeerConnection pc, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(sessionId) || pc.localDescription?.sdp == null)
        {
            return;
        }

        try
        {
            // pc.localDescription.sdp is an already-parsed SDP object, so the media can be read directly.
            var mids = pc.localDescription.sdp.Media
                .Where(m => !string.IsNullOrWhiteSpace(m.MediaID))
                .Select(m => new { mid = m.MediaID })
                .ToList();

            if (mids.Count == 0)
            {
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Put, $"{appId}/sessions/{sessionId}/tracks/close")
            {
                Content = JsonContent(new { tracks = mids, force = true })
            };
            await httpClient.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception excp)
        {
            logger.LogDebug("Failed to close Cloudflare SFU session tracks: {Error}", excp.Message);
        }
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient httpClient, string url, object body, CancellationToken ct) =>
        httpClient.PostAsync(url, JsonContent(body), ct);

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static string Truncate(string body) => body.Length > 200 ? body[..200] : body;

    private static int WriteResult(bool asJson, SfuResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"Cloudflare SFU OK: published to session {result.SessionId} (app {result.AppId}), " +
                $"connected in {result.ConnectTimeMs}ms and held for {result.MediaDurationMs}ms.");
        }
        else
        {
            Console.Error.WriteLine($"Cloudflare SFU check failed: {result.Error}");
        }

        return exitCode;
    }
}
