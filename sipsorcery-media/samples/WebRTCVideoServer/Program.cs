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
        private const string AUTOMATIC_PRIVATE_ADRRESS_PREFIX = "169.254";      // The prefix of the IP address range automatically assigned to interfaces using DHCP before they have acquired an address.

        private const string DTLS_CERTIFICATE_THUMBRPINT = "25:5A:A9:32:1F:35:04:8D:5F:8A:5B:27:0B:9F:A2:90:1A:0E:B9:E9:02:A2:24:95:64:E5:7C:4C:10:11:F7:36";

        private static ILog logger = AppState.logger;

        private static string _webSocketCertificatePath = AppState.GetConfigSetting("WebSocketCertificatePath");
        private static string _webSocketCertificatePassword = AppState.GetConfigSetting("WebSocketCertificatePassword");

        private static bool _exit = false;
        private static WebSocketServer _receiverWSS;
        private static ConcurrentBag<WebRtcSession> _webRtcSessions = new ConcurrentBag<WebRtcSession>();

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("WebRTC Test Video Peer:");

                SDPExchangeReceiver.WebSocketOpened += WebRtcStartCall;
                SDPExchangeReceiver.SDPAnswerReceived += WebRtcAnswerReceived;

                var wssCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(_webSocketCertificatePath, _webSocketCertificatePassword);
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

                    webRtcSession.Peer.OnSdpOfferReady += (sdp) => { logger.Debug("Offer SDP: " + sdp); context.WebSocket.Send(sdp); };
                    webRtcSession.Peer.OnDtlsPacket += webRtcSession.DtlsPacketReceived;
                    webRtcSession.Peer.OnMediaPacket += webRtcSession.MediaPacketReceived;
                    webRtcSession.Peer.Initialise(DTLS_CERTIFICATE_THUMBRPINT, null);
                }
            }
        }

        private static void WebRtcAnswerReceived(string webSocketID, string sdpAnswer)
        {
            try
            {
                logger.Debug("Answer SDP: " + sdpAnswer);

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

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

                    foreach (var iceCandidate in answerSDP.IceCandidates)
                    {
                        peer.AppendRemoteIceCandidate(iceCandidate);
                    }
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
