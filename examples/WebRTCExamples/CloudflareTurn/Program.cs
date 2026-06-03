//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example for working with the Cloudflare Realtime TURN API.
//
// See: https://developers.cloudflare.com/realtime/turn/
//
// To create the required Cloudflare Realtime TURN application and get the App ID and API token see:
// https://developers.cloudflare.com/realtime/turn/generate-credentials/
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 31 May 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using LanguageExt;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Examples;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using Vpx.Net;

const string ListenUrl = "http://localhost:8080";
const int TURN_CREDENTIALS_TTL_SECONDS = 60;

var cloudflareAppID = Environment.GetEnvironmentVariable("CLOUDFLARE_APPID");
var cloudflareAPIToken = Environment.GetEnvironmentVariable("CLOUDFLARE_API_TOKEN");

Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

if (string.IsNullOrWhiteSpace(cloudflareAppID) || string.IsNullOrWhiteSpace(cloudflareAPIToken))
{
    Console.Error.WriteLine("Please set the CLOUDFLARE_APPID and CLOUDFLARE_API_TOKEN environment variables.");
    return;
}

logger = AddConsoleLogger();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(ListenUrl);
//builder.Logging.ClearProviders();
//builder.Logging.AddSerilog((Serilog.ILogger)logger);

builder.Services
    .AddTransient<HttpLoggingHandler>()
    .AddTransient<ICloudflareTurnApiClient, CloudflareTurnApiClient>()
    .AddHttpClient(CloudflareTurnApiClient.CLOUDFLARE_HTTP_CLIENT_NAME, client =>
    {
        CloudflareTurnApiClientFactory.Configure(client, cloudflareAPIToken);
    })
    .AddHttpMessageHandler<HttpLoggingHandler>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/cloudflare/iceservers", async (ICloudflareTurnApiClient cloudflareTurnApiClient) => 
    (await cloudflareTurnApiClient.CreateCredentialsAsync(cloudflareAppID, TURN_CREDENTIALS_TTL_SECONDS)).Match(
        Right: credentials => Results.Json(credentials),
        Left: error =>
        {
            logger.LogError($"Failed to get TURN credentials: {error}");
            return Results.Problem("Failed to get TURN credentials.");
        }));
 
// Accepts a WebRTC offer from the browser and responds with an answer containing the Cloudflare TURN server details.
//app.MapPost("/api/webrtc", async (CloudflareService cloudflareService) =>
//{
//    //var (subscriberSessionId, sdp) = await sfu.SubscribeAsync();
//    //return Results.Json(new { subscriberSessionId, sdp });
//});

Console.WriteLine($"Cloudflare WebRTC TURN example. Browse to {ListenUrl} once publishing has started.");

app.Run();

Task<RTCPeerConnection> CreatePeerConnection()
{
    var pc = new RTCPeerConnection(null);

    var vp8Codec = new VP8Codec();
    var testPatternSource = new VideoTestPatternSource(vp8Codec);
    var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

    MediaStreamTrack videoTrack = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
    pc.addTrack(videoTrack);
    MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
    pc.addTrack(audioTrack);

    testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
    audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

    pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());
    pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());
    pc.onsignalingstatechange += () =>
    {
        logger.LogDebug($"Signalling state change to {pc.signalingState}.");

        if (pc.signalingState == RTCSignalingState.have_local_offer)
        {
            logger.LogDebug($"Local SDP offer:\n{pc.localDescription.sdp}");
        }
        else if (pc.signalingState == RTCSignalingState.stable)
        {
            logger.LogDebug($"Remote SDP offer:\n{pc.remoteDescription.sdp}");
        }
    };

    pc.onconnectionstatechange += async (state) =>
    {
        logger.LogDebug($"Peer connection state change to {state}.");

        if (state == RTCPeerConnectionState.connected)
        {
            await audioSource.StartAudio();
            await testPatternSource.StartVideo();
        }
        else if (state == RTCPeerConnectionState.failed)
        {
            pc.Close("ice disconnection");
        }
        else if (state == RTCPeerConnectionState.closed)
        {
            await testPatternSource.CloseVideo();
            await audioSource.CloseAudio();
        }
    };

    // Diagnostics.
    pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
    pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
    pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
    pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

    return Task.FromResult(pc);
}

Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
{
    var seriLogger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
        .WriteTo.Console()
        .CreateLogger();
    var factory = new SerilogLoggerFactory(seriLogger);
    SIPSorcery.LogFactory.Set(factory);
    return factory.CreateLogger<Program>();
}
