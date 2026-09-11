//-----------------------------------------------------------------------------
// Filename: LiveKitSinkNode.cs
//
// Description: A WebRTC egress edge for the "route" verb that publishes the graph's
// media into a LiveKit room. It is the route-graph counterpart of the "livekit room"
// verb: mint a publisher access token, open the room signalling web socket, register
// the tracks (AddTrack), bring up the publisher peer connection and then relay the
// graph's still-ENCODED H264 video and OPUS audio frames onto it (repacketise, not
// transcode) - exactly the WhipSinkNode send model, but over LiveKit's protobuf
// signalling instead of a single WHIP POST.
//
// This makes "route --from testpattern --to livekit:my-room --audio-codec opus"
// push a stream into a room a separate process (or a browser viewer) can subscribe
// to. Because the CLI holds the API key + secret it mints its own token locally, so
// the only shared config between a publisher and a subscriber is the room name.
//
// The tracks carry the graph's already-encoded frames via SendVideo/SendAudio
// (repacketise, not transcode) rather than generating their own media. LiveKit's
// room pipeline requires OPUS audio, so the edge requires --audio-codec opus.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Livekit.Server.Sdk.Dotnet;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands.Route;

public sealed class LiveKitSinkNode : ISinkNode
{
    private const int ADD_TRACK_TIMEOUT_SECONDS = 10;
    private const string VIDEO_CID = "cli-video";
    private const string AUDIO_CID = "cli-audio";

    private readonly string _url;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _room;
    private readonly string _identity;
    private readonly AudioFormat _audioFormat;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;

    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TrackInfo>> _pendingPublishes = new();
    private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RTCPeerConnection? _publisherPc;
    private RTCPeerConnection? _subscriberPc;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private volatile bool _ready;
    private string? _videoTrackSid;
    private string? _audioTrackSid;

    private int _framesSent;
    private long _bytesSent;
    private int _dropped;

    public LiveKitSinkNode(string url, string apiKey, string apiSecret, string room, AudioFormat audioFormat,
        int timeoutSeconds, ILogger logger)
    {
        _url = url;
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _room = room;
        _identity = $"cli-publisher-{Guid.NewGuid().ToString("N")[..8]}";
        _audioFormat = audioFormat;
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;
    }

    public string Describe() => $"livekit:{_room}";

    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        string jwt = new AccessToken(_apiKey, _apiSecret)
            .WithIdentity(_identity)
            .WithName(_identity)
            .WithGrants(new VideoGrants { RoomJoin = true, Room = _room })
            .ToJwt();

        _ws.Options.SetRequestHeader("Authorization", $"Bearer {jwt}");

        try
        {
            await _ws.ConnectAsync(new Uri($"{_url.TrimEnd('/')}/rtc?protocol=9&sdk=dotnet&auto_subscribe=false"), _cts.Token).ConfigureAwait(false);
        }
        catch (Exception excp)
        {
            throw new EdgeException($"The LiveKit web socket connection to {_url} failed: {excp.Message}");
        }

        if (_ws.State != WebSocketState.Open)
        {
            throw new EdgeException($"The LiveKit web socket to {_url} did not open (state {_ws.State}).");
        }

        _logger.LogDebug("LiveKit sink web socket connected, joining room {Room} as {Identity}.", _room, _identity);

        _receiveLoop = ReceiveLoopAsync(_cts.Token);

