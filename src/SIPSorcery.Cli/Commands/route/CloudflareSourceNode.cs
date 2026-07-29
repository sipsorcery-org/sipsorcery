//-----------------------------------------------------------------------------
// Filename: CloudflareSourceNode.cs
//
// Description: A live WebRTC ingress edge for the "route" verb that SUBSCRIBES to
// (pulls) tracks from a Cloudflare Realtime SFU session and emits the received media
// into the graph. It is the counterpart of CloudflareSinkNode: create our own
// session, ask the SFU to forward the publisher's remote tracks, answer the offer it
// returns, and fan each received frame into the graph as a MediaFrame (repacketise,
// not transcode), so:
//
//   route --from cloudflare:<sessionId> --to web --audio-codec opus
//
// pulls whatever a separate publisher (e.g. another CLI running
// "route --from testpattern --to cloudflare") is sending and serves it to a browser.
// The session id is printed by the publishing sink; the track names are the ones the
// sink publishes (cli-video / cli-audio).
//
// The Cloudflare "pull" flow (https://developers.cloudflare.com/realtime/sfu/https-api/):
//   POST sessions/new                         -> { sessionId } (our own receiving session)
//   POST sessions/{id}/tracks/new (remote)    -> { requiresImmediateRenegotiation, sessionDescription: offer }
//   PUT  sessions/{id}/renegotiate            (our answer to that offer)
//
// The receive side mirrors LiveKitSourceNode: recv-only tracks offering the codecs the
// library can negotiate, apply the offer and answer it; each frame is re-timed from its
// RTP timestamp the same way.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
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

public sealed class CloudflareSourceNode : ISourceNode
{
    private const string CLOUDFLARE_SFU_BASE_URL = "https://rtc.live.cloudflare.com/v1/apps/";
    private const string STUN_URL = "stun:stun.cloudflare.com";
    private static readonly string[] PULL_TRACK_NAMES = { "cli-video", "cli-audio" };

    private readonly string _appId;
    private readonly string _token;
    private readonly string _publisherSessionId;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;

    private readonly HttpClient _httpClient;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RTCPeerConnection? _pc;
    private string? _sessionId;
    private long? _connectTimeMs;

    // Received RTP timestamps are absolute; transport sinks want a per-frame duration (delta), so the
    // previous video timestamp is tracked to derive it. Audio carries its own millisecond duration.
    private uint _lastVideoTimestamp;
    private bool _haveVideoTimestamp;
    private uint _audioTimestamp;

    public event Action<MediaFrame>? OnFrame;

    public Task Completion => _completion.Task;

    public long? ConnectTimeMs => _connectTimeMs;

    public CloudflareSourceNode(string appId, string token, string publisherSessionId, int timeoutSeconds, ILogger logger)
    {
        _appId = appId;
        _token = token;
        _publisherSessionId = publisherSessionId;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;

        _httpClient = new HttpClient { BaseAddress = new Uri(CLOUDFLARE_SFU_BASE_URL), Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public string Describe() => $"cloudflare:{_publisherSessionId}";

    public async Task StartAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. Create our own receiving session.
        try
        {
            using var newSession = await _httpClient.PostAsync($"{_appId}/sessions/new", null, ct).ConfigureAwait(false);
            string body = await newSession.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!newSession.IsSuccessStatusCode)
            {
                throw new EdgeException($"Creating the Cloudflare SFU receive session failed with HTTP {(int)newSession.StatusCode}. {Truncate(body)}".TrimEnd());
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

        // 2. Build the receiving peer connection: recv-only tracks offering the codecs we can negotiate.
        var config = new RTCConfiguration { iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } } };
        var pc = new RTCPeerConnection(config);
        _pc = pc;
        pc.addTrack(new MediaStreamTrack(new List<AudioFormat>
        {
            AudioCommonlyUsedFormats.OpusWebRTC,
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)
        }, MediaStreamStatusEnum.RecvOnly));
        pc.addTrack(new MediaStreamTrack(RouteVideoFormats.All(), MediaStreamStatusEnum.RecvOnly));

        WireReceive(pc);

