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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Intrinsics.Arm;
using System.Security.Cryptography.Xml;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

if (string.IsNullOrWhiteSpace(cloudflareAppID) || string.IsNullOrWhiteSpace(cloudflareAPIToken))
{
    Console.Error.WriteLine("Please set the CLOUDFLARE_APPID and CLOUDFLARE_API_TOKEN environment variables.");
    return;
}

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

// Peer connections created for a browser offer, keyed by a session id, so the browser's answer
// (posted to /api/webrtc/answer) can be matched back to the correct peer connection. The dictionary
// also keeps the peer connection referenced for the lifetime of the call so it isn't garbage collected.
var peerConnections = new ConcurrentDictionary<string, RTCPeerConnection>();

app.MapGet("/api/webrtc/offer", async (ICloudflareTurnApiClient cloudflareTurnApiClient) =>
{
    return await (await cloudflareTurnApiClient.CreateCredentialsAsync(cloudflareAppID, TURN_CREDENTIALS_TTL_SECONDS)).MatchAsync(
      RightAsync: async credentials =>
      {
          seriLogger.Debug($"Successfully retrieved TURN credentials: {JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true })}");

          var (username, credential) = GetTurnCredentials(credentials);

          var pc = await CreatePeerConnection(new RTCConfiguration
          {
              iceTransportPolicy = RTCIceTransportPolicy.relay,
              iceServers = new List<RTCIceServer> { new RTCIceServer { urls = "turn:turn.cloudflare.com:3478?transport=udp", username = username, credential = credential } },
          });

          var offer = pc.createOffer(new RTCOfferOptions
          { 
              X_WaitForIceGatheringToComplete = true
          });

          await pc.setLocalDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = offer.sdp });

          var sessionId = Guid.NewGuid().ToString();
          peerConnections[sessionId] = pc;
          pc.onconnectionstatechange += state =>
          {
              if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
              {
                  peerConnections.TryRemove(sessionId, out _);
              }
          };

          seriLogger.Debug($"Created WebRTC offer for session {sessionId}.");

          // Return the session id alongside the offer SDP so the browser can echo it back with its answer.
          return Results.Json(new { id = sessionId, sdp = offer.sdp });
      },
      Left: error =>
      {
          seriLogger.Error($"Failed to get TURN credentials: {error}");

          return Results.Problem("Failed to get TURN credentials.");
      });
});

// Receives the browser's SDP answer and applies it to the peer connection created for the matching
// session id, completing the offer/answer negotiation.
app.MapPost("/api/webrtc/answer", (AnswerRequest answer) =>
{
    if (answer is null || string.IsNullOrWhiteSpace(answer.Id) || !peerConnections.TryGetValue(answer.Id, out var pc))
    {
        return Results.NotFound("Unknown or expired WebRTC session id.");
    }

    if (string.IsNullOrWhiteSpace(answer.Sdp))
    {
        return Results.Problem("An SDP answer must be supplied.");
    }

    var setAnswerResult = pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = answer.Sdp });
    if (setAnswerResult != SetDescriptionResultEnum.OK)
    {
        seriLogger.Error($"Failed to set remote SDP answer for session {answer.Id}: {setAnswerResult}");
        return Results.Problem("Failed to set remote SDP answer on peer connection.");
    }

    seriLogger.Debug($"Remote answer applied for session {answer.Id}.");
    return Results.Ok();
});

Console.WriteLine($"Cloudflare WebRTC TURN example. Browse to {ListenUrl} once publishing has started.");

app.Run();

Task<RTCPeerConnection> CreatePeerConnection(RTCConfiguration config)
{
    var pc = new RTCPeerConnection(config);

    var vp8Codec = new VP8Codec();
    var testPatternSource = new VideoTestPatternSource(vp8Codec);
    var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

    MediaStreamTrack videoTrack = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
    pc.addTrack(videoTrack);
    MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendOnly);
    pc.addTrack(audioTrack);

    testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
    audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

    pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());
    pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());
    pc.onsignalingstatechange += () =>
    {
        seriLogger.Debug($"Signalling state change to {pc.signalingState}.");

        if (pc.signalingState == RTCSignalingState.have_local_offer)
        {
            seriLogger.Debug($"Local SDP offer:\n{pc.localDescription.sdp}");
        }
        else if (pc.signalingState == RTCSignalingState.stable)
        {
            seriLogger.Debug($"Remote SDP offer:\n{pc.remoteDescription.sdp}");
        }
    };

    pc.onconnectionstatechange += async (state) =>
    {
        seriLogger.Debug($"Peer connection state change to {state}.");

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
    pc.OnReceiveReport += (re, media, rr) => seriLogger.Debug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
    pc.OnSendReport += (media, sr) => seriLogger.Debug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
    pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => seriLogger.Debug($"STUN {msg.Header.MessageType} received from {ep}.");
    pc.oniceconnectionstatechange += (state) => seriLogger.Debug($"ICE connection state change to {state}.");

    return Task.FromResult(pc);
}

(string username, string credential) GetTurnCredentials(CloudflareIceServers servers)
{
    var username = servers.IceServers.Where(x => !string.IsNullOrWhiteSpace(x.Username)).Select(x => x.Username).FirstOrDefault() ?? string.Empty;
    var credential = servers.IceServers.Where(x => !string.IsNullOrWhiteSpace(x.Username)).Select(x => x.Credential).FirstOrDefault() ?? string.Empty;

    return (username, credential);
}

/// <summary>
/// Body for the /api/webrtc/answer endpoint. The browser echoes back the session id it received with
/// the offer, along with its SDP answer. JSON property matching is case-insensitive (id/sdp map across).
/// </summary>
record AnswerRequest(string Id, string Sdp);
