using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using SIPSorceryMedia;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using log4net;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace WebRTCVideoServer
{
    public class WebRtcSession
    {
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int TIMESTAMP_SPACING = 3000;
        private const int PAYLOAD_TYPE_ID = 100;
        private const int SRTP_AUTH_KEY_LENGTH = 10;

        private static ILog logger = AppState.logger;

        public WebRtcPeer Peer;
        public DtlsManaged DtlsContext;
        public SRTPManaged SrtpContext;
        public SRTPManaged SrtpReceiveContext;  // Used to decrypt packets received from the remote peer.

        public string CallID
        {
            get { return Peer.CallID; }
        }

        public WebRtcSession(string callID)
        {
            Peer = new WebRtcPeer() { CallID = callID };
        }

        public void DtlsPacketReceived(IceCandidate iceCandidate, byte[] buffer, IPEndPoint remoteEndPoint)
        {
            logger.Debug("DTLS packet received " + buffer.Length + " bytes from " + remoteEndPoint.ToString() + ".");

            if (DtlsContext == null)
            {
                DtlsContext = new DtlsManaged();
                int res = DtlsContext.Init();
                Console.WriteLine("DtlsContext initialisation result=" + res);
            }

            int bytesWritten = DtlsContext.Write(buffer, buffer.Length);

            if (bytesWritten != buffer.Length)
            {
                logger.Warn("The required number of bytes were not successfully written to the DTLS context.");
            }
            else
            {
                byte[] dtlsOutBytes = new byte[2048];

                int bytesRead = DtlsContext.Read(dtlsOutBytes, dtlsOutBytes.Length);

                if (bytesRead == 0)
                {
                    Console.WriteLine("No bytes read from DTLS context :(.");
                }
                else
                {
                    Console.WriteLine(bytesRead + " bytes read from DTLS context sending to " + remoteEndPoint.ToString() + ".");
                    iceCandidate.LocalRtpSocket.SendTo(dtlsOutBytes, 0, bytesRead, SocketFlags.None, remoteEndPoint);

                    //if (client.DtlsContext.IsHandshakeComplete())
                    if (DtlsContext.GetState() == 3)
                    {
                        Console.WriteLine("DTLS negotiation complete for " + remoteEndPoint.ToString() + ".");
                        SrtpContext = new SRTPManaged(DtlsContext, false);
                        SrtpReceiveContext = new SRTPManaged(DtlsContext, true);
                        Peer.IsDtlsNegotiationComplete = true;
                        iceCandidate.RemoteRtpEndPoint = remoteEndPoint;
                    }
                }
            }
        }

        public void MediaPacketReceived(IceCandidate iceCandidate, byte[] buffer, IPEndPoint remoteEndPoint)
        {
            if ((buffer[0] >= 128) && (buffer[0] <= 191))
            {
                //logger.Debug("A non-STUN packet was received Receiver Client.");

                if (buffer[1] == 0xC8 /* RTCP SR */ || buffer[1] == 0xC9 /* RTCP RR */)
                {
                    // RTCP packet.
                    //webRtcClient.LastSTUNReceiveAt = DateTime.Now;
                }
                else
                {
                    // RTP packet.
                    //int res = peer.SrtpReceiveContext.UnprotectRTP(buffer, buffer.Length);

                    //if (res != 0)
                    //{
                    //    logger.Warn("SRTP unprotect failed, result " + res + ".");
                    //}
                }
            }
            else
            {
                logger.Debug("An unrecognised packet was received on the WebRTC media socket.");
            }
        }

        public void Send(byte[] buffer)
        {
            try
            {
                //if (client.LastRtcpSenderReportSentAt == DateTime.MinValue)
                //{
                //    logger.Debug("Sending RTCP report to " + client.SocketAddress + ".");

                //    // Send RTCP report.
                //    RTCPPacket rtcp = new RTCPPacket(client.SSRC, 0, 0, 0, 0);
                //    byte[] rtcpBuffer = rtcp.GetBytes();
                //    _webRTCReceiverClient.BeginSend(rtcpBuffer, rtcpBuffer.Length, client.SocketAddress, null, null);
                //    //int rtperr = client.SrtpContext.ProtectRTP(rtcpBuffer, rtcpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                //}

                //Console.WriteLine("Sending VP8 frame of " + encodedBuffer.Length + " bytes to " + client.SocketAddress + ".");

                Peer.LastTimestamp = (Peer.LastTimestamp == 0) ? RTSPSession.DateTimeToNptTimestamp32(DateTime.Now) : Peer.LastTimestamp + TIMESTAMP_SPACING;

                for (int index = 0; index * RTP_MAX_PAYLOAD < buffer.Length; index++)
                {
                    int offset = (index == 0) ? 0 : (index * RTP_MAX_PAYLOAD);
                    int payloadLength = (offset + RTP_MAX_PAYLOAD < buffer.Length) ? RTP_MAX_PAYLOAD : buffer.Length - offset;

                    byte[] vp8HeaderBytes = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };

                    RTPPacket rtpPacket = new RTPPacket(payloadLength + SRTP_AUTH_KEY_LENGTH + vp8HeaderBytes.Length);
                    rtpPacket.Header.SyncSource = Peer.SSRC;
                    rtpPacket.Header.SequenceNumber = Peer.SequenceNumber++;
                    rtpPacket.Header.Timestamp = Peer.LastTimestamp;
                    rtpPacket.Header.MarkerBit = ((offset + payloadLength) >= buffer.Length) ? 1 : 0; // Set marker bit for the last packet in the frame.
                    rtpPacket.Header.PayloadType = PAYLOAD_TYPE_ID;

                    Buffer.BlockCopy(vp8HeaderBytes, 0, rtpPacket.Payload, 0, vp8HeaderBytes.Length);
                    Buffer.BlockCopy(buffer, offset, rtpPacket.Payload, vp8HeaderBytes.Length, payloadLength);

                    var rtpBuffer = rtpPacket.GetBytes();

                    //_webRTCReceiverClient.Send(rtpBuffer, rtpBuffer.Length, _wiresharpEP);

                    int rtperr = SrtpContext.ProtectRTP(rtpBuffer, rtpBuffer.Length - SRTP_AUTH_KEY_LENGTH);
                    if (rtperr != 0)
                    {
                        logger.Warn("SRTP packet protection failed, result " + rtperr + ".");
                    }
                    else
                    {
                        var connectedIceCandidate = Peer.LocalIceCandidates.Where(y => y.RemoteRtpEndPoint != null).First();
                        connectedIceCandidate.LocalRtpSocket.SendTo(rtpBuffer, connectedIceCandidate.RemoteRtpEndPoint);
                    }
                }
            }
            catch (Exception sendExcp)
            {
                // logger.Error("SendRTP exception sending to " + client.SocketAddress + ". " + sendExcp.Message);
            }
        }
    }

    class Program
    {
        private const int RTP_MAX_PAYLOAD = 1400;
        private const int TIMESTAMP_SPACING = 3000;
        private const int PAYLOAD_TYPE_ID = 100;
        private const int SRTP_AUTH_KEY_LENGTH = 10;

        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;

        private static ILog logger = AppState.logger;

        private static bool m_exit = false;
        private static WebSocketServer _receiverWSS;
        private static ConcurrentBag<WebRtcSession> _webRtcSessions = new ConcurrentBag<WebRtcSession>();

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("WebRTC Test Video Peer:");

                SDPExchangeReceiver.WebSocketOpened += WebRtcStartCall;
                SDPExchangeReceiver.SDPAnswerReceived += WebRtcAnswerReceived;

                var wssCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2("test.p12");
                Console.WriteLine("WSS Certificate CN: " + wssCertificate.Subject + ", have key " + wssCertificate.HasPrivateKey + ", Expires " + wssCertificate.GetExpirationDateString() + ".");

                _receiverWSS = new WebSocketServer(8081, true);
                //_receiverWSS.Log.Level = LogLevel.Debug;
                _receiverWSS.SslConfiguration = new WebSocketSharp.Net.ServerSslConfiguration(wssCertificate, false,
                     System.Security.Authentication.SslProtocols.Tls,
                    false);

                _receiverWSS.AddWebSocketService<SDPExchangeReceiver>("/stream",
                    () => new SDPExchangeReceiver()
                    {
                        IgnoreExtensions = true,
                    });
                _receiverWSS.Start();

                ThreadPool.QueueUserWorkItem(delegate { SendTestPattern(); });

                ManualResetEvent dontStopEvent = new ManualResetEvent(false);
                dontStopEvent.WaitOne();
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception Main. " + excp);
            }
            finally
            {
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        private static void WebRtcStartCall(WebSocketSharp.Net.WebSockets.WebSocketContext context, string webSocketID)
        {
            logger.Debug("New WebRTC client added for web socket connection " + webSocketID + ".");

            lock (_webRtcSessions)
            {
                if (!_webRtcSessions.Any(x => x.CallID == webSocketID))
                {
                    var webRtcSession = new WebRtcSession(webSocketID);

                    _webRtcSessions.Add(webRtcSession);

                    webRtcSession.Peer.OnSdpOfferReady += (sdp) => { context.WebSocket.Send(sdp); };
                    webRtcSession.Peer.OnDtlsPacket += webRtcSession.DtlsPacketReceived;
                    webRtcSession.Peer.OnMediaPacket += webRtcSession.MediaPacketReceived;
                    webRtcSession.Peer.Initialise();
                }
            }
        }

        private static void WebRtcAnswerReceived(string webSocketID, string sdpAnswer)
        {
            try
            {
                logger.Debug("SDP Answer Received.");

                //Console.WriteLine(sdpAnswer);

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

                logger.Debug("ICE User: " + answerSDP.IceUfrag + ".");
                logger.Debug("ICE Password: " + answerSDP.IcePwd + ".");

                var peer = _webRtcSessions.Select(x => x.Peer).SingleOrDefault(x => x.CallID == webSocketID);

                if (peer == null)
                {
                    logger.Warn("No WebRTC client entry exists for web socket ID " + webSocketID + ", ignoring.");
                }
                else
                {
                    logger.Debug("New WebRTC client SDP answer for web socket ID " + webSocketID + ".");

                    peer.SdpSessionID = answerSDP.SessionId;
                    peer.RemoteIceUser = answerSDP.IceUfrag;
                    peer.RemoteIcePassword = answerSDP.IcePwd;
                    peer.RemoteIceCandidates = answerSDP.IceCandidates;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SDPExchangeReceiver_SDPAnswerReceived. " + excp.Message);
            }
        }

        private static void SendTestPattern()
        {
            try
            {
                unsafe
                {
                    Bitmap testPattern = new Bitmap("wizard.jpeg");

                    SIPSorceryMedia.VPXEncoder vpxEncoder = new VPXEncoder();
                    vpxEncoder.InitEncoder(Convert.ToUInt32(testPattern.Width), Convert.ToUInt32(testPattern.Height));

                    SIPSorceryMedia.ImageConvert colorConverter = new ImageConvert();

                    byte[] sampleBuffer = null;
                    byte[] encodedBuffer = new byte[4096];
                    while (true)
                    {
                        if (_webRtcSessions.Any(x => x.Peer.IsDtlsNegotiationComplete == true))
                        {
                            var stampedTestPattern = testPattern.Clone() as System.Drawing.Image;

                            AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                            sampleBuffer = BitmapToRGB24(stampedTestPattern as System.Drawing.Bitmap);

                            fixed (byte* p = sampleBuffer)
                            {
                                byte[] convertedFrame = null;
                                colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.RGB24, testPattern.Width, testPattern.Height, VideoSubTypesEnum.I420, ref convertedFrame);

                                fixed (byte* q = convertedFrame)
                                {
                                    int encodeResult = vpxEncoder.Encode(q, sampleBuffer.Length, 1, ref encodedBuffer);

                                    if (encodeResult != 0)
                                    {
                                        Console.WriteLine("VPX encode of video sample failed.");
                                        continue;
                                    }
                                }
                            }

                            stampedTestPattern.Dispose();
                            stampedTestPattern = null;

                            lock (_webRtcSessions)
                            {
                                foreach (var session in _webRtcSessions.Where(x => x.Peer.IsDtlsNegotiationComplete == true && x.Peer.LocalIceCandidates.Any(y => y.RemoteRtpEndPoint != null)))
                                {
                                    session.Send(encodedBuffer);
                                }
                            }

                            encodedBuffer = null;
                        }

                        Thread.Sleep(100);
                    }
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception SendTestPattern. " + excp);
            }
        }

        private static byte[] BitmapToRGB24(Bitmap bitmap)
        {
            try
            {
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                var length = bitmapData.Stride * bitmapData.Height;

                byte[] bytes = new byte[length];

                // Copy bitmap to byte[]
                Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
                bitmap.UnlockBits(bitmapData);

                return bytes;
            }
            catch (Exception)
            {
                return new byte[0];
            }
        }

        private static void AddTimeStampAndLocation(System.Drawing.Image image, string timeStamp, string locationText)
        {
            int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

            Graphics g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Alignment = StringAlignment.Center;

                using (Font f = new Font("Tahoma", pixelHeight, GraphicsUnit.Pixel))
                {
                    //// Draw a 'shadow' so the text will be visible on light and on dark images
                    //g.DrawString(timeStamp, _timestampFont, Brushes.Black, new Rectangle(0, image.Height - (TIMESTAMP_HEIGHT + 2), image.Width - 2, TIMESTAMP_HEIGHT), format);
                    //// Actual text
                    //g.DrawString(timeStamp, _timestampFont, Brushes.White, new Rectangle(2, image.Height - TIMESTAMP_HEIGHT, image.Width, TIMESTAMP_HEIGHT), format );
                    using (var gPath = new GraphicsPath())
                    {
                        float emSize = g.DpiY * f.Size / POINTS_PER_INCH;
                        if (locationText != null)
                        {
                            gPath.AddString(locationText, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, TEXT_MARGIN_PIXELS, image.Width, pixelHeight), format);
                        }

                        gPath.AddString(timeStamp /* + " -- " + fps.ToString("0.00") + " fps" */, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, image.Height - (pixelHeight + TEXT_MARGIN_PIXELS), image.Width, pixelHeight), format);
                        g.FillPath(Brushes.White, gPath);
                        g.DrawPath(new Pen(Brushes.Black, pixelHeight * TEXT_OUTLINE_REL_THICKNESS), gPath);
                    }
                }
            }
        }

    }

    public class SDPExchangeReceiver : WebSocketBehavior
    {
        public static event Action<WebSocketSharp.Net.WebSockets.WebSocketContext, string> WebSocketOpened;
        public static event Action<string, string> SDPAnswerReceived;

        protected override void OnMessage(MessageEventArgs e)
        {
            SDPAnswerReceived(this.ID, e.Data);
        }

        protected override void OnOpen()
        {
            base.OnOpen();
            WebSocketOpened(this.Context, this.ID);
        }
    }
}
