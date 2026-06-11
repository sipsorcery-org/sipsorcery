//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example for interoperating with the LiveKit cloud service.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 10 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Livekit.Server.Sdk.Dotnet;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Vpx.Net;

const string ListenUrl = "http://localhost:8080";

var _livekitApiKey = Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
var _livekitApiSecret = Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");
var _livekitWebsocketUrl = Environment.GetEnvironmentVariable("LIVEKIT_WEBSOCKET_URL");

// Optional. When set, a SIP dispatch rule is created on startup that routes inbound calls on
// this trunk into the publisher's room. Leave unset if no SIP trunk is configured.
var _livekitSipTrunkId = Environment.GetEnvironmentVariable("LIVEKIT_SIP_TRUNK_ID");

if (string.IsNullOrWhiteSpace(_livekitApiKey) || string.IsNullOrWhiteSpace(_livekitApiSecret))
{
    Console.Error.WriteLine("Please set the LIVEKIT_API_KEY and LIVEKIT_API_SECRET environment variables.");
    return;
}

if (string.IsNullOrWhiteSpace(_livekitWebsocketUrl))
{
    Console.Error.WriteLine("Please set the LIVEKIT_WEBSOCKET_URL environment variable.");
    return;
}

// Route the SIPSorcery library logs and the ASP.NET host logs through the same Serilog console sink.
var seriLogger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
    .WriteTo.Console()
    .CreateLogger();
SIPSorcery.LogFactory.Set(new SerilogLoggerFactory(seriLogger));

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(ListenUrl);
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(seriLogger);

builder.Services.AddTransient<LikeKitWebSocketClient>();
builder.Services.AddSingleton(sp => new LiveKitRoomService(
    sp.GetRequiredService<ILogger<LiveKitRoomService>>(),
    sp.GetRequiredService<LikeKitWebSocketClient>(),
    _livekitApiKey!,
    _livekitApiSecret!,
    _livekitWebsocketUrl!,
    _livekitSipTrunkId));
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveKitRoomService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Mints a short-lived, subscribe-only access token for the browser viewer. The LiveKit API
// key and secret never leave this app; the browser only ever receives a scoped JWT.
app.MapPost("/api/join", (LiveKitRoomService roomService) => Results.Json(roomService.CreateViewerJoinInfo()));

// Returns non-secret publisher state so the page can show whether the C# publisher is up.
//app.MapGet("/api/status", (LiveKitRoomService roomService) => Results.Json(new
//{
//    room = LiveKitRoomService.RoomName,
//    publisherIdentity = LiveKitRoomService.PublisherIdentity,
//    publisherState = roomService.PublisherConnectionState,
//    subscriberState = roomService.SubscriberConnectionState
//}));

Console.WriteLine($"LiveKit WebRTC example. Browse to {ListenUrl} once publishing has started.");

app.Run();

sealed class LikeKitWebSocketClient
{
    private readonly ILogger<LikeKitWebSocketClient> _logger;

    private readonly ClientWebSocket _ws = new ClientWebSocket();
    private Task? _receiveTask;

    public event Action<SignalResponse>? OnSignalResponse;

    public LikeKitWebSocketClient(ILogger<LikeKitWebSocketClient> logger)
    {
        _logger = logger;
    }

    public async Task ConnectAsync(string url, string jwt, CancellationToken ct)
    {
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {jwt}");
        await _ws.ConnectAsync(
            new Uri($"{url}/rtc?protocol=9&sdk=dotnet&auto_subscribe=false"),
            ct);

        if (_ws.State == WebSocketState.Open)
        {
            _logger.LogInformation("WebSocket connection established to LiveKit.");
            _receiveTask = DoReceive(ct);
        }
    }

    public async Task SendAsync(SignalRequest req, CancellationToken ct)
    {
        //var req = new SignalRequest
        //{
        //    PingReq = new Ping { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
        //};
        await _ws.SendAsync(req.ToByteArray(), WebSocketMessageType.Binary, true, ct);
    }

