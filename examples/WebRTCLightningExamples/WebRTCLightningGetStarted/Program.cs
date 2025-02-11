//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that serves a test pattern
// video stream to a WebRTC enabled browser.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 17 Jan 2020	Aaron Clauson	Created, Dublin, Ireland.
// 27 Jan 2021  Aaron Clauson   Switched from node-dss to REST signaling.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using WebSocketSharp.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;

namespace demo;

class Program
{
    private const int WEBSOCKET_PORT = 8081;
    private const int TEST_PATTERN_FRAMES_PER_SECOND = 5; //30;

    private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

    private static int _frameCount = 0;
    private static DateTime _startTime;

    static void Main(string[] args)
    {
        Console.WriteLine("WebRTC Lightning Demo");

        //Log.Logger = new LoggerConfiguration()
        //    .WriteTo.Console()
        //    .CreateBootstrapLogger();

        //Log.Information("Starting ASP.NET server...");

        StartWebSocketServer();

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((context, services, config) =>
        {
            config.WriteTo.Console()
                .Enrich.FromLogContext();
        });

        var app = builder.Build();
        //app.UseSerilogRequestLogging();

        app.MapGet("/", async context =>
        {
            await context.Response.WriteAsync("WebRTC API is running!");
        });

        app.Run();
    }

    private static void StartWebSocketServer()
    {
        // Start web socket.
        Console.WriteLine("Starting web socket server...");
        var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
        webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = () => CreatePeerConnection());
        webSocketServer.Start();

        Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
    }

    private static Task<RTCPeerConnection> CreatePeerConnection()
    {
        //RTCConfiguration config = new RTCConfiguration
        //{
        //    iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
        //    certificates = new List<RTCCertificate> { new RTCCertificate { Certificate = cert } }
        //};
        //var pc = new RTCPeerConnection(config);
        var pc = new RTCPeerConnection(null);

        //var testPatternSource = new VideoTestPatternSource(new SIPSorceryMedia.Encoders.VideoEncoder());
        SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);
        var testPatternSource = new VideoTestPatternSource(new FFmpegVideoEncoder());
        testPatternSource.SetFrameRate(TEST_PATTERN_FRAMES_PER_SECOND);
        //testPatternSource.SetMaxFrameRate(true);
        //var videoEndPoint = new SIPSorceryMedia.FFmpeg.FFmpegVideoEndPoint();
        //videoEndPoint.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);
        //testPatternSource.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);
        //var videoEndPoint = new SIPSorceryMedia.Windows.WindowsEncoderEndPoint();
        //var videoEndPoint = new SIPSorceryMedia.Encoders.VideoEncoderEndPoint();

        MediaStreamTrack track = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(track);

        //testPatternSource.OnVideoSourceRawSample += videoEndPoint.ExternalVideoSourceRawSample;
        testPatternSource.OnVideoSourceRawSample += MesasureTestPatternSourceFrameRate;
        testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());

        pc.onconnectionstatechange += async (state) =>
        {
            logger.LogDebug($"Peer connection state change to {state}.");

            if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                await testPatternSource.CloseVideo();
                testPatternSource.Dispose();
            }
            else if (state == RTCPeerConnectionState.connected)
            {
                await testPatternSource.StartVideo();
            }
        };

        // Diagnostics.
        //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
        //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
        //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
        pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
        pc.onsignalingstatechange += () =>
        {
            if (pc.signalingState == RTCSignalingState.have_local_offer)
            {
                logger.LogDebug($"Local SDP set, type {pc.localDescription.type}.");
                logger.LogDebug(pc.localDescription.sdp.ToString());
            }
            else if (pc.signalingState == RTCSignalingState.have_remote_offer)
            {
                logger.LogDebug($"Remote SDP set, type {pc.remoteDescription.type}.");
                logger.LogDebug(pc.remoteDescription.sdp.ToString());
            }
        };

        return Task.FromResult(pc);
    }

    private static void MesasureTestPatternSourceFrameRate(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        if (_startTime == DateTime.MinValue)
        {
            _startTime = DateTime.Now;
        }

        _frameCount++;

        if (DateTime.Now.Subtract(_startTime).TotalSeconds > 5)
        {
            double fps = _frameCount / DateTime.Now.Subtract(_startTime).TotalSeconds;
            Console.WriteLine($"Frame rate {fps:0.##}fps.");
            _startTime = DateTime.Now;
            _frameCount = 0;
        }
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
