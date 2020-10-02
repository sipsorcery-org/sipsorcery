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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Encoders.Codecs;
using Serilog.Extensions.Logging;
using WebSocketSharp.Server;
using static OpenH264Lib.Encoder;
using Encoder = OpenH264Lib.Encoder;

namespace demo
{
    public class H264Codec
    {
        private Encoder H264Encoder;

        public H264Codec(int width, int height, int fps, OnEncodeCallback onEncode)
        {
            this.H264Encoder = new Encoder("openh264-2.1.1-win64.dll");
            this.H264Encoder.Setup(width, height, width * height * 3 * 8, fps, 2.0F, onEncode);
        }

        public void EncodeImage(byte[] yuvFrameBuffer)
        {
            this.H264Encoder.Encode(yuvFrameBuffer);
        }
    }

    class Program
    {
        private const int WEBSOCKET_PORT = 8081;

        private const int WIDTH = 640;
        private const int HEIGHT = 480;
        private const int FPS = 30;
        private const int RTP_TIMESTMAMPSPACING = 3000; // @30fps.

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static void Main()
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
            var pc = new RTCPeerConnection(null);

            var testPatternSource = new VideoTestPatternSource();
            OpenH264Lib.Encoder.OnEncodeCallback onEncoded = (data, len, fType) => pc.SendVideo(RTP_TIMESTMAMPSPACING, data);
            var codec = new H264Codec(WIDTH, HEIGHT, FPS, onEncoded);

            MediaStreamTrack track = new MediaStreamTrack(new List<VideoCodecsEnum>() { VideoCodecsEnum.H264 }, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(track);

            testPatternSource.OnVideoSourceRawSample += (uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            {
                var i420 = PixelConverter.BGRtoI420(sample, width, height);
                codec.EncodeImage(i420);
            };

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
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    await testPatternSource.StartVideo();
                }
            };

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
