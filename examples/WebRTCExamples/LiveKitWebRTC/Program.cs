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
using Vpx.Net;

const string ListenUrl = "http://localhost:8080";

var _livekitApiKey = Environment.GetEnvironmentVariable("LIVEKIT_API_KEY");
var _livekitApiSecret = Environment.GetEnvironmentVariable("LIVEKIT_API_SECRET");
var _livekitWebsocketUrl = Environment.GetEnvironmentVariable("LIVEKIT_WEBSOCKET_URL");

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
    _livekitWebsocketUrl!));
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveKitRoomService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Mints a short-lived, subscribe-only access token for the browser viewer. The LiveKit API
// key and secret never leave this app; the browser only ever receives a scoped JWT.
app.MapPost("/api/join", (LiveKitRoomService roomService) => Results.Json(roomService.CreateViewerJoinInfo()));

// Returns non-secret publisher state so the page can show whether the C# publisher is up.
app.MapGet("/api/status", (LiveKitRoomService roomService) => Results.Json(new
{
    room = LiveKitRoomService.RoomName,
    publisherIdentity = LiveKitRoomService.PublisherIdentity,
    publisherState = roomService.PublisherConnectionState,
    subscriberState = roomService.SubscriberConnectionState
}));

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
    private const string STUN_URL = "stun:stun.cloudflare.com";

    public const string RoomName = "my-room";
    public const string PublisherIdentity = "user-123";
    public const string VideoTrackName = "test-pattern";
    public const string AudioTrackName = "music";

    private readonly ILogger<LiveKitRoomService> _logger;
    private readonly LikeKitWebSocketClient _liveKitWebSocketClient;

    private readonly string _appId;
    private readonly string _apiToken;
    private readonly string _websocketUrl;

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
            .WithName("Browser viewer")
            .WithGrants(new VideoGrants
            {
                RoomJoin = true,
                Room = RoomName,
                CanSubscribe = true,
                CanPublish = false,
                CanPublishData = false
            })
            .WithTtl(TimeSpan.FromHours(1));

        _logger.LogInformation("Created viewer access token for identity {Identity} on room {Room}.", identity, RoomName);

        return new ViewerJoinInfo(_websocketUrl, token.ToJwt(), RoomName, identity);
    }

    public LiveKitRoomService(
        ILogger<LiveKitRoomService> logger,
        LikeKitWebSocketClient liveKitWebSocketClient,
        string appId,
        string apiToken,
        string websocketUrl)
    {
        _logger = logger;
        _liveKitWebSocketClient = liveKitWebSocketClient;
        _appId = appId;
        _apiToken = apiToken;
        _websocketUrl = websocketUrl;

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
                    response.Join.Room?.Name, response.Join.Participant?.Identity);

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
            .WithIdentity(PublisherIdentity)
            .WithName("John Doe")
            .WithGrants(new VideoGrants { RoomJoin = true, Room = RoomName });

        if (token is null)
        {
            _logger.LogError("Failed to generate LiveKit access token.");
        }
        else
        {
            _logger.LogDebug("Successfully generated LiveKit access token.");

            _jwtToken = token.ToJwt();

            await _liveKitWebSocketClient.ConnectAsync(_websocketUrl, _jwtToken, cancellationToken);

            // Publishing is kicked off when the Join response arrives, see HandleLiveKitSignalResponse.
            // The tracks must be registered with AddTrack requests, and acknowledged by the server,
            // before the publisher peer connection's offer is sent.
        }
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
            Name = VideoTrackName,
            Type = TrackType.Video,
            Source = TrackSource.Camera,
            Width = 640,
            Height = 480
        });

        var audioTrackInfo = await AddTrackAsync(new AddTrackRequest
        {
            Cid = "music-audio",
            Name = AudioTrackName,
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
    }

    /// <summary>
    /// Creates a peer connection for the subscriber role. This peer connection set up is mandatory and is triggered by a LiveKit offer when the web socket
    /// connection is established. The C# application does not want to subscribe to new tracks from the publisher so it can set auto subscribe to false. It
    /// will still create the peer connection and have data channels set up but will not be used for media.
    /// </summary>
    private RTCPeerConnection CreateSubscriberPeerConnection()
    {
        RTCConfiguration config = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
        };
        var pc = new RTCPeerConnection(config);

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
        RTCConfiguration config = new RTCConfiguration
        {
            iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
        };
        var pc = new RTCPeerConnection(config);

        var vp8Codec = new VP8Codec();
        _videoSource = new VideoTestPatternSource(vp8Codec);
        _audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

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
