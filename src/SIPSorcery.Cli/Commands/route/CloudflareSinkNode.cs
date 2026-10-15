//-----------------------------------------------------------------------------
// Filename: CloudflareSinkNode.cs
//
// Description: A WebRTC egress edge for the "route" verb that publishes the graph's
// media into a Cloudflare Realtime SFU session. It is the route-graph counterpart
// of the (now retired) "cloudflare sfu" verb: create a session, push send tracks and
// relay the graph's still-ENCODED H264 video and audio frames onto them (repacketise,
// not transcode) - the same WhipSinkNode/LiveKitSinkNode send model, over Cloudflare's
// simple HTTP API instead of a WHIP POST or LiveKit's web socket.
//
// The Cloudflare SFU HTTP API (https://developers.cloudflare.com/realtime/sfu/https-api/):
//   POST apps/{appId}/sessions/new              -> { sessionId }
//   POST apps/{appId}/sessions/{id}/tracks/new  -> { sessionDescription } (the answer)
//   PUT  apps/{appId}/sessions/{id}/tracks/close (teardown)
//
// Unlike a LiveKit room there is no shared name: Cloudflare allocates the session id,
// so the sink prints it plus the published track names - a subscriber pulls the media
// by (sessionId, trackName).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class CloudflareSinkNode : ISinkNode
{
    private const string CLOUDFLARE_SFU_BASE_URL = "https://rtc.live.cloudflare.com/v1/apps/";
    private const string STUN_URL = "stun:stun.cloudflare.com";

    private readonly string _appId;
    private readonly string _token;
    private readonly AudioFormat _audioFormat;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;

    private readonly HttpClient _httpClient;
    private RTCPeerConnection? _pc;
    private string? _sessionId;
    private volatile bool _ready;

    private int _framesSent;
    private long _bytesSent;
    private int _dropped;

    public CloudflareSinkNode(string appId, string token, AudioFormat audioFormat, int timeoutSeconds, ILogger logger)
    {
        _appId = appId;
        _token = token;
        _audioFormat = audioFormat;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;

        _httpClient = new HttpClient { BaseAddress = new Uri(CLOUDFLARE_SFU_BASE_URL), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public string Describe() => _sessionId != null ? $"cloudflare:{_sessionId}" : "cloudflare";

    public async Task StartAsync(CancellationToken ct)
    {
        // 1. Create the publisher session.
        try
        {
            using var newSession = await _httpClient.PostAsync($"{_appId}/sessions/new", null, ct).ConfigureAwait(false);
            string body = await newSession.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!newSession.IsSuccessStatusCode)
            {
                throw new EdgeException($"Creating the Cloudflare SFU session failed with HTTP {(int)newSession.StatusCode}. {Truncate(body)}".TrimEnd());
            }
            using var sessionDoc = JsonDocument.Parse(body);
            _sessionId = sessionDoc.RootElement.GetProperty("sessionId").GetString();
        }
        catch (EdgeException)
        {
            throw;
        }
        catch (Exception excp)
        {
            throw new EdgeException($"Could not reach the Cloudflare SFU API: {excp.Message}");
        }

        _logger.LogDebug("Created Cloudflare SFU session {SessionId}.", _sessionId);

        // 2. Build the publisher peer connection: send-only H264 video + the graph's audio, relayed by Write.
        var config = new RTCConfiguration { iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } } };
        var pc = new RTCPeerConnection(config);
        _pc = pc;
        pc.addTrack(new MediaStreamTrack(new List<VideoFormat> { RouteVideoFormats.H264 }, MediaStreamStatusEnum.SendOnly));
        pc.addTrack(new MediaStreamTrack(new List<AudioFormat> { _audioFormat }, MediaStreamStatusEnum.SendOnly));

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("Cloudflare sink peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                connected.TrySetResult(true);
            }
            else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
            {
                connected.TrySetResult(false);
            }
        };

        var offer = pc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
        await pc.setLocalDescription(offer).ConfigureAwait(false);

        _logger.LogDebug("Cloudflare sink offer SDP:\n{Sdp}", offer.sdp);

        // 3. Push the local tracks. The mids come from the generated offer so the library's ordering is
        // honoured; the track names are what a subscriber pulls the media by.
        var parsedOffer = SDP.ParseSDPDescription(offer.sdp);
        var tracks = parsedOffer.Media.Select(m => new
        {
            location = "local",
            mid = m.MediaID,
            trackName = $"cli-{m.Media}",
            kind = m.Media.ToString()
        }).ToList();

        try
        {
            using var tracksResponse = await PostJsonAsync($"{_appId}/sessions/{_sessionId}/tracks/new",
                new { sessionDescription = new { type = "offer", sdp = offer.sdp }, tracks }, ct).ConfigureAwait(false);
            string body = await tracksResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!tracksResponse.IsSuccessStatusCode)
            {
                throw new EdgeException($"Pushing tracks to the Cloudflare SFU failed with HTTP {(int)tracksResponse.StatusCode}. {Truncate(body)}".TrimEnd());
            }

            using var tracksDoc = JsonDocument.Parse(body);
            string? answerSdp = tracksDoc.RootElement.GetProperty("sessionDescription").GetProperty("sdp").GetString();
            var setAnswer = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = answerSdp });
            if (setAnswer != SetDescriptionResultEnum.OK)
            {
                throw new EdgeException($"The SDP answer from Cloudflare could not be applied: {setAnswer}.");
            }
        }
        catch (EdgeException)
        {
            throw;
        }
        catch (Exception excp)
        {
            throw new EdgeException($"Pushing tracks to the Cloudflare SFU failed: {excp.Message}");
        }

        // 4. Wait for the publisher connection to come up.
        var completed = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), ct)).ConfigureAwait(false);
        if (completed != connected.Task || !await connected.Task.ConfigureAwait(false))
        {
            throw new EdgeException(completed == connected.Task
                ? $"The Cloudflare SFU publisher peer connection failed (state {pc.connectionState})."
                : $"The Cloudflare SFU publisher peer connection did not reach connected within {_timeoutSeconds}s.");
        }

        _ready = true;
        string trackNames = string.Join(", ", tracks.Select(t => t.trackName));
        Console.Error.WriteLine($"Publishing to Cloudflare SFU session {_sessionId} (tracks: {trackNames}). Subscribe with the session id + track name.");
    }

    public void Write(MediaFrame frame)
    {
        if (!_ready || _pc == null || frame.Payload.Length == 0)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        try
        {
            if (frame.Kind == MediaKind.Audio)
            {
                _pc.SendAudio(frame.DurationRtpUnits, frame.Payload);
            }
            else
            {
                _pc.SendVideo(frame.DurationRtpUnits, frame.Payload);
            }

            Interlocked.Increment(ref _framesSent);
            Interlocked.Add(ref _bytesSent, frame.Payload.Length);
        }
        catch (Exception excp)
        {
            _logger.LogDebug("Cloudflare sink send failed: {Error}", excp.Message);
            Interlocked.Increment(ref _dropped);
        }
    }

    public SinkStats GetStats() => new(_framesSent, Interlocked.Read(ref _bytesSent), _dropped);

    private Task<HttpResponseMessage> PostJsonAsync(string url, object body, CancellationToken ct) =>
        _httpClient.PostAsync(url, JsonContent(body), ct);

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static string Truncate(string body) => body.Length > 200 ? body[..200] : body;

    public async ValueTask DisposeAsync()
    {
        _ready = false;

        // Force-close the published tracks so Cloudflare reclaims the session promptly (the API has no
        // explicit delete-session call).
        var pc = _pc;
        if (_sessionId != null && pc?.localDescription?.sdp != null)
        {
            try
            {
                var mids = pc.localDescription.sdp.Media
                    .Where(m => !string.IsNullOrWhiteSpace(m.MediaID))
                    .Select(m => new { mid = m.MediaID })
                    .ToList();

                if (mids.Count > 0)
                {
                    using var request = new HttpRequestMessage(HttpMethod.Put, $"{_appId}/sessions/{_sessionId}/tracks/close")
                    {
                        Content = JsonContent(new { tracks = mids, force = true })
                    };
                    await _httpClient.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Failed to close Cloudflare SFU session tracks: {Error}", excp.Message);
            }
        }

        try { pc?.Close("route cloudflare sink disposed"); } catch { /* best effort */ }
        _httpClient.Dispose();
    }
}