    /// <summary>
    /// Performs a graceful web socket close handshake with the LiveKit server.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct)
    {
        if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
        {
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client shutting down", ct);
        }
    }

    private async Task DoReceive(CancellationToken ct)
    {
        var buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                using var messageBuffer = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket close received. Status: {CloseStatus}, Description: {CloseStatusDescription}.",
                            result.CloseStatus, result.CloseStatusDescription);

                        if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                        }

                        return;
                    }

                    if (result.Count > 0)
                    {
                        messageBuffer.Write(buffer, 0, result.Count);
                    }
                }
                while (!result.EndOfMessage);

                _logger.LogDebug("Received message of type {MessageType} and length {Length}.",
                    result.MessageType, messageBuffer.Length);

                if (result.MessageType == WebSocketMessageType.Binary && messageBuffer.Length > 0)
                {
                    messageBuffer.Position = 0;
                    var signalResponse = SignalResponse.Parser.ParseFrom(messageBuffer);
                    _logger.LogDebug("Parsed LiveKit SignalResponse with message case {MessageCase}.",
                        signalResponse.MessageCase);

                    switch (signalResponse.MessageCase)
                    {
                        case SignalResponse.MessageOneofCase.None:
                            _logger.LogInformation("LiveKit response has no message payload.");
                            break;

                        default:
                            var payloadProperty = typeof(SignalResponse).GetProperty(signalResponse.MessageCase.ToString());
                            if (payloadProperty?.GetValue(signalResponse) is IMessage payload)
                            {
                                var payloadJson = JsonFormatter.Default.Format(payload);
                                var payloadJsonElement = JsonSerializer.Deserialize<JsonElement>(payloadJson);
                                //var prettyPayloadJson = JsonSerializer.Serialize(payloadJsonElement, new JsonSerializerOptions { WriteIndented = true });
                                var prettyPayloadJson = JsonSerializer.Serialize(payloadJsonElement, new JsonSerializerOptions { WriteIndented = false });
                                _logger.LogInformation("LiveKit response ({MessageCase}): {PayloadJson}",
                                    signalResponse.MessageCase,
                                    prettyPayloadJson);
                            }
                            else
                            {
                                _logger.LogWarning("No payload found for LiveKit response case {MessageCase}.",
                                    signalResponse.MessageCase);
                            }
                            break;
                    }

                    OnSignalResponse?.Invoke(signalResponse);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogDebug("WebSocket receive loop canceled.");
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex, "WebSocket receive error.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in WebSocket receive loop.");
        }
    }
}

/// <summary>
/// The connection details handed to the browser so it can join the LiveKit room as a viewer.
/// </summary>
sealed record ViewerJoinInfo(string Url, string Token, string Room, string Identity);

sealed class LiveKitRoomService : IHostedService
{
    public const string ROOM_NAME_PREFIX = "Room";
    public const string PUBLISHER_IDENTITY_PREFIX = "Publisher";
    public const string VIDEO_TRACK_NAME = "test-pattern";
    public const string AUDIO_TRACK_NAME = "music";
    public const string SIP_DISPATCH_RULE_NAME = "sipsorcery-demo-direct";

    private readonly ILogger<LiveKitRoomService> _logger;
    private readonly LikeKitWebSocketClient _liveKitWebSocketClient;

    private readonly string _roomName = $"{ROOM_NAME_PREFIX}-{Guid.NewGuid().ToString("N")[..8]}";
    private readonly string _publisherIdentity = $"{PUBLISHER_IDENTITY_PREFIX}-{Guid.NewGuid().ToString("N")[..8]}";
    private readonly string _appId;
    private readonly string _apiToken;
    private readonly string _websocketUrl;
    private readonly string? _sipTrunkId;

    private string? _sipDispatchRuleId;