        var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("Cloudflare source peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                connected.TrySetResult(true);
            }
            else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed ||
                     state == RTCPeerConnectionState.disconnected)
            {
                connected.TrySetResult(false);
                _completion.TrySetResult();
            }
        };

        // 3. Ask the SFU to forward the publisher's tracks. With remote tracks (no offer in the request)
        // the SFU returns an OFFER for us to answer.
        var pullTracks = new List<object>();
        foreach (var name in PULL_TRACK_NAMES)
        {
            pullTracks.Add(new { location = "remote", sessionId = _publisherSessionId, trackName = name });
        }

        string offerSdp;
        try
        {
            using var tracksResponse = await PostJsonAsync($"{_appId}/sessions/{_sessionId}/tracks/new",
                new { tracks = pullTracks }, ct).ConfigureAwait(false);
            string body = await tracksResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!tracksResponse.IsSuccessStatusCode)
            {
                throw new EdgeException($"Pulling tracks from Cloudflare session {_publisherSessionId} failed with HTTP {(int)tracksResponse.StatusCode}. {Truncate(body)}".TrimEnd());
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("sessionDescription", out var sdpEl) ||
                sdpEl.GetProperty("sdp").GetString() is not string sdp || string.IsNullOrWhiteSpace(sdp))
            {
                throw new EdgeException($"Cloudflare did not return an offer for session {_publisherSessionId}; is the session id correct and still publishing (tracks {string.Join("/", PULL_TRACK_NAMES)})?");
            }
            offerSdp = sdp;
        }
        catch (EdgeException)
        {
            throw;
        }
        catch (Exception excp)
        {
            throw new EdgeException($"Pulling tracks from the Cloudflare SFU failed: {excp.Message}");
        }

        // 4. Answer the SFU's offer and complete the renegotiation.
        var setOffer = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offerSdp });
        if (setOffer != SetDescriptionResultEnum.OK)
        {
            throw new EdgeException($"The SDP offer from Cloudflare could not be applied: {setOffer}.");
        }

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer).ConfigureAwait(false);

        _logger.LogDebug("Cloudflare source answer SDP:\n{Sdp}", answer.sdp);

        try
        {
            using var renegotiate = await PutJsonAsync($"{_appId}/sessions/{_sessionId}/renegotiate",
                new { sessionDescription = new { type = "answer", sdp = answer.sdp } }, ct).ConfigureAwait(false);
            if (!renegotiate.IsSuccessStatusCode)
            {
                string body = await renegotiate.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new EdgeException($"The Cloudflare renegotiate (answer) failed with HTTP {(int)renegotiate.StatusCode}. {Truncate(body)}".TrimEnd());
            }
        }
        catch (EdgeException)
        {
            throw;
        }
        catch (Exception excp)
        {
            throw new EdgeException($"The Cloudflare renegotiate (answer) failed: {excp.Message}");
        }

        // 5. Wait for the receiving connection to come up; media then flows from the publisher.
        var completed = await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), ct)).ConfigureAwait(false);
        if (completed != connected.Task || !await connected.Task.ConfigureAwait(false))
        {
            throw new EdgeException(completed == connected.Task
                ? $"The Cloudflare SFU receive connection failed (state {pc.connectionState})."
                : $"The Cloudflare SFU receive connection did not reach connected within {_timeoutSeconds}s.");
        }

        _connectTimeMs = stopwatch.ElapsedMilliseconds;
        _logger.LogDebug("Cloudflare source connected in {Ms}ms; receiving media from session {SessionId}.", _connectTimeMs, _publisherSessionId);
    }

    private void WireReceive(RTCPeerConnection pc)
    {
        pc.OnVideoFrameReceived += (_, timestamp, frame, format) =>
        {
            // Derive the per-frame duration from the gap between successive RTP timestamps so a transport
            // sink re-times the outgoing track correctly (the first frame has no predecessor, so 0).
            uint duration = _haveVideoTimestamp ? timestamp - _lastVideoTimestamp : 0;
            _lastVideoTimestamp = timestamp;
            _haveVideoTimestamp = true;
            OnFrame?.Invoke(MediaFrame.ForVideo(frame.ToArray(), timestamp, format, duration));
        };

        pc.OnAudioFrameReceived += (encodedFrame) =>
        {
            uint durationRtpUnits = (uint)((long)encodedFrame.DurationMilliSeconds * encodedFrame.AudioFormat.RtpClockRate / 1000);
            _audioTimestamp += durationRtpUnits;
            OnFrame?.Invoke(MediaFrame.ForAudio(encodedFrame.EncodedAudio.ToArray(), _audioTimestamp, durationRtpUnits, encodedFrame.AudioFormat));
        };
    }

    private Task<HttpResponseMessage> PostJsonAsync(string url, object body, CancellationToken ct) =>
        _httpClient.PostAsync(url, JsonContent(body), ct);

    private Task<HttpResponseMessage> PutJsonAsync(string url, object body, CancellationToken ct) =>
        _httpClient.PutAsync(url, JsonContent(body), ct);

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static string Truncate(string body) => body.Length > 200 ? body[..200] : body;

    public ValueTask DisposeAsync()
    {
        _completion.TrySetResult();
        try { _pc?.Close("route cloudflare source disposed"); } catch { /* best effort */ }
        _httpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