        var completed = await Task.WhenAny(_connected.Task, Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), _cts.Token)).ConfigureAwait(false);
        if (completed != _connected.Task || !await _connected.Task.ConfigureAwait(false))
        {
            throw new EdgeException(completed == _connected.Task
                ? $"The LiveKit publisher peer connection to room {_room} failed (state {_publisherPc?.connectionState})."
                : $"The LiveKit publisher peer connection to room {_room} did not reach connected within {_timeoutSeconds}s.");
        }

        _ready = true;
        _logger.LogDebug("LiveKit sink publishing to room {Room} (video {VideoSid}, audio {AudioSid}).", _room, _videoTrackSid, _audioTrackSid);
    }

    public void Write(MediaFrame frame)
    {
        if (!_ready || _publisherPc == null || frame.Payload.Length == 0)
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        try
        {
            if (frame.Kind == MediaKind.Audio)
            {
                _publisherPc.SendAudio(frame.DurationRtpUnits, frame.Payload);
            }
            else
            {
                _publisherPc.SendVideo(frame.DurationRtpUnits, frame.Payload);
            }

            Interlocked.Increment(ref _framesSent);
            Interlocked.Add(ref _bytesSent, frame.Payload.Length);
        }
        catch (Exception excp)
        {
            _logger.LogDebug("LiveKit sink send failed: {Error}", excp.Message);
            Interlocked.Increment(ref _dropped);
        }
    }

    public SinkStats GetStats() => new(_framesSent, Interlocked.Read(ref _bytesSent), _dropped);

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    if (result.Count > 0)
                    {
                        message.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Binary || message.Length == 0)
                {
                    continue;
                }

                message.Position = 0;
                var response = SignalResponse.Parser.ParseFrom(message);

                // Dispatch without awaiting so the loop keeps reading: the Join handler publishes tracks
                // and awaits the TrackPublished acknowledgement, which arrives on this same loop.
                _ = HandleSignalAsync(response, ct).ContinueWith(
                    t => _logger.LogError(t.Exception, "LiveKit sink signal handler failed."),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception excp)
        {
            _logger.LogDebug("LiveKit sink receive loop ended: {Error}", excp.Message);
        }
    }

    private async Task HandleSignalAsync(SignalResponse response, CancellationToken ct)
    {
        switch (response.MessageCase)
        {
            case SignalResponse.MessageOneofCase.Join:
                _logger.LogDebug("LiveKit sink Join response received, registering tracks.");
                await PublishTracksAsync(ct).ConfigureAwait(false);
                break;

            case SignalResponse.MessageOneofCase.TrackPublished:
                if (_pendingPublishes.TryRemove(response.TrackPublished.Cid, out var pending))
                {
                    pending.TrySetResult(response.TrackPublished.Track!);
                }
                break;

            case SignalResponse.MessageOneofCase.Answer:
                _publisherPc?.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.answer,
                    sdp = response.Answer.Sdp
                });
                break;

            case SignalResponse.MessageOneofCase.Offer:
                // LiveKit sends an initial offer for the subscriber transport. Answer it even though no
                // media is pulled, otherwise the connection does not complete.
                if (_subscriberPc == null)
                {
                    _subscriberPc = new RTCPeerConnection();
                    _subscriberPc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = response.Offer.Sdp });
                    var answer = _subscriberPc.createAnswer();
                    await _subscriberPc.setLocalDescription(answer).ConfigureAwait(false);
                    await SendAsync(new SignalRequest { Answer = new SessionDescription { Type = "answer", Sdp = answer.sdp } }, ct).ConfigureAwait(false);
                }
                break;

            case SignalResponse.MessageOneofCase.Trickle:
                var target = response.Trickle.Target switch
                {
                    SignalTarget.Publisher => _publisherPc,
                    SignalTarget.Subscriber => _subscriberPc,
                    _ => null
                };
                if (target != null && RTCIceCandidateInit.TryParse(response.Trickle.CandidateInit, out var candidate))
                {
                    target.addIceCandidate(candidate);
                }
                break;
        }
    }

    private async Task PublishTracksAsync(CancellationToken ct)
    {
        var videoTrack = await AddTrackAsync(new AddTrackRequest
        {
            Cid = VIDEO_CID,
            Name = "test-pattern",
            Type = TrackType.Video,
            Source = TrackSource.Camera,
            Width = 640,
            Height = 480
        }, ct).ConfigureAwait(false);
        _videoTrackSid = videoTrack.Sid;

        var audioTrack = await AddTrackAsync(new AddTrackRequest
        {
            Cid = AUDIO_CID,
            Name = "music",
            Type = TrackType.Audio,
            Source = TrackSource.Microphone
        }, ct).ConfigureAwait(false);
        _audioTrackSid = audioTrack.Sid;

        // The publisher peer connection carries the graph's encoded frames: send-only H264 video and the
        // (OPUS) audio the graph produces, relayed unchanged by Write (repacketise, not transcode).
        _publisherPc = new RTCPeerConnection();
        _publisherPc.addTrack(new MediaStreamTrack(new List<VideoFormat> { RouteVideoFormats.H264 }, MediaStreamStatusEnum.SendOnly));
        _publisherPc.addTrack(new MediaStreamTrack(new List<AudioFormat> { _audioFormat }, MediaStreamStatusEnum.SendOnly));

        _publisherPc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("LiveKit sink publisher peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.connected)
            {
                _connected.TrySetResult(true);
            }
            else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
            {
                _connected.TrySetResult(false);
            }
        };

        var offer = _publisherPc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
        await _publisherPc.setLocalDescription(offer).ConfigureAwait(false);

        _logger.LogDebug("LiveKit sink publisher offer SDP:\n{Sdp}", offer.sdp);

        await SendAsync(new SignalRequest { Offer = new SessionDescription { Type = "offer", Sdp = offer.sdp } }, ct).ConfigureAwait(false);
    }

    private async Task<TrackInfo> AddTrackAsync(AddTrackRequest request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<TrackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingPublishes[request.Cid] = tcs;

        await SendAsync(new SignalRequest { AddTrack = request }, ct).ConfigureAwait(false);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(ADD_TRACK_TIMEOUT_SECONDS), ct)).ConfigureAwait(false);
        if (completed != tcs.Task)
        {
            _pendingPublishes.TryRemove(request.Cid, out _);
            throw new EdgeException($"Timed out waiting for LiveKit to acknowledge the AddTrack request for cid {request.Cid}.");
        }

        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task SendAsync(SignalRequest request, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _ws.SendAsync(request.ToByteArray(), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ready = false;

        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                // Tell LiveKit the participant is leaving so it is removed immediately.
                await SendAsync(new SignalRequest
                {
                    Leave = new LeaveRequest
                    {
                        CanReconnect = false,
                        Reason = DisconnectReason.ClientInitiated,
                        Action = LeaveRequest.Types.Action.Disconnect
                    }
                }, CancellationToken.None).ConfigureAwait(false);

                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "route livekit sink disposed", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception excp)
        {
            _logger.LogDebug("LiveKit sink shutdown error: {Error}", excp.Message);
        }

        _cts?.Cancel();
        if (_receiveLoop != null)
        {
            try { await _receiveLoop.ConfigureAwait(false); } catch { /* best effort */ }
        }

        try { _publisherPc?.Close("route livekit sink disposed"); } catch { /* best effort */ }
        try { _subscriberPc?.Close("route livekit sink disposed"); } catch { /* best effort */ }
        _ws.Dispose();
        _cts?.Dispose();
    }
}
