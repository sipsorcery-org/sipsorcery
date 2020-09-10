//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Displays a VP8 video stream received from a WebRTC peer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 05 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Windows;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace demo
{
    public class SDPExchange : WebSocketBehavior
    {
        public RTCPeerConnection PeerConnection;

        public event Func<WebSocketContext, RTCPeerConnection> WebSocketOpened;
        public event Action<WebSocketContext, RTCPeerConnection, string> OnMessageReceived;

        public SDPExchange()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnMessageReceived(this.Context, PeerConnection, e.Data);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            PeerConnection = WebSocketOpened(this.Context);
        }

        protected override void OnClose(CloseEventArgs e)
        {
            base.OnClose(e);
            PeerConnection.Close("remote party close");
        }
    }

    class Program
    {
        //private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;

        private static WebSocketServer _webSocketServer;
        private static Form _form;
        private static PictureBox _picBox;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        [STAThread]
        static void Main(string[] args)
        {
            logger = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, false);
            //_webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
            //_webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            _webSocketServer.AddWebSocketService<SDPExchange>("/", (sdpExchanger) =>
            {
                sdpExchanger.WebSocketOpened += WebSocketOpened;
                sdpExchanger.OnMessageReceived += WebSocketMessageReceived;
            });
            _webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");

            // Open a Window to display the video feed from the WebRTC peer.
            _form = new Form();
            _form.AutoSize = true;
            _form.BackgroundImageLayout = ImageLayout.Center;
            _picBox = new PictureBox
            {
                Size = new Size(640, 480),
                Location = new Point(0, 0),
                Visible = true
            };
            _form.Controls.Add(_picBox);

            Application.EnableVisualStyles();
            Application.Run(_form);
        }

        private static RTCPeerConnection WebSocketOpened(WebSocketContext context)
        {
            WindowsVideoEndPoint winVideoEP = new WindowsVideoEndPoint();
            winVideoEP.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                _form.BeginInvoke(new Action(() =>
                {
                    unsafe
                    {
                        fixed (byte* s = bmp)
                        {
                            System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap((int)width, (int)height, (int)(bmp.Length / height), System.Drawing.Imaging.PixelFormat.Format24bppRgb, (IntPtr)s);
                            _picBox.Image = bmpImage;
                        }
                    }
                }));
            };

            var peerConnection = new RTCPeerConnection(null);

            // Add local recvonly tracks. This ensures that the SDP answer includes only
            // the codecs we support.
            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(winVideoEP.GetVideoSinkFormats(), MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(videoTrack);

            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;
            peerConnection.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) =>
            {
                bool hasUseCandidate = msg.Attributes.Any(x => x.AttributeType == STUNAttributeTypesEnum.UseCandidate);
                Console.WriteLine($"STUN {msg.Header.MessageType} received from {ep}, use candidate {hasUseCandidate}.");
            };
            peerConnection.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state changed to {state}.");
            peerConnection.onconnectionstatechange += async (state) =>
            {
                Console.WriteLine($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.closed)
                {
                    await winVideoEP.CloseVideo();
                }
            };
            //peerConnection.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            //{
            //    //logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}.");

            //    if (media == SDPMediaTypesEnum.video)
            //    {
            //        winVideoEP.GotVideoRtp(rep, rtpPkt.Header.SyncSource, rtpPkt.Header.SequenceNumber, rtpPkt.Header.Timestamp, rtpPkt.Header.PayloadType, rtpPkt.Header.MarkerBit == 1, rtpPkt.Payload);
            //    }
            //};
            peerConnection.OnVideoFrameReceived += winVideoEP.GotVideoFrame;

            return peerConnection;
        }

        private static async void WebSocketMessageReceived(WebSocketContext context, RTCPeerConnection peerConnection, string msg)
        {
            if (peerConnection.RemoteDescription != null)
            {
                Console.WriteLine($"ICE Candidate: {msg}.");
                //await _peerConnections[0].addIceCandidate(new RTCIceCandidateInit { candidate = msg });

                //  await peerConnection.addIceCandidate(new RTCIceCandidateInit { candidate = msg });
                Console.WriteLine("add ICE candidate complete.");
            }
            else
            {
                //Console.WriteLine($"websocket recv: {msg}");
                //var offerSDP = SDP.ParseSDPDescription(msg);
                Console.WriteLine($"offer sdp: {msg}");

                peerConnection.setRemoteDescription(new RTCSessionDescriptionInit { sdp = msg, type = RTCSdpType.offer });

                var answerInit = peerConnection.createAnswer(null);
                await peerConnection.setLocalDescription(answerInit);

                Console.WriteLine($"answer sdp: {answerInit.sdp}");

                context.WebSocket.Send(answerInit.sdp);
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                Console.WriteLine($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                Console.WriteLine($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTCPCompoundPacket report)
        {
            //var rr = recvRtcpReport.ReceiverReport.ReceptionReports.FirstOrDefault();
            //if (rr != null)
            //{
            //    Console.WriteLine($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
            //}
            //else
            //{
            //    Console.WriteLine($"RTCP {mediaType} Receiver Report: empty.");
            //}

            ReceptionReportSample rrs = null;
            string cname = null;

            if (report.SDesReport != null)
            {
                cname = report.SDesReport.CNAME;
            }

            if (report.SenderReport != null)
            {
                var sr = report.SenderReport;
                rrs = report.SenderReport.ReceptionReports.FirstOrDefault();
                Console.WriteLine($"RTCP recv SR {mediaType}, ssrc {sr.SSRC}, packets sent {sr.PacketCount}, bytes sent {sr.OctetCount}, (cname={cname}).");
            }

            if (report.ReceiverReport != null)
            {
                rrs = report.ReceiverReport.ReceptionReports.FirstOrDefault();
            }

            if (rrs != null)
            {
                Console.WriteLine($"RTCP recv RR {mediaType}, ssrc {rrs.SSRC}, pkts lost {rrs.PacketsLost}, delay since SR {rrs.DelaySinceLastSenderReport}, (cname={cname}).");
            }

            if (report.Bye != null)
            {
                Console.WriteLine($"RTCP recv BYE {mediaType}, reason {report.Bye.Reason}, (cname={cname}).");
            }
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
