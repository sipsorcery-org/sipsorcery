//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example for working with the Cloudflare Realtime SFU API.
// This example is an ASP.NET app that:
//   1. Publishes a test pattern video and audio stream to the SFU on startup.
//   2. Serves a browser subscriber page (wwwroot/index.html).
//   3. Proxies the Cloudflare "pull tracks" + "renegotiate" calls server-side so
//      the Cloudflare API token is NEVER sent to the browser, and the publisher
//      session id never needs to be copied and pasted.
//
// See: https://developers.cloudflare.com/realtime/sfu/https-api/
// API Reference: https://developers.cloudflare.com/realtime/static/realtime-api-2024-05-21.yaml
//
// To create the required Cloudflare Realtime SFU application and get the App ID and API token see:
// https://developers.cloudflare.com/realtime/sfu/get-started/
//
// Kiota (https://github.com/microsoft/kiota) codegen command for Cloudlfare realtime API client:
// kiota generate -l CSharp -d https://developers.cloudflare.com/realtime/static/calls-api-2024-05-21.yaml -c RealtimeSfuClient -n Cloudflare.Realtime.Sfu   -o ./RealtimeSfu --exclude-backward-compatible --clean-output
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 28 May 2026	Aaron Clauson	Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cloudflare.Realtime.Sfu;
using Cloudflare.Realtime.Sfu.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using Vpx.Net;

const string ListenUrl = "http://localhost:8080";

var cloudflareAppID = Environment.GetEnvironmentVariable("CLOUDFLARE_APPID");
var cloudflareAPIToken = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");

