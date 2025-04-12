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
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
    private const int ASPNET_PORT = 8080;
    //private const string STUN_URL = "stun:stun.cloudflare.com";
    private const string LINUX_FFMPEG_LIB_PATH = "/usr/local/lib/";

    private static List<RTCPeerConnection> _peerConnections = new List<RTCPeerConnection>();

    static void Main()
    {
        Console.WriteLine("WebRTC FFmpeg Get Started");

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
            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, LINUX_FFMPEG_LIB_PATH, programLogger);
        }
        else
        {
            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, programLogger);
        }

        var builder = WebApplication.CreateBuilder();

        builder.Host.UseSerilog();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, ASPNET_PORT);
        });

        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseWebSockets();

        app.Map("/ws", async context =>
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                var webSocketPeer = new WebRTCWebSocketPeerAspNet(webSocket, x => CreatePeerConnection(Log.Logger as Microsoft.Extensions.Logging.ILogger), RTCSdpType.offer);
                await webSocketPeer.Start();

                // Set the status code to 200 OK
                context.Response.StatusCode = StatusCodes.Status200OK;
            }
            else
            {
                // Set the status code to 400 Bad Request if it's not a WebSocket request
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
        });

        app.Run();
    }

    private static Task<RTCPeerConnection> CreatePeerConnection(Microsoft.Extensions.Logging.ILogger logger)
    {
        RTCConfiguration config = new RTCConfiguration
        {
            //iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
            X_BindAddress = IPAddress.Any // Docker images typically don't support IPv6 so force bind to IPv4.
        };
        var pc = new RTCPeerConnection(config);

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
