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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia.Windows;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;
using SIPSorcery.Media;
using Windows.UI.WebUI;
using Serilog.Extensions.Logging;

namespace demo
{
    public class SDPExchange : WebSocketBehavior
    {
        public RTCPeerConnection PeerConnection;

        public event Func<WebSocketContext, Task<RTCPeerConnection>> WebSocketOpened;
        public event Action<RTCPeerConnection, string> OnMessageReceived;

        public SDPExchange()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnMessageReceived(PeerConnection, e.Data);
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            PeerConnection = await WebSocketOpened(this.Context);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            base.OnClose(e);
            PeerConnection?.Close("remote party close");
        }
    }

    class Program
    {
        private const int WEBSOCKET_PORT = 8081;
        private static SDPMediaFormatsEnum VIDEO_CODEC = SDPMediaFormatsEnum.VP8;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static WebSocketServer _webSocketServer;

        static void Main(string[] args)
        {
            Console.WriteLine("WebRTC Test Pattern Server Demo");
            Console.WriteLine("Press ctrl-c to exit.");

            if (args?.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
            {
                string arg0 = args[0];
                if (arg0.ToLower() == "vp8")
                {
                    VIDEO_CODEC = SDPMediaFormatsEnum.VP8;
                }
                else if (arg0.ToLower() == "h264")
                {
                    VIDEO_CODEC = SDPMediaFormatsEnum.H264;
                }
                else
                {
                    Console.WriteLine($"Codec option {arg0} not recognised.");
                }
            }

            Console.WriteLine($"Selected video codec {VIDEO_CODEC}.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            logger = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            _webSocketServer.AddWebSocketService<SDPExchange>("/", (sdpExchanger) =>
            {
                sdpExchanger.WebSocketOpened += SendSDPOffer;
                sdpExchanger.OnMessageReceived += WebSocketMessageReceived;
            });
            _webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static async Task<RTCPeerConnection> SendSDPOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}.");

            var pc = new RTCPeerConnection(null);

            var testPatternSource = new VideoTestPatternSource();
            WindowsVideoEndPoint windowsVideoEndPoint = new WindowsVideoEndPoint(true);

            MediaStreamTrack track = new MediaStreamTrack(windowsVideoEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(track);

            testPatternSource.OnVideoSourceRawSample += windowsVideoEndPoint.ExternalVideoSourceRawSample;
            windowsVideoEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;

            pc.OnVideoFormatsNegotiated += (sdpFormat) =>
                windowsVideoEndPoint.SetVideoSourceFormat(SDPMediaFormatInfo.GetVideoCodecForSdpFormat(sdpFormat.First().FormatCodec));
            //pc.OnReceiveReport += RtpSession_OnReceiveReport;
            //pc.OnSendReport += RtpSession_OnSendReport;
            pc.OnTimeout += (mediaType) => pc.Close("remote timeout");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    pc.Close("remote disconnection");
                }

                if (state == RTCPeerConnectionState.closed)
                {
                    // OnTestPatternSampleReady -= pc.SendMedia;
                    pc.OnReceiveReport -= RtpSession_OnReceiveReport;
                    pc.OnSendReport -= RtpSession_OnSendReport;

                    await testPatternSource.CloseVideo();
                    await windowsVideoEndPoint.CloseVideo();
                   
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    await windowsVideoEndPoint.StartVideo();
                    await testPatternSource.StartVideo();
                }
            };

            var offerSdp = pc.createOffer(null);
            await pc.setLocalDescription(offerSdp);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");
            logger.LogDebug(offerSdp.sdp);

            context.WebSocket.Send(offerSdp.sdp);

            return pc;
        }

        private static void WebSocketMessageReceived(RTCPeerConnection pc, string message)
        {
            try
            {
                if (pc.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    var res = pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                    if(res != SetDescriptionResultEnum.OK)
                    {
                        logger.LogWarning($"Failed to set remote description {res}.");
                    }
                }
                else
                {
                    logger.LogDebug("ICE Candidate: " + message);
                    pc.addIceCandidate(new RTCIceCandidateInit { candidate = message });
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebSocketMessageReceived. " + excp.Message);
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP sent BYE {mediaType}.");
            }
            else if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                logger.LogDebug($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                if (sentRtcpReport.ReceiverReport.ReceptionReports?.Count > 0)
                {
                    var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                    logger.LogDebug($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
                }
                else
                {
                    logger.LogDebug($"RTCP sent RR {mediaType}, no packets sent or received.");
                }
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            if (recvRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP recv BYE {mediaType}.");
            }
            else
            {
                var rr = recvRtcpReport.ReceiverReport.ReceptionReports.FirstOrDefault();
                if (rr != null)
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
                }
                else
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
                }
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(logger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
