//-----------------------------------------------------------------------------
// Filename: LiveKitSourceNode.cs
//
// Description: A live WebRTC ingress edge for the "route" verb that SUBSCRIBES to a
// LiveKit room and emits the received media into the graph. It is the counterpart of
// LiveKitSinkNode: mint a subscribe-only access token, open the room signalling web
// socket with auto_subscribe, and answer the subscriber-transport offer LiveKit sends
// for the published tracks. Each received, depacketised video/audio frame is fanned
// into the graph as a MediaFrame (repacketise, not transcode), so:
//
//   route --from livekit:my-room --to web --audio-codec opus
//
// pulls whatever a separate publisher (e.g. another CLI running
// "route --from testpattern --to livekit:my-room") is sending and serves it to a
// browser, with no bespoke command.
//
// The receive side is the answerer-with-recvonly-tracks pattern proven by the
// diagnostics "webrtc whip-server" verb: add recv-only tracks offering the codecs the
// library can negotiate, apply LiveKit's offer and answer it. Because the CLI holds
// the API key + secret it mints its own subscribe token locally, so the only shared
// config with the publisher is the room name.
//
// Ordering note: LiveKit includes a published track in the subscriber offer once it
// exists, so starting the publisher first (then this subscriber) is the simplest
// path. A publisher that joins later triggers a renegotiation offer, handled best
// effort on the existing peer connection.
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
using System.Collections.Generic;
using System.Diagnostics;
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

public sealed class LiveKitSourceNode : ISourceNode
{
    private readonly string _url;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _room;
    private readonly string _identity;
    private readonly int _timeoutSeconds;
    private readonly ILogger _logger;

    private readonly ClientWebSocket _ws = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly TaskCompletionSource<bool> _joined = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private RTCPeerConnection? _subscriberPc;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private long? _connectTimeMs;

    // Received RTP timestamps are absolute; transport sinks want a per-frame duration (delta), so the
    // previous video timestamp is tracked to derive it. Audio carries its own millisecond duration.
    private uint _lastVideoTimestamp;
    private bool _haveVideoTimestamp;
    private uint _audioTimestamp;

    public event Action<MediaFrame>? OnFrame;

    public Task Completion => _completion.Task;

    public long? ConnectTimeMs => _connectTimeMs;

    public LiveKitSourceNode(string url, string apiKey, string apiSecret, string room, int timeoutSeconds, ILogger logger)
    {
        _url = url;
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        _room = room;
        _identity = $"cli-subscriber-{Guid.NewGuid().ToString("N")[..8]}";
        _timeoutSeconds = timeoutSeconds;
        _logger = logger;
    }

    public string Describe() => $"livekit:{_room}";

    public async Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        // A subscribe-only grant: join the room and receive, but never publish.
        string jwt = new AccessToken(_apiKey, _apiSecret)
            .WithIdentity(_identity)
            .WithName(_identity)
            .WithGrants(new VideoGrants { RoomJoin = true, Room = _room, CanSubscribe = true, CanPublish = false })
            .ToJwt();

