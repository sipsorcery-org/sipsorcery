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
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.FFmpeg;
using WebSocketSharp.Server;

namespace demo;

class Program
{
    private const int ASPNET_PORT = 8080;
    private const int WEBSOCKET_PORT = 8081;
    //private const string STUN_URL = "stun:stun.cloudflare.com";
    private const string LINUX_FFMPEG_LIB_PATH = "/usr/local/lib/";

    private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

    static void Main()
    {
        Console.WriteLine("WebRTC FFmpeg Get Started");

        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, LINUX_FFMPEG_LIB_PATH, logger);
        }
        else
        {
            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);
        }

        logger = AddConsoleLogger();

        // Start web socket.
        Console.WriteLine("Starting web socket server...");
        var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
        webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/ws", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
        webSocketServer.Start();

        Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
        Console.WriteLine("Press ctrl-c to exit.");

        var builder = WebApplication.CreateBuilder();

        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Any, ASPNET_PORT);
        });

        var app = builder.Build();

        // Map the root URL (/) to return "Hello World"
        ///app.MapGet("/", () => "Hello World");

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.Run();
    }

    private static Task<RTCPeerConnection> CreatePeerConnection()
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

        // To test closing.
        //_ = Task.Run(async () => 
        //{ 
        //    await Task.Delay(5000);

        //    audioSource.OnAudioSourceEncodedSample -= pc.SendAudio;
        //    videoEncoderEndPoint.OnVideoSourceEncodedSample -= pc.SendVideo;

        //    logger.LogDebug("Closing peer connection.");
        //    pc.Close("normal");
        //});

        return Task.FromResult(pc);
    }

    /// <summary>
    ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
    /// </summary>
    private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
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
}
