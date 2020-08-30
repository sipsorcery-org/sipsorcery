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
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.FFmpeg;
using SIPSorceryMedia.Windows;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;
using FFmpeg.AutoGen;
using SIPSorcery.Sys;

namespace WebRTCServer
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
            PeerConnection.Close("remote party close");
        }
    }

    class Program
    {
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;

        private static SDPMediaFormatsEnum VIDEO_CODEC = SDPMediaFormatsEnum.VP8;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static bool _forceKeyFrame = false;
        private static long _presentationTimestamp = 0;

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

            AddConsoleLogger();

            //InitialiseTestPattern();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            //_webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            //_webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
            //_webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            //_webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
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
                //_sendTestPatternTimer?.Dispose();
                //windowsVideoEndPoint.CloseVideo();
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static async Task<RTCPeerConnection> SendSDPOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}.");

            WindowsVideoEndPoint windowsVideoEndPoint = new WindowsVideoEndPoint();

            var pc = new RTCPeerConnection(null);

            if (VIDEO_CODEC == SDPMediaFormatsEnum.VP8)
            {
                MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false,
                    new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) },
                    MediaStreamStatusEnum.SendOnly);
                pc.addTrack(videoTrack);
            }
            else if (VIDEO_CODEC == SDPMediaFormatsEnum.H264)
            {
                MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false,
                new List<SDPMediaFormat>
                {
                    new SDPMediaFormat(SDPMediaFormatsEnum.H264)
                    {
                        FormatParameterAttribute = $"packetization-mode=1",
                    }
                },
                MediaStreamStatusEnum.SendOnly);
                pc.addTrack(videoTrack);
            }
            else
            {
                throw new ApplicationException($"Video codec {VIDEO_CODEC} is not supported.");
            }

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

                    await windowsVideoEndPoint.CloseVideo();
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    windowsVideoEndPoint.OnVideoSourceEncodedSample += (videoFormat, durationRtpUnits, sample) =>
                    {
                        if (videoFormat.Codec == VideoCodecsEnum.H264)
                        {
                            pc.SendH264Frame(durationRtpUnits, videoFormat.PayloadID, sample);
                        }
                        else if (videoFormat.Codec == VideoCodecsEnum.VP8)
                        {
                            pc.SendVp8Frame(durationRtpUnits, videoFormat.PayloadID, sample);
                        }
                        else
                        {
                            logger.LogWarning($"An encoded video sample was received for {videoFormat.Codec} but there's no RTP send method.");
                        }
                    };
                    await windowsVideoEndPoint.StartVideo();
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
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
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
