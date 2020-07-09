//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Listens on an RTP socket for a feed from ffmpeg and forwards it
// to a WebRTC peer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 08 Jul 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace SIPSorcery.Examples
{
    public class WebRtcClient : WebSocketBehavior
    {
        public RTCPeerConnection pc;

        public event Func<WebSocketContext, Task<RTCPeerConnection>> WebSocketOpened;
        public event Action<WebSocketContext, RTCPeerConnection, string> OnMessageReceived;

        public WebRtcClient()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnMessageReceived(this.Context, pc, e.Data);
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            pc = await WebSocketOpened(this.Context);
        }
    }

    class Program
    {
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;
        private const string FFMPEG_DEFAULT_COMMAND = "ffmpeg -re -f lavfi -i testsrc=size=640x480:rate=10 -vcodec {0} -strict experimental -g 1 -f rtp rtp://127.0.0.1:{1} -sdp_file {2}";
        private const string FFMPEG_SDP_FILE = "ffmpeg.sdp";
        private const int FFMPEG_DEFAULT_RTP_PORT = 5020;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static SDPMediaFormat _ffmpegVideoFormat;
        private static RTPSession _ffmpegListener;

        private static string _ffmpegCommand = String.Format(FFMPEG_DEFAULT_COMMAND, "vp8", FFMPEG_DEFAULT_RTP_PORT, FFMPEG_SDP_FILE);

        static async Task Main()
        {
            CancellationTokenSource exitCts = new CancellationTokenSource();

            AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            _webSocketServer.SslConfiguration.ServerCertificate = new X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
            _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            //_webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
            _webSocketServer.AddWebSocketService<WebRtcClient>("/", (client) =>
            {
                client.WebSocketOpened += SendOffer;
                client.OnMessageReceived += WebSocketMessageReceived;
            });
            _webSocketServer.Start();

            Console.WriteLine("Start ffmpeg using the command below and then initiate a WebRTC connection from the browser");
            Console.WriteLine(_ffmpegCommand);

            Console.WriteLine();
            Console.WriteLine($"Waiting for {FFMPEG_SDP_FILE} to appear...");
            await Task.Run(() => StartFfmpegListener(FFMPEG_SDP_FILE, exitCts.Token));

            Console.WriteLine($"ffmpeg listener successfully created on port {FFMPEG_DEFAULT_RTP_PORT} with video format {_ffmpegVideoFormat.Name}.");

            Console.WriteLine();
            Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            await Task.Run(() => OnKeyPress(exitCts.Token));

            _webSocketServer.Stop();
        }

        private static async Task StartFfmpegListener(string sdpPath, CancellationToken cancel)
        {
            while (!File.Exists(FFMPEG_SDP_FILE) && !cancel.IsCancellationRequested)
            {
                await Task.Delay(500);
            }

            if (!cancel.IsCancellationRequested)
            {
                var sdp = SDP.ParseSDPDescription(File.ReadAllText(FFMPEG_SDP_FILE));

                // The SDP is only expected to contain a single video media announcement.
                var videoAnn = sdp.Media.Single(x => x.Media == SDPMediaTypesEnum.video);

                _ffmpegVideoFormat = videoAnn.MediaFormats.First();

                _ffmpegListener = new RTPSession(false, false, false, IPAddress.Loopback, FFMPEG_DEFAULT_RTP_PORT);
                MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { _ffmpegVideoFormat }, MediaStreamStatusEnum.RecvOnly);
                _ffmpegListener.addTrack(videoTrack);

                await _ffmpegListener.Start();
            }
        }

        private static Task OnKeyPress(CancellationToken exit)
        {
            while (!exit.WaitHandle.WaitOne(0))
            {
                var keyProps = Console.ReadKey();

                if (keyProps.KeyChar == 'q')
                {
                    // Quit application.
                    Console.WriteLine("Quitting");
                    break;
                }
            }

            return Task.CompletedTask;
        }

        private static async Task<RTCPeerConnection> SendOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}, sending offer.");

            var pc = Createpc(context, _ffmpegVideoFormat);

            var offerInit = pc.createOffer(null);
            await pc.setLocalDescription(offerInit);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            context.WebSocket.Send(offerInit.sdp);

            return pc;
        }

        private static RTCPeerConnection Createpc(WebSocketContext context, SDPMediaFormat videoFormat)
        {
            var pc = new RTCPeerConnection(null);

            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { videoFormat }, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);

            pc.onicecandidateerror += (candidate, error) => logger.LogWarning($"Error adding remote ICE candidate. {error} {candidate}");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
            pc.OnReceiveReport += (type, rtcp) => logger.LogDebug($"RTCP {type} report received.");
            pc.OnRtcpBye += (reason) => logger.LogDebug($"RTCP BYE receive, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");
            pc.OnRtpClosed += (reason) => logger.LogDebug($"Peer connection closed, reason: {(string.IsNullOrWhiteSpace(reason) ? "<none>" : reason)}.");

            pc.onicecandidate += (candidate) =>
            {
                if (pc.signalingState == RTCSignalingState.have_local_offer ||
                    pc.signalingState == RTCSignalingState.have_remote_offer)
                {
                    context.WebSocket.Send($"candidate:{candidate}");
                }
            };

            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    logger.LogDebug("Creating RTP session to receive ffmpeg stream.");

                    _ffmpegListener.OnRtpPacketReceived += (media, rtpPkt) =>
                    {
                        if (media == SDPMediaTypesEnum.video && pc.VideoDestinationEndPoint != null)
                        {
                            //logger.LogDebug($"Forwarding {media} RTP packet to webrtc peer timestamp {rtpPkt.Header.Timestamp}.");
                            pc.SendRtpRaw(media, rtpPkt.Payload, rtpPkt.Header.Timestamp, rtpPkt.Header.MarkerBit, rtpPkt.Header.PayloadType);
                        }
                    };
                }
            };

            return pc;
        }

        private static void WebSocketMessageReceived(WebSocketContext context, RTCPeerConnection pc, string message)
        {
            try
            {
                if (pc.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                }
                else
                {
                    logger.LogDebug("ICE Candidate: " + message);

                    if (string.IsNullOrWhiteSpace(message) || message.Trim().ToLower() == SDP.END_ICE_CANDIDATES_ATTRIBUTE)
                    {
                        logger.LogDebug("End of candidates message received.");
                    }
                    else
                    {
                        var candInit = Newtonsoft.Json.JsonConvert.DeserializeObject<RTCIceCandidateInit>(message);
                        pc.addIceCandidate(candInit);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebSocketMessageReceived. " + excp.Message);
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}
