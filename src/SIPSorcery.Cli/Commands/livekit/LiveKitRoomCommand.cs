//-----------------------------------------------------------------------------
// Filename: LiveKitRoomCommand.cs
//
// Description: The "sipsorcery livekit room" verb. Mints a LiveKit access token,
// connects to the room signalling web socket, joins as a publisher and pushes a
// VP8 test pattern and OPUS music track over a full WebRTC connection. Reports
// whether the publisher peer connection connects. Answers "are my LiveKit
// credentials valid and can I publish media to a room from here".
//
// LiveKit signalling is protobuf over a web socket. The publisher is server
// negotiated: after the Join response the tracks are registered with AddTrack
// requests, then the publisher peer connection's offer is sent and LiveKit
// answers. A subscriber peer connection is also created to answer LiveKit's
// initial offer, but no media is pulled (auto_subscribe=false).
//
// See: https://docs.livekit.io/
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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using Google.Protobuf;
using Livekit.Server.Sdk.Dotnet;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

public sealed class LiveKitRoomCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 15;
    private const int DEFAULT_MEDIA_DURATION_SECONDS = 5;
    private const int ADD_TRACK_TIMEOUT_SECONDS = 10;
    private const string VIDEO_TRACK_NAME = "test-pattern";
    private const string AUDIO_TRACK_NAME = "music";

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record RoomResult(
        bool Success,
        string Room,
        string Identity,
        string PublisherState,
        string? VideoTrackSid,
        string? AudioTrackSid,
        long? ConnectTimeMs,
        int? MediaDurationMs,
        string? Error);

    public LiveKitRoomCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var urlOption = new Option<string?>("--url")
        {
            Description = "The LiveKit web socket URL (wss://...). Defaults to the LIVEKIT_WEBSOCKET_URL environment variable."
        };

        var apiKeyOption = new Option<string?>("--api-key")
        {
            Description = "The LiveKit API key. Defaults to the LIVEKIT_API_KEY environment variable."
        };

        var apiSecretOption = new Option<string?>("--api-secret")
        {
            Description = "The LiveKit API secret. Defaults to the LIVEKIT_API_SECRET environment variable."
        };

        var roomOption = new Option<string?>("--room")
        {
            Description = "The room to join. Defaults to a random room name."
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to keep publishing after the connection is established.",
            DefaultValueFactory = _ => DEFAULT_MEDIA_DURATION_SECONDS
        };

        var command = new Command("room", "Join a LiveKit room, publish a test pattern and verify the connection.");
        command.Options.Add(urlOption);
        command.Options.Add(apiKeyOption);
        command.Options.Add(apiSecretOption);
        command.Options.Add(roomOption);
        command.Options.Add(durationOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(urlOption),
            parseResult.GetValue(apiKeyOption),
            parseResult.GetValue(apiSecretOption),
            parseResult.GetValue(roomOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string? url, string? apiKey, string? apiSecret, string? room, int durationSeconds,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(LiveKitRoomCommand));

        url ??= Environment.GetEnvironmentVariable("LIVEKIT_WEBSOCKET_URL");
        apiKey ??= Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
        apiSecret ??= Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");
        room ??= $"cli-{Guid.NewGuid().ToString("N")[..8]}";

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            return WriteResult(asJson,
                new RoomResult(false, room, string.Empty, "new", null, null, null, null,
                    "A URL, API key and API secret are required (--url/--api-key/--api-secret or LIVEKIT_WEBSOCKET_URL/LIVEKIT_API_KEY/LIVEKIT_API_SECRET)."),
                ExitCodes.InvalidArgument);
        }

        string identity = $"cli-publisher-{Guid.NewGuid().ToString("N")[..8]}";

        var session = new RoomPublisher(logger, room, identity, durationSeconds, timeoutSeconds);
        return await session.RunAsync(url, apiKey, apiSecret, asJson, ct).ConfigureAwait(false);
    }

    private static int WriteResult(bool asJson, RoomResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"LiveKit room OK: joined {result.Room} as {result.Identity}, published video {result.VideoTrackSid} " +
                $"and audio {result.AudioTrackSid}, connected in {result.ConnectTimeMs}ms and held for {result.MediaDurationMs}ms.");
        }
        else
        {
            Console.Error.WriteLine($"LiveKit room check failed: {result.Error}");
        }

        return exitCode;
    }

    /// <summary>
    /// Orchestrates a single publish session: web socket signalling, the AddTrack handshake and the
    /// publisher peer connection. Mirrors the LiveKitWebRTC example's room service, reduced to the
    /// publisher path needed for a connectivity check.
    /// </summary>
    private sealed class RoomPublisher
    {
        private readonly ILogger _logger;
        private readonly string _room;
        private readonly string _identity;
        private readonly int _durationSeconds;
        private readonly int _timeoutSeconds;

        private readonly ClientWebSocket _ws = new();
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<TrackInfo>> _pendingPublishes = new();
        private readonly TaskCompletionSource<bool> _connected = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private MediaTestSource? _media;
        private RTCPeerConnection? _publisherPc;
        private RTCPeerConnection? _subscriberPc;
        private string? _videoTrackSid;
        private string? _audioTrackSid;
        private volatile string _publisherState = "new";

        public RoomPublisher(ILogger logger, string room, string identity, int durationSeconds, int timeoutSeconds)
        {
            _logger = logger;
            _room = room;
            _identity = identity;
            _durationSeconds = durationSeconds;
            _timeoutSeconds = timeoutSeconds;
        }

        public async Task<int> RunAsync(string url, string apiKey, string apiSecret, bool asJson, CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                string jwt = new AccessToken(apiKey, apiSecret)
                    .WithIdentity(_identity)
                    .WithName(_identity)
                    .WithGrants(new VideoGrants { RoomJoin = true, Room = _room })
                    .ToJwt();

                _ws.Options.SetRequestHeader("Authorization", $"Bearer {jwt}");
                await _ws.ConnectAsync(new Uri($"{url.TrimEnd('/')}/rtc?protocol=9&sdk=dotnet&auto_subscribe=false"), ct).ConfigureAwait(false);

                if (_ws.State != WebSocketState.Open)
                {
                    return WriteResult(asJson,
                        new RoomResult(false, _room, _identity, _publisherState, null, null, null, null,
                            $"The LiveKit web socket did not open (state {_ws.State})."),
                        ExitCodes.TransportError);
                }

                _logger.LogDebug("LiveKit web socket connected, joining room {Room} as {Identity}.", _room, _identity);

                var receiveLoop = ReceiveLoopAsync(ct);

                var completed = await Task.WhenAny(_connected.Task, Task.Delay(TimeSpan.FromSeconds(_timeoutSeconds), ct)).ConfigureAwait(false);

                if (completed != _connected.Task || !await _connected.Task.ConfigureAwait(false))
                {
                    return WriteResult(asJson,
                        new RoomResult(false, _room, _identity, _publisherState, _videoTrackSid, _audioTrackSid,
                            stopwatch.ElapsedMilliseconds, null,
                            completed == _connected.Task
                                ? $"The publisher peer connection failed (state {_publisherState})."
                                : $"The publisher peer connection did not reach connected within {_timeoutSeconds}s."),
                        ExitCodes.Timeout);
                }

                long connectTimeMs = stopwatch.ElapsedMilliseconds;
                _logger.LogDebug("Publisher connected in {ConnectTimeMs}ms, publishing for {Duration}s.", connectTimeMs, _durationSeconds);

                await Task.Delay(TimeSpan.FromSeconds(_durationSeconds), ct).ConfigureAwait(false);

                return WriteResult(asJson,
                    new RoomResult(true, _room, _identity, _publisherState, _videoTrackSid, _audioTrackSid,
                        connectTimeMs, _durationSeconds * 1000, null),
                    ExitCodes.Ok);
            }
            catch (OperationCanceledException)
            {
                return WriteResult(asJson,
                    new RoomResult(false, _room, _identity, _publisherState, _videoTrackSid, _audioTrackSid, null, null, "Cancelled or a request timed out."),
                    ExitCodes.Timeout);
            }
            catch (Exception excp)
            {
                return WriteResult(asJson,
                    new RoomResult(false, _room, _identity, _publisherState, _videoTrackSid, _audioTrackSid, null, null, excp.Message),
                    ExitCodes.TransportError);
            }
            finally
            {
                await ShutdownAsync().ConfigureAwait(false);
            }
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
                    await HandleSignalAsync(response, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutting down.
            }
            catch (Exception excp)
            {
                _logger.LogDebug("LiveKit receive loop ended: {Error}", excp.Message);
            }
        }

        private async Task HandleSignalAsync(SignalResponse response, CancellationToken ct)
        {
            switch (response.MessageCase)
            {
                case SignalResponse.MessageOneofCase.Join:
                    _logger.LogDebug("Join response received, registering tracks.");
                    await PublishTracksAsync(ct).ConfigureAwait(false);
                    break;

                case SignalResponse.MessageOneofCase.TrackPublished:
                    _logger.LogDebug("Track published acknowledgement for cid {Cid} sid {Sid}.",
                        response.TrackPublished.Cid, response.TrackPublished.Track?.Sid);
                    if (_pendingPublishes.TryRemove(response.TrackPublished.Cid, out var pending))
                    {
                        pending.TrySetResult(response.TrackPublished.Track!);
                    }
                    break;

                case SignalResponse.MessageOneofCase.Answer:
                    if (_publisherPc != null)
                    {
                        var setResult = _publisherPc.setRemoteDescription(new RTCSessionDescriptionInit
                        {
                            type = RTCSdpType.answer,
                            sdp = response.Answer.Sdp
                        });
                        _logger.LogDebug("Applied publisher answer with result {Result}.", setResult);
                    }
                    break;

                case SignalResponse.MessageOneofCase.Offer:
                    // LiveKit sends an initial offer for the subscriber transport. Answer it even
                    // though no media is pulled, otherwise the connection does not complete.
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
                Cid = "cli-video",
                Name = VIDEO_TRACK_NAME,
                Type = TrackType.Video,
                Source = TrackSource.Camera,
                Width = 640,
                Height = 480
            }, ct).ConfigureAwait(false);
            _videoTrackSid = videoTrack.Sid;

            var audioTrack = await AddTrackAsync(new AddTrackRequest
            {
                Cid = "cli-audio",
                Name = AUDIO_TRACK_NAME,
                Type = TrackType.Audio,
                Source = TrackSource.Microphone
            }, ct).ConfigureAwait(false);
            _audioTrackSid = audioTrack.Sid;

            _logger.LogDebug("LiveKit registered tracks video {VideoSid} audio {AudioSid}.", _videoTrackSid, _audioTrackSid);

            _publisherPc = CreatePublisherPeerConnection();

            var offer = _publisherPc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
            await _publisherPc.setLocalDescription(offer).ConfigureAwait(false);

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
                throw new TimeoutException($"Timed out waiting for LiveKit to acknowledge the AddTrack request for cid {request.Cid}.");
            }

            return await tcs.Task.ConfigureAwait(false);
        }

        private RTCPeerConnection CreatePublisherPeerConnection()
        {
            var pc = new RTCPeerConnection();

            // LiveKit's room pipeline (SFU forwarding, SIP bridge, agents) consumes OPUS, so the
            // audio track must be pinned to it; see the LiveKitWebRTC example README for why G711
            // and G722 do not work end to end.
            _media = new MediaTestSource(opusOnly: true, _logger);
            _media.AddTracks(pc, MediaStreamStatusEnum.SendOnly);

            pc.onconnectionstatechange += async (state) =>
            {
                _publisherState = state.ToString();
                _logger.LogDebug("Publisher peer connection state changed to {State}.", state);

                if (state == RTCPeerConnectionState.connected)
                {
                    await _media.StartAsync().ConfigureAwait(false);
                    _connected.TrySetResult(true);
                }
                else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed)
                {
                    _connected.TrySetResult(false);
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

        private async Task ShutdownAsync()
        {
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

                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client shutting down", CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Error during LiveKit shutdown: {Error}", excp.Message);
            }

            _media?.Dispose();
            _publisherPc?.Close("room probe complete");
            _subscriberPc?.Close("room probe complete");
            _ws.Dispose();
        }
    }
}
