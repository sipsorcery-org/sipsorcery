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
using SIPSorcery.Net;
using WebSocketSharp.Server;
using SIPSorcery.Media;
using Serilog.Extensions.Logging;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;
        private const string NODE_DSS_SERVER = "http://192.168.0.50:3000";
        private const string NODE_DSS_MY_USER = "svr";
        private const string NODE_DSS_THEIR_USER = "cli";
        private const string STUN_URL = "stun:stun.sipsorcery.com";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static async Task Main()
        {
            Console.WriteLine("WebRTC Test Pattern Server Demo");

            logger = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
            Console.WriteLine("Press ctrl-c to exit.");

            //var nodeDssWebRTCPeer = new WebRTCNodeDssPeer(NODE_DSS_SERVER, NODE_DSS_MY_USER, NODE_DSS_THEIR_USER, CreatePeerConnection);
            //await nodeDssWebRTCPeer.StartSendOffer();

            //Console.WriteLine($"Waiting for node DSS peer to connect...");
            //Console.WriteLine("Press ctrl-c to exit.");

            // Ctrl-c will gracefully exit the call at any point.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } }
            };
            //var pc = new RTCPeerConnection(config);
            var pc = new RTCPeerConnection(null);

            var testPatternSource = new VideoTestPatternSource();
            //var videoEndPoint = new SIPSorceryMedia.FFmpeg.FFmpegVideoEndPoint();
            //var videoEndPoint = new SIPSorceryMedia.Windows.WindowsVideoEndPoint(true);
            //var videoEndPoint = new SIPSorceryMedia.Windows.WindowsEncoderEndPoint();
            var videoEndPoint = new SIPSorceryMedia.Encoders.VideoEncoderEndPoint();

            MediaStreamTrack track = new MediaStreamTrack(videoEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(track);

            testPatternSource.OnVideoSourceRawSample += videoEndPoint.ExternalVideoSourceRawSample;
            videoEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;
            pc.OnVideoFormatsNegotiated += (sdpFormat) => videoEndPoint.SetVideoSourceFormat(SDPMediaFormatInfo.GetVideoCodecForSdpFormat(sdpFormat.First().FormatCodec));
            
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
                    await videoEndPoint.CloseVideo();
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    await videoEndPoint.StartVideo();
                    await testPatternSource.StartVideo();
                }
            };

            // Diagnostics.
            //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

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
}
