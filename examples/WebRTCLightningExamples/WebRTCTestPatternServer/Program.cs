﻿//-----------------------------------------------------------------------------
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
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using WebSocketSharp.Server;

namespace demo
{
    public class Options
    {
        [Option("rest", Required = false,
            HelpText = "Address and ID's for a REST, simple HTTP signalling, server to exchange SDP and ice candidates. Format \"--rest=https://localhost:5001/api/webrtcsignal;myid;theirid\".")]
        public string RestSignalingServer { get; set; }
    }

    class Program
    {
        // Install with: winget install "FFmpeg (Shared)" 
        private const string ffmpegLibFullPath = null; //@"C:\ffmpeg-4.4.1-full_build-shared\bin"; //  /!\ A valid path to FFmpeg library

        private const int WEBSOCKET_PORT = 8081;
        private const string STUN_URL = "stun:stun.sipsorcery.com";
        private const int TEST_PATTERN_FRAMES_PER_SECOND = 5; //30;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static int _frameCount = 0;
        private static DateTime _startTime;

        static async Task Main(string[] args)
        {
            Console.WriteLine("WebRTC Test Pattern Server Demo");

            logger = AddConsoleLogger();

            CancellationTokenSource cts = new CancellationTokenSource();

            var parseResult = Parser.Default.ParseArguments<Options>(args);
            var options = (parseResult as Parsed<Options>)?.Value;

            //X509Certificate2 cert = new X509Certificate2("localhost.pfx", "", X509KeyStorageFlags.Exportable);
            //if (cert == null)
            //{
            //    Console.WriteLine("Could not load certificate file.");
            //}
            //else
            //{
            //    Console.WriteLine($"Certificate file successfully loaded {cert.Thumbprint}, have private key {cert.HasPrivateKey}.");
            //}
            X509Certificate2 cert = null;

            if (options?.RestSignalingServer == null)
            {
                // Start web socket.
                Console.WriteLine("Starting web socket server...");
                var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
                webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = () => CreatePeerConnection(cert));
                webSocketServer.Start();

                Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
                Console.WriteLine("Press ctrl-c to exit.");
            }
            else
            {
                string[] fields = options.RestSignalingServer.Split(';');
                if (fields.Length < 3)
                {
                    throw new ArgumentException("The 'rest' option must contain 3 semi-colon separated fields, e.g. --rest=https://localhost:5001/api/webrtcsignal;myid;theirid.");
                }

                Console.WriteLine($"Connecting to REST signaling server at {fields[0]}, our ID={fields[1]}, their ID={fields[2]}.");

                var restSignalingPeer = new WebRTCRestSignalingPeer(fields[0], fields[1], fields[2], () => CreatePeerConnection(cert));
                await restSignalingPeer.Start(cts);

                Console.WriteLine($"Waiting for remote REST signaling peer to connect...");
                Console.WriteLine("Press ctrl-c to exit.");
            }

            // Ctrl-c will gracefully exit the call at any point.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                cts.Cancel();
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection(X509Certificate2 cert)
        {
            //RTCConfiguration config = new RTCConfiguration
            //{
            //    iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
            //    certificates = new List<RTCCertificate> { new RTCCertificate { Certificate = cert } }
            //};
            //var pc = new RTCPeerConnection(config);
            var pc = new RTCPeerConnection(null);

            //var testPatternSource = new VideoTestPatternSource(new SIPSorceryMedia.Encoders.VideoEncoder());
            SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, ffmpegLibFullPath, logger);
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
                if(pc.signalingState == RTCSignalingState.have_local_offer)
                {
                    logger.LogDebug($"Local SDP set, type {pc.localDescription.type}.");
                    logger.LogDebug(pc.localDescription.sdp.ToString());
                }
                else if(pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    logger.LogDebug($"Remote SDP set, type {pc.remoteDescription.type}.");
                    logger.LogDebug(pc.remoteDescription.sdp.ToString());
                }
            };

            return Task.FromResult(pc);
        }

        private static void MesasureTestPatternSourceFrameRate(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            if(_startTime == DateTime.MinValue)
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
}
