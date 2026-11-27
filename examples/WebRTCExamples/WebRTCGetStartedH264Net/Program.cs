//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Sends a test pattern to a browser over WebRTC, encoded as H.264
// by SIPSorcery.H264, the pure C# port of OpenH264, so no native libraries or
// devices are needed. Run it, open getstarted.html in a browser and press
// Start. A web socket is used for signalling. Modelled on the WebRTCGetStarted
// example, which does the same with VP8.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E8), from WebRTCGetStarted.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.H264;
using SIPSorcery.Media;
using SIPSorcery.Net;
using WebSocketSharp.Server;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;
        private const string STUN_URL = "stun:stun.cloudflare.com";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("WebRTC Get Started H.264 (SIPSorcery.H264 encoder)");

            logger = AddConsoleLogger();

            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
            Console.WriteLine("Open getstarted.html in a browser and press Start. Press ctrl-c to exit.");

            var exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                exitMre.Set();
            };
            exitMre.WaitOne();

            webSocketServer.Stop();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
            };
            var pc = new RTCPeerConnection(config);

            // The codec encodes the test pattern's I420 frames straight to H.264 access units. It
            // offers Constrained Baseline (profile-level-id=42e01f), which every browser decodes.
            var h264Codec = new H264Codec
            {
                TargetBitrateKbps = 1000,
                // A key frame every two seconds; a receiver that loses one also asks for one (below).
                KeyframeIntervalFrames = 60,
            };
            var testPatternSource = new VideoTestPatternSource(h264Codec);

            var videoTrack = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);

            testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
            pc.OnVideoFormatsNegotiated += (formats) =>
            {
                logger.LogInformation("Video format negotiated: {Format}.", formats.First());
                testPatternSource.SetVideoSourceFormat(formats.First());
            };

            // A browser that joins late or loses data asks for a key frame with an RTCP PLI (or FIR).
            pc.OnReceiveReport += (ep, media, report) =>
            {
                if (report.FeedbackMessages.Any(f => f.Header.PacketType == RTCPReportTypesEnum.PSFB &&
                        (f.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.PLI ||
                         f.Header.PayloadFeedbackMessageType == PSFBFeedbackTypesEnum.FIR)))
                {
                    logger.LogInformation("The browser asked for a key frame.");
                    testPatternSource.ForceKeyFrame();
                }
            };

            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug("Peer connection state change to {State}.", state);

                if (state == RTCPeerConnectionState.connected)
                {
                    await testPatternSource.StartVideo();
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await testPatternSource.CloseVideo();
                    h264Codec.Dispose();
                }
            };

            pc.oniceconnectionstatechange += (state) => logger.LogDebug("ICE connection state change to {State}.", state);

            return Task.FromResult(pc);
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
