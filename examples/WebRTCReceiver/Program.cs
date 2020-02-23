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
using System.Net.Sockets;
using System.Windows.Forms;
using Serilog;
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
        public event Action<WebSocketContext, string> MessageReceived;

        public SDPExchange()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            MessageReceived(this.Context, e.Data);
        }
    }

    class Program
    {
        private const string WEBSOCKET_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
        private const string DTLS_KEY_PATH = "certs/localhost_key.pem";
        private const string DTLS_CERTIFICATE_FINGERPRINT = "sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";
        private const int WEBSOCKET_PORT = 8081;

        private static WebSocketServer _webSocketServer;
        private static VpxEncoder _vpxEncoder;
        private static ImageConvert _imgConverter;
        private static List<WebRtcSession> _webRtcSessions = new List<WebRtcSession>();
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
            _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(WEBSOCKET_CERTIFICATE_PATH);
            _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            _webSocketServer.AddWebSocketService<SDPExchange>("/", (sdpExchanger) =>
            {
                sdpExchanger.MessageReceived += MessageReceived;
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

        private static async void MessageReceived(WebSocketContext context, string msg)
        {
            //Console.WriteLine($"websocket recv: {msg}");
            var offerSDP = SDP.ParseSDPDescription(msg);
            Console.WriteLine($"offer sdp: {offerSDP}");

            var webRtcSession = new WebRtcSession(
               AddressFamily.InterNetwork,
               DTLS_CERTIFICATE_FINGERPRINT,
               null,
               null);

            webRtcSession.setRemoteDescription(new RTCSessionDescription { sdp = offerSDP, type = RTCSdpType.offer });

            webRtcSession.OnReceiveReport += RtpSession_OnReceiveReport;
            webRtcSession.OnSendReport += RtpSession_OnSendReport;
            webRtcSession.OnRtpPacketReceived += RtpSession_OnRtpPacketReceived;
            webRtcSession.OnClose += (reason) =>
            {
                Console.WriteLine($"webrtc session closed: {reason}");
                _webRtcSessions.Remove(webRtcSession);
            };

            // Add local recvonly tracks. This ensures that the SDP answer includes only
            // the codecs we support.
            MediaStreamTrack audioTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) });
            audioTrack.Transceiver.SetStreamStatus(MediaStreamStatusEnum.RecvOnly);
            webRtcSession.addTrack(audioTrack);
            MediaStreamTrack videoTrack = new MediaStreamTrack(null, SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) });
            videoTrack.Transceiver.SetStreamStatus(MediaStreamStatusEnum.RecvOnly);
            webRtcSession.addTrack(videoTrack);

            var answerSdp = await webRtcSession.createAnswer();
            webRtcSession.setLocalDescription(new RTCSessionDescription { sdp = answerSdp, type = RTCSdpType.answer });

            Console.WriteLine($"answer sdp: {answerSdp}");

            context.WebSocket.Send(answerSdp.ToString());

            if (DoDtlsHandshake(webRtcSession))
            {
                _webRtcSessions.Add(webRtcSession);
            }
            else
            {
                webRtcSession.Close("dtls handshake failed.");
            }
        }

        private static void RtpSession_OnRtpPacketReceived(SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                //Console.WriteLine($"rtp audio, seqnum {rtpPacket.Header.SequenceNumber}, payload type {rtpPacket.Header.PayloadType}, marker {rtpPacket.Header.MarkerBit}.");
            }
            else if (mediaType == SDPMediaTypesEnum.video)
            {
                //Console.WriteLine($"rtp video, seqnum {rtpPacket.Header.SequenceNumber}, ts {rtpPacket.Header.Timestamp}, marker {rtpPacket.Header.MarkerBit}, payload {rtpPacket.Payload.Length}, payload[0-5] {rtpPacket.Payload.HexStr(5)}.");

                // New frames must have the VP8 Payload Descriptor Start bit set.
                if (_currVideoFramePosn > 0 || (rtpPacket.Payload[0] & 0x10) > 0)
                {
                    // TODO: use the VP8 Payload descriptor to properly determine the VP8 header length (currently hard coded to 4).
                    Buffer.BlockCopy(rtpPacket.Payload, 4, _currVideoFrame, _currVideoFramePosn, rtpPacket.Payload.Length - 4);
                    _currVideoFramePosn += rtpPacket.Payload.Length - 4;

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
        /// Hands the socket handle to the DTLS context and waits for the handshake to complete.
        /// </summary>
        /// <param name="webRtcSession">The WebRTC session to perform the DTLS handshake on.</param>
        /// <returns>True if the handshake completes successfully. False if not.</returns>
        private static bool DoDtlsHandshake(WebRtcSession webRtcSession)
        {
            Console.WriteLine("DoDtlsHandshake started.");

            var dtls = new DtlsHandshake(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);
            webRtcSession.OnClose += (reason) => dtls.Shutdown();

            int res = dtls.DoHandshakeAsServer((ulong)webRtcSession.GetRtpChannel(SDPMediaTypesEnum.audio).RtpSocket.Handle);

            Console.WriteLine("DtlsContext initialisation result=" + res);

            if (dtls.IsHandshakeComplete())
            {
                Console.WriteLine("DTLS negotiation complete.");

                // TODO fix race condition!!! First RTP packet is not getting decrypted.
                var srtpSendContext = new Srtp(dtls, false);
                var srtpReceiveContext = new Srtp(dtls, true);

                webRtcSession.SetSecurityContext(
                    srtpSendContext.ProtectRTP,
                    srtpReceiveContext.UnprotectRTP,
                    srtpSendContext.ProtectRTCP,
                    srtpReceiveContext.UnprotectRTCP);

                return true;
            }
            else
            {
                return false;
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
        private static void RtpSession_OnReceiveReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket report)
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