        _ws.Options.SetRequestHeader("Authorization", $"Bearer {jwt}");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // auto_subscribe=true so LiveKit subscribes us to the room's published tracks and offers them
            // on the subscriber transport without an explicit UpdateSubscription round trip.
            await _ws.ConnectAsync(new Uri($"{_url.TrimEnd('/')}/rtc?protocol=9&sdk=dotnet&auto_subscribe=true"), _cts.Token).ConfigureAwait(false);
        }
        catch (Exception excp)
        {
            throw new EdgeException($"The LiveKit web socket connection to {_url} failed: {excp.Message}");
        }

        if (_ws.State != WebSocketState.Open)
        {
            throw new EdgeException($"The LiveKit web socket to {_url} did not open (state {_ws.State}).");
        }

        _logger.LogDebug("LiveKit source web socket connected, joining room {Room} as {Identity}.", _room, _identity);

        _receiveLoop = ReceiveLoopAsync(_cts.Token);

        // "Started" = joined the room and listening for tracks. Media flows whenever a publisher is (or
        // becomes) present; the run is bounded by --duration / cancellation like any source.
        var completed = await Task.WhenAny(_joined.Task, Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), _cts.Token)).ConfigureAwait(false);
        if (completed != _joined.Task || !await _joined.Task.ConfigureAwait(false))
        {
            throw new EdgeException($"Did not receive a LiveKit Join response for room {_room} within {_timeoutSeconds}s (is the URL/token correct?).");
        }

        _connectTimeMs = stopwatch.ElapsedMilliseconds;
        _logger.LogDebug("LiveKit source joined room {Room} in {Ms}ms; waiting for published media.", _room, _connectTimeMs);
    }

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
                        _completion.TrySetResult();
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

                _ = HandleSignalAsync(response, ct).ContinueWith(
                    t => _logger.LogError(t.Exception, "LiveKit source signal handler failed."),
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (Exception excp)
        {
            _logger.LogDebug("LiveKit source receive loop ended: {Error}", excp.Message);
        }
        finally
        {
            _completion.TrySetResult();
        }
    }

    private async Task HandleSignalAsync(SignalResponse response, CancellationToken ct)
    {
        switch (response.MessageCase)
        {
            case SignalResponse.MessageOneofCase.Join:
                _logger.LogDebug("LiveKit source Join response received for room {Room}.", _room);
                _joined.TrySetResult(true);
                break;

            case SignalResponse.MessageOneofCase.Offer:
                // LiveKit offers the subscribed tracks on the subscriber transport. Answer it, creating
                // the receiving peer connection on the first offer and renegotiating on later ones.
                await HandleSubscriberOfferAsync(response.Offer.Sdp, ct).ConfigureAwait(false);
                break;

            case SignalResponse.MessageOneofCase.Trickle:
                if (response.Trickle.Target == SignalTarget.Subscriber && _subscriberPc != null &&
                    RTCIceCandidateInit.TryParse(response.Trickle.CandidateInit, out var candidate))
                {
                    _subscriberPc.addIceCandidate(candidate);
                }
                break;

            case SignalResponse.MessageOneofCase.Leave:
                _logger.LogDebug("LiveKit source received Leave for room {Room}.", _room);
                _completion.TrySetResult();
                break;
        }
    }

    private async Task HandleSubscriberOfferAsync(string offerSdp, CancellationToken ct)
    {
        if (_subscriberPc == null)
        {
            _subscriberPc = CreateSubscriberPeerConnection();
        }

        var setResult = _subscriberPc.setRemoteDescription(new RTCSessionDescriptionInit
        {
            type = RTCSdpType.offer,
            sdp = offerSdp
        });

        if (setResult != SetDescriptionResultEnum.OK)
        {
            _logger.LogWarning("LiveKit source could not apply the subscriber offer: {Result}.", setResult);
            return;
        }

        var answer = _subscriberPc.createAnswer();
        await _subscriberPc.setLocalDescription(answer).ConfigureAwait(false);

        _logger.LogDebug("LiveKit source answer SDP to subscriber transport:\n{Sdp}", answer.sdp);

        await SendAsync(new SignalRequest { Answer = new SessionDescription { Type = "answer", Sdp = answer.sdp } }, ct).ConfigureAwait(false);
    }

    private RTCPeerConnection CreateSubscriberPeerConnection()
    {
        var pc = new RTCPeerConnection();

        // Recv-only tracks offering every codec the library can negotiate, so the answer matches whatever
        // LiveKit forwards (we publish H264 + OPUS from the sink, but accept broadly).
        pc.addTrack(new MediaStreamTrack(new List<AudioFormat>
        {
            AudioCommonlyUsedFormats.OpusWebRTC,
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA)
        }, MediaStreamStatusEnum.RecvOnly));

        pc.addTrack(new MediaStreamTrack(RouteVideoFormats.All(), MediaStreamStatusEnum.RecvOnly));

        pc.OnVideoFrameReceived += (_, timestamp, frame, format) =>
        {
            // Derive the per-frame duration from the gap between successive RTP timestamps so a transport
            // sink re-times the outgoing track correctly (the first frame has no predecessor, so 0).
            uint duration = _haveVideoTimestamp ? timestamp - _lastVideoTimestamp : 0;
            _lastVideoTimestamp = timestamp;
            _haveVideoTimestamp = true;
            OnFrame?.Invoke(MediaFrame.ForVideo(frame, timestamp, format, duration));
        };

        pc.OnAudioFrameReceived += (encodedFrame) =>
        {
            // Convert the frame's millisecond duration to RTP clock units for the outgoing track.
            uint durationRtpUnits = (uint)((long)encodedFrame.DurationMilliSeconds * encodedFrame.AudioFormat.RtpClockRate / 1000);
            _audioTimestamp += durationRtpUnits;
            OnFrame?.Invoke(MediaFrame.ForAudio(encodedFrame.EncodedAudio, _audioTimestamp, durationRtpUnits, encodedFrame.AudioFormat));
        };

        pc.onconnectionstatechange += (state) =>
        {
            _logger.LogDebug("LiveKit source subscriber peer connection state changed to {State}.", state);
            if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed ||
                state == RTCPeerConnectionState.disconnected)
            {
                // A dropped subscriber transport ends the source.
                _completion.TrySetResult();
            }
        };

        return pc;
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
        try
        {
            if (_ws.State == WebSocketState.Open)
            {
                await SendAsync(new SignalRequest
                {
                    Leave = new LeaveRequest
                    {
                        CanReconnect = false,
                        Reason = DisconnectReason.ClientInitiated,
                        Action = LeaveRequest.Types.Action.Disconnect
                    }
                }, CancellationToken.None).ConfigureAwait(false);

                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "route livekit source disposed", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception excp)
        {
            _logger.LogDebug("LiveKit source shutdown error: {Error}", excp.Message);
        }

        _cts?.Cancel();
        if (_receiveLoop != null)
        {
            try { await _receiveLoop.ConfigureAwait(false); } catch { /* best effort */ }
        }

        try { _subscriberPc?.Close("route livekit source disposed"); } catch { /* best effort */ }
        _ws.Dispose();
        _cts?.Dispose();
        _completion.TrySetResult();
    }
}
