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
using System.Threading.Tasks;
using System.Windows.Forms;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorceryMedia;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace TestConsole
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
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;

        private static WebSocketServer _webSocketServer;
        private static VpxEncoder _vpxEncoder;
        private static ImageConvert _imgConverter;
        private static byte[] _currVideoFrame = new byte[65536];
        private static int _currVideoFramePosn = 0;
        private static Form _form;
        private static PictureBox _picBox;

        [STAThread]
        static void Main(string[] args)
        {
            AddConsoleLogger();

            _vpxEncoder = new VpxEncoder();
            int res = _vpxEncoder.InitDecoder();
            if (res != 0)
            {
                throw new ApplicationException("VPX decoder initialisation failed.");
            }

            _imgConverter = new ImageConvert();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(LOCALHOST_CERTIFICATE_PATH);
            _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
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
            var peerConnection = new RTCPeerConnection(null);

            // Add local recvonly tracks. This ensures that the SDP answer includes only
            // the codecs we support.
            MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.RecvOnly);
            peerConnection.addTrack(videoTrack);

            peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
            peerConnection.OnSendReport += RtpSession_OnSendReport;
            peerConnection.oniceconnectionstatechange += (state) => Console.WriteLine($"ICE connection state changed to {state}.");
            peerConnection.onconnectionstatechange += (state) =>
            {
                Console.WriteLine($"Peer connection state changed to {state}.");

                if (state == RTCPeerConnectionState.closed)
                {
                    peerConnection.OnRtpPacketReceived -= RtpSession_OnRtpPacketReceived;
                }
                else if(state == RTCPeerConnectionState.connected)
                {
                    peerConnection.OnRtpPacketReceived += RtpSession_OnRtpPacketReceived;
                }
            };

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

        private static void RtpSession_OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                //Console.WriteLine($"rtp audio, seqnum {rtpPacket.Header.SequenceNumber}, payload type {rtpPacket.Header.PayloadType}, marker {rtpPacket.Header.MarkerBit}.");
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                //Console.WriteLine($"rtp video, seqnum {rtpPacket.Header.SequenceNumber}, ts {rtpPacket.Header.Timestamp}, marker {rtpPacket.Header.MarkerBit}, payload {rtpPacket.Payload.Length}, payload[0-5] {rtpPacket.Payload.HexStr(5)}.");

                // New frames must have the VP8 Payload Descriptor Start bit set.
                // The tracking of the current video frame position is to deal with a VP8 frame being split across multiple RTP packets
                // as per https://tools.ietf.org/html/rfc7741#section-4.4.
                if (_currVideoFramePosn > 0 || (rtpPacket.Payload[0] & 0x10) > 0)
                {
                    RtpVP8Header vp8Header = RtpVP8Header.GetVP8Header(rtpPacket.Payload);

                    Buffer.BlockCopy(rtpPacket.Payload, vp8Header.Length, _currVideoFrame, _currVideoFramePosn, rtpPacket.Payload.Length - vp8Header.Length);
                    _currVideoFramePosn += rtpPacket.Payload.Length - vp8Header.Length;

                    if (rtpPacket.Header.MarkerBit == 1)
                    {
                        unsafe
                        {
                            fixed (byte* p = _currVideoFrame)
                            {
                                uint width = 0, height = 0;
                                byte[] i420 = null;

                                //Console.WriteLine($"Attempting vpx decode {_currVideoFramePosn} bytes.");

                                int decodeResult = _vpxEncoder.Decode(p, _currVideoFramePosn, ref i420, ref width, ref height);

                                if (decodeResult != 0)
                                {
                                    Console.WriteLine("VPX decode of video sample failed.");
                                }
                                else
                                {
                                    //Console.WriteLine($"Video frame ready {width}x{height}.");

                                    fixed (byte* r = i420)
                                    {
                                        byte[] bmp = null;
                                        int stride = 0;
                                        int convRes = _imgConverter.ConvertYUVToRGB(r, VideoSubTypesEnum.I420, (int)width, (int)height, VideoSubTypesEnum.BGR24, ref bmp, ref stride);

                                        if (convRes == 0)
                                        {
                                            _form.BeginInvoke(new Action(() =>
                                            {
                                                fixed (byte* s = bmp)
                                                {
                                                    System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap((int)width, (int)height, stride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, (IntPtr)s);
                                                    _picBox.Image = bmpImage;
                                                }
                                            }));
                                        }
                                        else
                                        {
                                            Console.WriteLine("Pixel format conversion of decoded sample failed.");
                                        }
                                    }
                                }
                            }
                        }

                        _currVideoFramePosn = 0;
                    }
                }
                else
                {
                    Console.WriteLine("Discarding RTP packet, VP8 header Start bit not set.");
                    Console.WriteLine($"rtp video, seqnum {rtpPacket.Header.SequenceNumber}, ts {rtpPacket.Header.Timestamp}, marker {rtpPacket.Header.MarkerBit}, payload {rtpPacket.Payload.Length}, payload[0-5] {rtpPacket.Payload.HexStr(5)}.");
                }
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
