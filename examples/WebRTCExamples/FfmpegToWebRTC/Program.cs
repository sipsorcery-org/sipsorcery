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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
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
        //private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;
        private const string FFMPEG_DEFAULT_COMMAND = "ffmpeg -re -f lavfi -i testsrc=size=640x480:rate=10 -vcodec {0} -pix_fmt yuv420p -strict experimental -g 1 -f rtp rtp://127.0.0.1:{1} -sdp_file {2}";
        private const string FFMPEG_SDP_FILE = "ffmpeg.sdp";
        private const int FFMPEG_DEFAULT_RTP_PORT = 5020;

        /// <summary>
        /// The codec to pass to ffmpeg via the command line. WebRTC supported options are:
        /// - vp8
        /// - vp9
        /// - h264
        /// Note if you change this option you will need to delete the ffmpeg.sdp file.
        /// </summary>
        private const string FFMPEG_VP8_CODEC = "vp8";
        private const string FFMPEG_VP9_CODEC = "vp9";
        private const string FFMPEG_H264_CODEC = "h264";
        private const string FFMPEG_DEFAULT_CODEC = FFMPEG_VP9_CODEC;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static WebSocketServer _webSocketServer;
        private static SDPAudioVideoMediaFormat _ffmpegVideoFormat;
        private static RTPSession _ffmpegListener;

        static async Task Main(string[] args)
        {
            string videoCodec = FFMPEG_DEFAULT_CODEC;

            if (args?.Length > 0)
            {
                switch(args[0].ToLower())
                {
                    case FFMPEG_VP8_CODEC:
                    case FFMPEG_VP9_CODEC:
                    case FFMPEG_H264_CODEC:
                        videoCodec = args[0].ToLower();
                        break;

                    default:
                        Console.WriteLine($"Video codec option not recognised. Valid values are {FFMPEG_VP8_CODEC}, {FFMPEG_VP9_CODEC} and {FFMPEG_H264_CODEC}. Using {videoCodec}.");
                        break;
                }
            }

            CancellationTokenSource exitCts = new CancellationTokenSource();

            logger = AddConsoleLogger();

            string ffmpegCommand = String.Format(FFMPEG_DEFAULT_COMMAND, videoCodec, FFMPEG_DEFAULT_RTP_PORT, FFMPEG_SDP_FILE);

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            //_webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            //_webSocketServer.SslConfiguration.ServerCertificate = new X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
            //_webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            //_webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
            _webSocketServer.AddWebSocketService<WebRtcClient>("/", (client) =>
            {
                client.WebSocketOpened += SendOffer;
                client.OnMessageReceived += WebSocketMessageReceived;
            });

            if(File.Exists(FFMPEG_SDP_FILE))
            {
                var sdp = SDP.ParseSDPDescription(File.ReadAllText(FFMPEG_SDP_FILE));
                var videoAnn = sdp.Media.Single(x => x.Media == SDPMediaTypesEnum.video);
                if(videoAnn.MediaFormats.Values.First().Name().ToLower() != videoCodec)
                {
                    logger.LogWarning($"Removing existing ffmpeg SDP file {FFMPEG_SDP_FILE} due to codec mismatch.");
                    File.Delete(FFMPEG_SDP_FILE);
                }
            }

            Console.WriteLine("Start ffmpeg using the command below and then initiate a WebRTC connection from the browser");
            Console.WriteLine(ffmpegCommand);

            if (!File.Exists(FFMPEG_SDP_FILE))
            {
                Console.WriteLine();
                Console.WriteLine($"Waiting for {FFMPEG_SDP_FILE} to appear...");
            }

            await Task.Run(() => StartFfmpegListener(FFMPEG_SDP_FILE, exitCts.Token));

            Console.WriteLine($"ffmpeg listener successfully created on port {FFMPEG_DEFAULT_RTP_PORT} with video format {_ffmpegVideoFormat.Name()}.");

            _webSocketServer.Start();

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
                _ffmpegVideoFormat = videoAnn.MediaFormats.Values.First();

                _ffmpegListener = new RTPSession(false, false, false, IPAddress.Loopback, FFMPEG_DEFAULT_RTP_PORT);
                _ffmpegListener.AcceptRtpFromAny = true;
                MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { _ffmpegVideoFormat }, MediaStreamStatusEnum.RecvOnly);
                _ffmpegListener.addTrack(videoTrack);

                _ffmpegListener.SetRemoteDescription(SIP.App.SdpType.answer, sdp);

                // Set a dummy destination end point or the RTP session will end up sending RTCP reports
                // to itself.
                var dummyIPEndPoint = new IPEndPoint(IPAddress.Loopback, 0);
                _ffmpegListener.SetDestination(SDPMediaTypesEnum.video, dummyIPEndPoint, dummyIPEndPoint);

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

        private static RTCPeerConnection Createpc(WebSocketContext context, SDPAudioVideoMediaFormat videoFormat)
        {
            var pc = new RTCPeerConnection(null);

            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPAudioVideoMediaFormat> { videoFormat }, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);

            pc.onicecandidateerror += (candidate, error) => logger.LogWarning($"Error adding remote ICE candidate. {error} {candidate}");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
            //pc.OnReceiveReport += (type, rtcp) => logger.LogDebug($"RTCP {type} report received.");
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

                    _ffmpegListener.OnRtpPacketReceived += (ep, media, rtpPkt) =>
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
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
