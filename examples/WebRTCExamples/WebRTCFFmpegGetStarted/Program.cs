//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that attempts to send and
// receive audio and video. This example attempts to use the ffmpeg libraries for
// the video encoding. A web socket is used for signalling.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 06 Apr 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.FFmpeg;

namespace demo;

class Program
{
    private const string LINUX_FFMPEG_LIB_PATH = "/usr/local/lib/";

    private static string _stunUrl = string.Empty;
    private static bool _waitForIceGatheringToSendOffer = false;
    private static int _webrtcBindPort = 0;

    static async Task Main()
    {
        Console.WriteLine("WebRTC FFmpeg Get Started");

        _stunUrl = Environment.GetEnvironmentVariable("STUN_URL");
        bool.TryParse(Environment.GetEnvironmentVariable("WAIT_FOR_ICE_GATHERING_TO_SEND_OFFER"), out _waitForIceGatheringToSendOffer);
        int.TryParse(Environment.GetEnvironmentVariable("BIND_PORT"), out _webrtcBindPort);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);
        var programLogger = factory.CreateLogger<Program>();

        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, LINUX_FFMPEG_LIB_PATH, programLogger);
        }
        else
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, programLogger);
        }

        programLogger.LogDebug(_stunUrl != null ? $"STUN URL: {_stunUrl}" : "No STUN URL provided.");
        programLogger.LogDebug($"Wait for ICE gathering to send offer: {_waitForIceGatheringToSendOffer}");

        var builder = WebApplication.CreateBuilder();

        builder.Host.UseSerilog();

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        var webSocketOptions = new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromMinutes(2)
        };

        app.UseWebSockets(webSocketOptions);

        app.Map("/ws", async context =>
        {
            programLogger.LogDebug("Web socket client connection established.");

            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();

                var webSocketPeer = new WebRTCWebSocketPeerAspNet(webSocket,
                    CreatePeerConnection,
                    RTCSdpType.offer,
                    programLogger);

                webSocketPeer.OfferOptions = new RTCOfferOptions
                {
                    X_WaitForIceGatheringToComplete = _waitForIceGatheringToSendOffer
                };

                await webSocketPeer.Run();

                await webSocketPeer.Close();
            }
            else
            {
                // Not a WebSocket request
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });

        await app.RunAsync();
    }

    private static Task<RTCPeerConnection> CreatePeerConnection(Microsoft.Extensions.Logging.ILogger logger)
    {
        RTCConfiguration config = new RTCConfiguration
        {
            X_ICEIncludeAllInterfaceAddresses = true
        };

        if (!string.IsNullOrWhiteSpace(_stunUrl))
        {
            config.iceServers = new List<RTCIceServer> { new RTCIceServer { urls = _stunUrl } };
        }

        var pc = new RTCPeerConnection(config, _webrtcBindPort);

        var testPatternSource = new VideoTestPatternSource(new FFmpegVideoEncoder());
        var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

        MediaStreamTrack videoTrack = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
        pc.addTrack(videoTrack);
        MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
        pc.addTrack(audioTrack);

        testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
        audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

        pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());
        pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());
        pc.onicegatheringstatechange += (state) => logger.LogDebug($"ICE gathering state changed to {state}."); ;
        pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");

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
}
