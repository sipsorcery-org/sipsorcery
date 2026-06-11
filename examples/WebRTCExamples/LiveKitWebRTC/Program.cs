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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
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
using SIPSorcery.Sys;
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

//// Returns non-secret publisher info for display in the page. The token and the raw SFU
//// API are never exposed to the browser.
//app.MapGet("/api/publisher", (CloudflareSfuService sfu) => Results.Json(new
//{
//    sessionId = sfu.PublisherSessionId,
//    audioTrackName = sfu.AudioTrackName,
//    videoTrackName = sfu.VideoTrackName
//}));

//// Creates a subscriber session and pulls the publisher's remote tracks. Cloudflare generates
//// the offer for pulled tracks, which we hand back to the browser to answer.
//app.MapPost("/api/subscribe", async (CloudflareSfuService sfu) =>
//{
//    var (subscriberSessionId, sdp) = await sfu.SubscribeAsync();
//    return Results.Json(new { subscriberSessionId, sdp });
//});

//// Forwards the browser's answer SDP to Cloudflare to complete the pulled-track negotiation.
//app.MapPost("/api/renegotiate", async (CloudflareSfuService sfu, RenegotiateBody body) =>
//{
//    await sfu.RenegotiateAsync(body.SubscriberSessionId, body.Sdp);
//    return Results.Ok();
//});

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
            new Uri($"{url}/rtc?protocol=9&sdk=dotnet&auto_subscribe=true"),
            ct);

        if (_ws.State == WebSocketState.Open)
        {
            _logger.LogInformation("WebSocket connection established to LiveKit.");
            _receiveTask = DoReceive(ct);
        }
    }

    public async Task SendPing(CancellationToken ct)
    {
        var req = new SignalRequest
        {
            PingReq = new Ping { Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
        };
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
                                var prettyPayloadJson = JsonSerializer.Serialize(payloadJsonElement, new JsonSerializerOptions { WriteIndented = true });
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

sealed class LiveKitRoomService : IHostedService
{
    private const string STUN_URL = "stun:stun.cloudflare.com";

    private readonly ILogger<LiveKitRoomService> _logger;
    private readonly LikeKitWebSocketClient _liveKitWebSocketClient;

    private readonly string _appId;
    private readonly string _apiToken;
    private readonly string _websocketUrl;

    private string _jwtToken = string.Empty;
    private RTCPeerConnection? _publisherPc;
    private VideoTestPatternSource? _videoSource;
    private AudioExtrasSource? _audioSource;

    public string? PublisherSessionId { get; private set; }
    public string AudioTrackName => "test-audio";
    public string VideoTrackName => "test-pattern";

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

        _liveKitWebSocketClient.OnSignalResponse += HandleLiveKitSignalResponse;
    }

    private void HandleLiveKitSignalResponse(SignalResponse response)
    {   
        switch (response.MessageCase)
        {
            case SignalResponse.MessageOneofCase.Offer:
                if (_publisherPc == null)
                {
                    _publisherPc = CreatePublisherPeerConnection();
                    var result = _publisherPc.setRemoteDescription(new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.offer,
                        sdp = response.Offer.Sdp
                    });

                    _logger.LogInformation("Set remote description for publisher peer connection with result: {Result}.", result);
                }
                break;

            case SignalResponse.MessageOneofCase.Trickle:
                _logger.LogInformation("Received ICE candidate from LiveKit for target {Target}: {CandidateInit}.",
                    response.Trickle.Target, response.Trickle.CandidateInit);

                if(_publisherPc != null)
                {
                    if (RTCIceCandidateInit.TryParse(response.Trickle.CandidateInit, out var parsedCandidate))
                    {
                        _logger.LogInformation("Parsed ICE candidate successfully: {Candidate}.", parsedCandidate.candidate);
                        _publisherPc.addIceCandidate(parsedCandidate);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to parse ICE candidate from LiveKit: {CandidateInit}.", response.Trickle.CandidateInit);
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
            .WithIdentity("user-123")
            .WithName("John Doe")
            .WithGrants(new VideoGrants { RoomJoin = true, Room = "my-room" });

        if (token is null)
        {
            _logger.LogError("Failed to generate LiveKit access token.");
        }
        else
        {
            _logger.LogDebug("Successfully generated LiveKit access token.");

            _jwtToken = token.ToJwt();

            await _liveKitWebSocketClient.ConnectAsync(_websocketUrl, _jwtToken, cancellationToken);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("LiveKit Room service stopping.");
    }

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