    private string _jwtToken = string.Empty;
    private RTCPeerConnection? _subscriberPc;
    private RTCPeerConnection? _publisherPc;
    private VideoTestPatternSource? _videoSource;
    private AudioExtrasSource? _audioSource;

    /// <summary>
    /// AddTrack requests awaiting their TrackPublished acknowledgement, keyed by client track id (cid).
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TrackInfo>> _pendingTrackPublishes = new();

    public string PublisherConnectionState => _publisherPc?.connectionState.ToString() ?? "not started";

    public string SubscriberConnectionState => _subscriberPc?.connectionState.ToString() ?? "not started";

    /// <summary>
    /// Creates the join details for a browser viewer: a subscribe-only token with a random
    /// identity, scoped to the publisher's room.
    /// </summary>
    public ViewerJoinInfo CreateViewerJoinInfo()
    {
        var identity = $"viewer-{Guid.NewGuid().ToString("N")[..8]}";

        var token = new AccessToken(_appId, _apiToken)
            .WithIdentity(identity)
            .WithName($"Browser {identity}")
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = _roomName,
                CanSubscribe = true,
                CanPublish = false,
                CanPublishData = false
            })
            .WithTtl(TimeSpan.FromHours(1));

        _logger.LogInformation("Created viewer access token for identity {Identity} on room {Room}.", identity, _roomName);

        return new ViewerJoinInfo(_websocketUrl, token.ToJwt(), _roomName, identity);
    }

    public LiveKitRoomService(
        ILogger<LiveKitRoomService> logger,
        LikeKitWebSocketClient liveKitWebSocketClient,
        string appId,
        string apiToken,
        string websocketUrl,
        string? sipTrunkId = null)
    {
        _logger = logger;
        _liveKitWebSocketClient = liveKitWebSocketClient;
        _appId = appId;
        _apiToken = apiToken;
        _websocketUrl = websocketUrl;
        _sipTrunkId = sipTrunkId;

        _liveKitWebSocketClient.OnSignalResponse += async (e) =>
        {
            try
            {
                await HandleLiveKitSignalResponse(e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LiveKit signal response event handler failed.");
            }
        };
    }

    private async Task HandleLiveKitSignalResponse(SignalResponse response)
    {
        switch (response.MessageCase)
        {
            case SignalResponse.MessageOneofCase.Join:

                _logger.LogInformation("Join response received for room {Room} as participant {Identity}.",
                    _roomName, _publisherIdentity);

                // The participant is now in the room so the tracks can be registered and published.
                await PublishTracksAsync();
                break;

            case SignalResponse.MessageOneofCase.TrackPublished:

                _logger.LogInformation("Track published acknowledgement for cid {Cid}, track sid {Sid}.",
                    response.TrackPublished.Cid, response.TrackPublished.Track?.Sid);

                if (_pendingTrackPublishes.TryRemove(response.TrackPublished.Cid, out var pendingPublish))
                {
                    pendingPublish.TrySetResult(response.TrackPublished.Track!);
                }
                break;

            case SignalResponse.MessageOneofCase.Offer:

                if (_subscriberPc == null)
                {
                    _subscriberPc = CreateSubscriberPeerConnection();
                    var result = _subscriberPc.setRemoteDescription(new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.offer,
                        sdp = response.Offer.Sdp
                    });

                    _logger.LogInformation("Set remote description for subscriber peer connection with result: {Result}.", result);

                    var answer = _subscriberPc.createAnswer();
                    await _subscriberPc.setLocalDescription(answer);

                    _logger.LogInformation("Created answer for subscriber peer connection: {Answer}.", answer);

                    await _liveKitWebSocketClient.SendAsync(new SignalRequest { Answer = new SessionDescription { Type = "answer", Sdp = answer.sdp } }, CancellationToken.None);
                }
                break;

            case SignalResponse.MessageOneofCase.Answer:

                if (_publisherPc != null)
                {
                    var result = _publisherPc.setRemoteDescription(new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.answer,
                        sdp = response.Answer.Sdp
                    });

                    _logger.LogInformation("Set remote answer for publisher peer connection with result: {Result}.", result);
                }
                break;

            case SignalResponse.MessageOneofCase.Trickle:

                _logger.LogInformation("Received ICE candidate from LiveKit for target {Target}: {CandidateInit}.",
                    response.Trickle.Target, response.Trickle.CandidateInit);

                var targetPeerConnection = response.Trickle.Target switch
                {
                    SignalTarget.Publisher => _publisherPc,
                    SignalTarget.Subscriber => _subscriberPc,
                    _ => null
                };

                if (targetPeerConnection != null)
                {
                    if (RTCIceCandidateInit.TryParse(response.Trickle.CandidateInit, out var parsedCandidate))
                    {
                        _logger.LogInformation("Parsed {Target} ICE candidate successfully: {Candidate}.", response.Trickle.Target, parsedCandidate.candidate);

                        targetPeerConnection.addIceCandidate(parsedCandidate);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to parse {Target} ICE candidate from LiveKit: {CandidateInit}.", response.Trickle.Target, response.Trickle.CandidateInit);
                    }
                }

                break;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LiveKit Room service starting.");

        // Generate access token
        var token = new AccessToken(_appId, _apiToken)
            .WithIdentity(_publisherIdentity)
            .WithName("John Doe")
            .WithGrants(new VideoGrants { RoomJoin = true, Room = _roomName });

        if (token is null)
        {
            _logger.LogError("Failed to generate LiveKit access token.");
        }
        else
        {
            _logger.LogDebug("Successfully generated LiveKit access token.");

            _jwtToken = token.ToJwt();

            if (!string.IsNullOrWhiteSpace(_sipTrunkId))
            {
                try
                {
                    await EnsureSipDispatchRuleAsync();
                }
                catch (Exception ex)
                {
                    // SIP is an optional extra for this example so a failure to set up the
                    // dispatch rule should not stop the WebRTC publisher.
                    _logger.LogError(ex, "Failed to configure the SIP dispatch rule for trunk {TrunkId}.", _sipTrunkId);
                }
            }
            else
            {
                _logger.LogDebug("LIVEKIT_SIP_TRUNK_ID not set, skipping SIP dispatch rule configuration.");
            }

            await _liveKitWebSocketClient.ConnectAsync(_websocketUrl, _jwtToken, cancellationToken);

            // Publishing is kicked off when the Join response arrives, see HandleLiveKitSignalResponse.
            // The tracks must be registered with AddTrack requests, and acknowledged by the server,
            // before the publisher peer connection's offer is sent.
        }
    }

    /// <summary>
    /// Creates a client for the LiveKit server's HTTP API, which lives on the same host as the
    /// signalling web socket.
    /// </summary>
    private SipServiceClient CreateSipServiceClient()
    {
        var apiHost = _websocketUrl.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
            ? $"https://{_websocketUrl[6..]}"
            : _websocketUrl.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
                ? $"http://{_websocketUrl[5..]}"
                : _websocketUrl;

        return new SipServiceClient(apiHost, _appId, _apiToken);
    }

    /// <summary>
    /// Points inbound SIP calls on the configured trunk at this instance's room. A dispatch rule
    /// must exist BEFORE a call arrives; LiveKit rejects calls that match no rule. Because the
    /// room name is random per run, the rule is recreated on every start: any rule left behind by
    /// a previous run (matched by name) is deleted first, since CreateSIPDispatchRule does not
    /// upsert and duplicate rules on the same trunk would compete for calls.
    /// </summary>
    private async Task EnsureSipDispatchRuleAsync()
    {
        var sipClient = CreateSipServiceClient();

        var existingRules = await sipClient.ListSIPDispatchRule(new ListSIPDispatchRuleRequest());
        foreach (var staleRule in existingRules.Items.Where(x => x.Name == SIP_DISPATCH_RULE_NAME))
        {
            _logger.LogInformation("Deleting SIP dispatch rule {RuleId} left over from a previous run.", staleRule.SipDispatchRuleId);

            await sipClient.DeleteSIPDispatchRule(new DeleteSIPDispatchRuleRequest { SipDispatchRuleId = staleRule.SipDispatchRuleId });
        }

        var createdRule = await sipClient.CreateSIPDispatchRule(new CreateSIPDispatchRuleRequest
        {
            DispatchRule = new SIPDispatchRuleInfo
            {
                Name = SIP_DISPATCH_RULE_NAME,
                TrunkIds = { _sipTrunkId },
                Rule = new SIPDispatchRule
                {
                    DispatchRuleDirect = new SIPDispatchRuleDirect { RoomName = _roomName }
                }
            }
        });

        _sipDispatchRuleId = createdRule.SipDispatchRuleId;

        _logger.LogInformation("Created SIP dispatch rule {RuleId} routing inbound calls on trunk {TrunkId} to room {Room}.",
            _sipDispatchRuleId, _sipTrunkId, _roomName);
    }

    /// <summary>
    /// Registers the audio and video tracks with LiveKit and, once the server has acknowledged
    /// them, creates the publisher peer connection and sends its offer. LiveKit needs the
    /// AddTrack registrations BEFORE the offer arrives so it can map the SDP media sections to
    /// published tracks. The SIPSorcery SDP does not include a=msid entries, so the server falls
    /// back to matching pending tracks by media kind; the cid is used to correlate the
    /// TrackPublished acknowledgement with the request.
    /// </summary>
    private async Task PublishTracksAsync()
    {
        var videoTrackInfo = await AddTrackAsync(new AddTrackRequest
        {
            Cid = "testpattern-video",
            Name = VIDEO_TRACK_NAME,
            Type = TrackType.Video,
            Source = TrackSource.Camera,
            Width = 640,
            Height = 480
        });

        var audioTrackInfo = await AddTrackAsync(new AddTrackRequest
        {
            Cid = "music-audio",
            Name = AUDIO_TRACK_NAME,
            Type = TrackType.Audio,
            Source = TrackSource.Microphone
        });

        _logger.LogInformation("LiveKit registered tracks, video sid {VideoSid}, audio sid {AudioSid}.",
            videoTrackInfo.Sid, audioTrackInfo.Sid);

        _publisherPc = CreatePublisherPeerConnection();

        var offer = _publisherPc.createOffer(new RTCOfferOptions { X_WaitForIceGatheringToComplete = true });
        await _publisherPc.setLocalDescription(offer);

        await _liveKitWebSocketClient.SendAsync(new SignalRequest { Offer = new SessionDescription { Type = "offer", Sdp = offer.sdp } }, CancellationToken.None);
    }

    /// <summary>
    /// Sends an AddTrack request to LiveKit and waits for the matching TrackPublished response.
    /// </summary>
    private async Task<TrackInfo> AddTrackAsync(AddTrackRequest request)
    {
        var tcs = new TaskCompletionSource<TrackInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingTrackPublishes[request.Cid] = tcs;

        _logger.LogInformation("Sending AddTrack request for {Type} track cid {Cid}.", request.Type, request.Cid);

        await _liveKitWebSocketClient.SendAsync(new SignalRequest { AddTrack = request }, CancellationToken.None);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (completed != tcs.Task)
        {
            _pendingTrackPublishes.TryRemove(request.Cid, out _);
            throw new TimeoutException($"Timed out waiting for LiveKit to acknowledge the AddTrack request for cid {request.Cid}.");
        }

        return await tcs.Task;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LiveKit Room service stopping.");

        if (_sipDispatchRuleId != null)
        {
            try
            {
                // Remove the dispatch rule so callers are rejected rather than dropped into a
                // room with no publisher. The room name is random per run so the rule would be
                // useless after shutdown anyway.
                await CreateSipServiceClient().DeleteSIPDispatchRule(
                    new DeleteSIPDispatchRuleRequest { SipDispatchRuleId = _sipDispatchRuleId });

                _logger.LogInformation("Deleted SIP dispatch rule {RuleId}.", _sipDispatchRuleId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete SIP dispatch rule {RuleId} during shutdown.", _sipDispatchRuleId);
            }
        }

        try
        {
            // Tell LiveKit the participant is leaving so it is removed from the room immediately,
            // rather than lingering until the server's connection timeout expires.
            await _liveKitWebSocketClient.SendAsync(new SignalRequest
            {
                Leave = new LeaveRequest
                {
                    CanReconnect = false,
                    Reason = DisconnectReason.ClientInitiated,
                    Action = LeaveRequest.Types.Action.Disconnect
                }
            }, cancellationToken);

            await _liveKitWebSocketClient.CloseAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send the leave request to LiveKit during shutdown.");
        }

        // Closing the publisher peer connection also stops the media sources via its
        // connection state change handler.
        _publisherPc?.Close("application shutdown");
        _subscriberPc?.Close("application shutdown");
    }

    /// <summary>
    /// Creates a peer connection for the subscriber role. This peer connection set up is mandatory and is triggered by a LiveKit offer when the web socket
    /// connection is established. The C# application does not want to subscribe to new tracks from the publisher so it can set auto subscribe to false. It
    /// will still create the peer connection and have data channels set up but will not be used for media.
    /// </summary>
    private RTCPeerConnection CreateSubscriberPeerConnection()
    {
        var pc = new RTCPeerConnection();

        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug("Subscriberpeer connection state change to {State}.", state);

            if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
        };

        pc.oniceconnectionstatechange += (state) => _logger.LogDebug("Subscriber ICE connection state change to {State}.", state);

        return pc;
    }

    /// <summary>
    /// Creates a peer connection for the publisher role, which will send audio and video tracks to LiveKit.
    /// </summary>
    private RTCPeerConnection CreatePublisherPeerConnection()
    {
        var pc = new RTCPeerConnection();

        var vp8Codec = new VP8Codec();
        _videoSource = new VideoTestPatternSource(vp8Codec);

        // LiveKit's media pipeline, in particular the SIP bridge, is built around Opus. The
        // SIPSorcery AudioEncoder does not include Opus by default, and without it the audio
        // track negotiates G711, which browser subscribers can decode but the SIP bridge sends
        // to callers as silence. Publish Opus only so the negotiation cannot fall back.
        _audioSource = new AudioExtrasSource(new AudioEncoder(includeOpus: true), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });
        _audioSource.RestrictFormats(format => format.Codec == AudioCodecsEnum.OPUS);

        MediaStreamTrack videoTrack = new MediaStreamTrack(_videoSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);
        MediaStreamTrack audioTrack = new MediaStreamTrack(_audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(audioTrack);

        _videoSource.OnVideoSourceEncodedSample += pc.SendVideo;
        _audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

        pc.OnVideoFormatsNegotiated += (formats) => _videoSource.SetVideoSourceFormat(formats.First());
        pc.OnAudioFormatsNegotiated += (formats) => _audioSource.SetAudioSourceFormat(formats.First());

        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug("Publisher peer connection state change to {State}.", state);

            if (state == RTCPeerConnectionState.connected)
            {
                await _audioSource.StartAudio();
                await _videoSource.StartVideo();
            }
            else if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                await _videoSource.CloseVideo();
                await _audioSource.CloseAudio();
            }
        };

        pc.oniceconnectionstatechange += (state) => _logger.LogDebug("Publisher ICE connection state change to {State}.", state);

        return pc;
    }
}