if (string.IsNullOrWhiteSpace(cloudflareAppID) || string.IsNullOrWhiteSpace(cloudflareAPIToken))
{
    Console.Error.WriteLine("Please set the CLOUDFLARE_APPID and CLOUDFLARE_API_TOKEN environment variables.");
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

// The publisher service holds the live Cloudflare session and the SFU API client (with the token).
// It is registered as both a singleton (so the endpoints can use it) and a hosted service (so it
// starts publishing when the app starts and tears down the session when the app stops).
builder.Services.AddSingleton(sp => new CloudflareSfuService(
    sp.GetRequiredService<ILogger<CloudflareSfuService>>(),
    cloudflareAppID!,
    cloudflareAPIToken!));
builder.Services.AddHostedService(sp => sp.GetRequiredService<CloudflareSfuService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// Returns non-secret publisher info for display in the page. The token and the raw SFU
// API are never exposed to the browser.
app.MapGet("/api/publisher", (CloudflareSfuService sfu) => Results.Json(new
{
    sessionId = sfu.PublisherSessionId,
    audioTrackName = sfu.AudioTrackName,
    videoTrackName = sfu.VideoTrackName
}));

// Creates a subscriber session and pulls the publisher's remote tracks. Cloudflare generates
// the offer for pulled tracks, which we hand back to the browser to answer.
app.MapPost("/api/subscribe", async (CloudflareSfuService sfu) =>
{
    var (subscriberSessionId, sdp) = await sfu.SubscribeAsync();
    return Results.Json(new { subscriberSessionId, sdp });
});

// Forwards the browser's answer SDP to Cloudflare to complete the pulled-track negotiation.
app.MapPost("/api/renegotiate", async (CloudflareSfuService sfu, RenegotiateBody body) =>
{
    await sfu.RenegotiateAsync(body.SubscriberSessionId, body.Sdp);
    return Results.Ok();
});

Console.WriteLine($"Cloudflare WebRTC SFU example. Browse to {ListenUrl} once publishing has started.");

app.Run();

/// <summary>
/// Body for the /api/renegotiate endpoint. Bound from the browser's JSON request
/// (property matching is case-insensitive, so subscriberSessionId/sdp map across).
/// </summary>
record RenegotiateBody(string SubscriberSessionId, string Sdp);

/// <summary>
/// Hosts the publisher peer connection and proxies the Cloudflare SFU calls. Keeping all
/// Cloudflare interaction here means the API token stays server-side.
/// </summary>
sealed class CloudflareSfuService : IHostedService
{
    private const string STUN_URL = "stun:stun.cloudflare.com";

    private readonly ILogger<CloudflareSfuService> _logger;
    private readonly Cloudflare.Realtime.Sfu.Apps.Item.Sessions.SessionsRequestBuilder _sessions;

    private RTCPeerConnection? _publisherPc;
    private VideoTestPatternSource? _videoSource;
    private AudioExtrasSource? _audioSource;

    public string? PublisherSessionId { get; private set; }
    public string AudioTrackName => "test-audio";
    public string VideoTrackName => "test-pattern";

    public CloudflareSfuService(ILogger<CloudflareSfuService> logger, string appId, string apiToken)
    {
        _logger = logger;

        var authProvider = new BaseBearerTokenAuthenticationProvider(new StaticAccessTokenProvider(apiToken));
        var httpLoggingHandler = new HttpLoggingHandler(SIPSorcery.LogFactory.CreateLogger<HttpLoggingHandler>())
        {
            InnerHandler = new HttpClientHandler()
        };
        var httpClient = new HttpClient(httpLoggingHandler);
        var requestAdapter = new HttpClientRequestAdapter(authProvider, null, null, httpClient);
        var client = new RealtimeSfuClient(requestAdapter);
        _sessions = client.Apps[appId].Sessions;
    }

    /// <summary>
    /// Creates the publisher session and pushes the local audio/video tracks to Cloudflare.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var newSessionResponse = await _sessions.New.PostAsync(cancellationToken: cancellationToken);
        PublisherSessionId = newSessionResponse?.SessionId;
        _logger.LogInformation("Created publisher session {SessionId}.", PublisherSessionId);

        _publisherPc = CreatePublisherPeerConnection();
        var offer = _publisherPc.createOffer();
        await _publisherPc.setLocalDescription(offer);

        var newTrackResponse = await _sessions[PublisherSessionId!].Tracks.New.PostAsync(new TracksRequest
        {
            SessionDescription = new SessionDescription
            {
                Type = SessionDescription_type.Offer,
                Sdp = offer.sdp
            },
            Tracks = new List<TrackObject>
            {
                new() { Location = TrackObject_location.Local, Mid = "0", Kind = "audio", TrackName = AudioTrackName },
                new() { Location = TrackObject_location.Local, Mid = "1", Kind = "video", TrackName = VideoTrackName }
            }
        }, cancellationToken: cancellationToken);

        var answer = new RTCSessionDescriptionInit { sdp = newTrackResponse?.SessionDescription?.Sdp, type = RTCSdpType.answer };
        _publisherPc.setRemoteDescription(answer);

        _logger.LogInformation("Publisher tracks pushed for session {SessionId}.", PublisherSessionId);
    }

    /// <summary>
    /// Gracefully tears down the publisher session. The SFU API has no explicit "delete session"
    /// call; closing its tracks (force = true, so no WebRTC renegotiation is required) stops the
    /// media flow and lets Cloudflare reclaim the session once the transport drops.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await CloseSessionAsync(PublisherSessionId);
        _publisherPc?.Close("server shutdown");
    }

    /// <summary>
    /// Creates a subscriber session and pulls the publisher's remote tracks. Returns the
    /// subscriber session id and the offer SDP that Cloudflare generated for the pulled tracks.
    /// </summary>
    public async Task<(string SubscriberSessionId, string? Sdp)> SubscribeAsync()
    {
        var subscriber = await _sessions.New.PostAsync();
        var subscriberSessionId = subscriber?.SessionId
            ?? throw new InvalidOperationException("Cloudflare did not return a subscriber session id.");

        // For remote (pull) tracks no offer is sent; Cloudflare adds the m-lines server-side and
        // returns an offer that the browser must answer.
        var pull = await _sessions[subscriberSessionId].Tracks.New.PostAsync(new TracksRequest
        {
            Tracks = new List<TrackObject>
            {
                new() { Location = TrackObject_location.Remote, SessionId = PublisherSessionId, TrackName = AudioTrackName },
                new() { Location = TrackObject_location.Remote, SessionId = PublisherSessionId, TrackName = VideoTrackName }
            }
        });

        _logger.LogInformation("Subscriber session {SubscriberSessionId} pulling from publisher {PublisherSessionId}.",
            subscriberSessionId, PublisherSessionId);

        return (subscriberSessionId, pull?.SessionDescription?.Sdp);
    }

    /// <summary>
    /// Forwards the browser's answer SDP to Cloudflare to complete the pulled-track negotiation.
    /// </summary>
    public async Task RenegotiateAsync(string subscriberSessionId, string sdp)
    {
        await _sessions[subscriberSessionId].Renegotiate.PutAsync(new RenegotiateRequest
        {
            SessionDescription = new SessionDescription
            {
                Type = SessionDescription_type.Answer,
                Sdp = sdp
            }
        });

        _logger.LogInformation("Renegotiated subscriber session {SubscriberSessionId}.", subscriberSessionId);
    }

    private async Task CloseSessionAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            var session = _sessions[sessionId];

            var state = await session.GetAsync();
            var mids = state?.Tracks?
                .Where(t => !string.IsNullOrWhiteSpace(t.Mid))
                .Select(t => t.Mid!)
                .Distinct()
                .ToList() ?? new List<string>();

            if (mids.Count == 0)
            {
                _logger.LogInformation("Session {SessionId} has no open tracks to close.", sessionId);
                return;
            }

            _logger.LogInformation("Closing {Count} track(s) for session {SessionId}.", mids.Count, sessionId);

            await session.Tracks.Close.PutAsync(new CloseTracksRequest
            {
                Force = true,
                Tracks = mids.Select(mid => new CloseTrackObject { Mid = mid }).ToList()
            });

            _logger.LogInformation("Closed tracks for session {SessionId}.", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to close Cloudflare session {SessionId}: {Message}", sessionId, ex.Message);
        }
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

        MediaStreamTrack videoTrack = new MediaStreamTrack(_videoSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
        pc.addTrack(videoTrack);
        MediaStreamTrack audioTrack = new MediaStreamTrack(_audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
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

/// <summary>
/// Supplies the static Cloudflare bearer token to the Kiota request adapter.
/// </summary>
sealed class StaticAccessTokenProvider : IAccessTokenProvider
{
    private readonly string _accessToken;

    public StaticAccessTokenProvider(string accessToken)
    {
        _accessToken = accessToken;
        AllowedHostsValidator = new AllowedHostsValidator(new[] { "rtc.live.cloudflare.com" });
    }

    public AllowedHostsValidator AllowedHostsValidator { get; }

    public Task<string> GetAuthorizationTokenAsync(
        Uri uri,
        Dictionary<string, object>? additionalAuthenticationContext = default,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_accessToken);
    }
}
