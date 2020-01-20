//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that servers media streams
// to a WebRTC enabled browser.
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
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace WebRTCServer
{
    public class SDPExchange : WebSocketBehavior
    {
        public WebRtcSession WebRtcSession;

        public event Func<WebSocketContext, Task<WebRtcSession>> WebSocketOpened;
        public event Action<WebRtcSession, string> SDPAnswerReceived;

        public SDPExchange()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            SDPAnswerReceived(WebRtcSession, e.Data);
            this.Close();
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            WebRtcSession = await WebSocketOpened(this.Context);
        }
    }

    class Program
    {
        private static string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const string WEBSOCKET_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
        private const string DTLS_KEY_PATH = "certs/localhost_key.pem";
        private const string DTLS_CERTIFICATE_FINGERPRINT = "C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";
        private const string ICE_USER = "EJYWWCUDJQLTXTNQRXEJ";
        private const string ICE_PASSWORD = "SKYKPPYLTZOAVCLTGHDUODANRKSPOVQVKXJULOGG";
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static bool _exit = false;
        //private static ConcurrentDictionary<string, WebRtcSession> _webRtcSessions = new ConcurrentDictionary<string, WebRtcSession>();
        //private static WebRtcSession _webRtcSession;

        private static event Action<uint, byte[]> OnTestPatternSampleReady;

        static void Main()
        {
            Console.WriteLine("WebRTC Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            // Initialise OpenSSL & libsrtp, saves a couple of seconds for the first client connection.
            Console.WriteLine("Initialising OpenSSL and libsrtp...");
            Dtls.InitialiseOpenSSL();
            Srtp.InitialiseLibSrtp();

            Task.Run(SendTestPattern);

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT, true);
            _webSocketServer.SslConfiguration.ServerCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(WEBSOCKET_CERTIFICATE_PATH);
            _webSocketServer.SslConfiguration.CheckCertificateRevocation = false;
            //_webSocketServer.Log.Level = WebSocketSharp.LogLevel.Debug;
            _webSocketServer.AddWebSocketService<SDPExchange>("/", (sdpExchanger) =>
            {
                sdpExchanger.WebSocketOpened += SendSDPOffer;
                sdpExchanger.SDPAnswerReceived += SDPAnswerReceived;
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

        private static async Task<WebRtcSession> SendSDPOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}.");

            //var webRtcSession = new WebRtcSession(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH, DTLS_CERTIFICATE_FINGERPRINT, new List<IPAddress> { IPAddress.Loopback });
            var webRtcSession = new WebRtcSession(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH, DTLS_CERTIFICATE_FINGERPRINT, null);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");

            webRtcSession.OnClose += (reason) =>
            {
                logger.LogDebug($"WebRTCSession was closed with reason {reason}.");
            };

            await webRtcSession.Initialise(null);

            context.WebSocket.Send(webRtcSession.SDP);

            return webRtcSession;
        }

        private static void SDPAnswerReceived(WebRtcSession webRtcSession, string sdpAnswer)
        {
            try
            {
                logger.LogDebug("Answer SDP: " + sdpAnswer);

                var answerSDP = SDP.ParseSDPDescription(sdpAnswer);

                webRtcSession.SdpSessionID = answerSDP.SessionId;
                webRtcSession.RemoteIceUser = answerSDP.IceUfrag;
                webRtcSession.RemoteIcePassword = answerSDP.IcePwd;

                // All browsers seem to have gone to trickling ICE candidates now but just
                // in case one or more are given we can start the STUN dance immediately.
                if (answerSDP.IceCandidates != null)
                {
                    foreach (var iceCandidate in answerSDP.IceCandidates)
                    {
                        webRtcSession.AppendRemoteIceCandidate(iceCandidate);
                    }
                }

                OnTestPatternSampleReady += (timestamp, sample) =>
                {
                    try
                    {
                        webRtcSession.SendMedia(SDPMediaTypesEnum.video, timestamp, sample);
                    }
                    catch (Exception sendExcp)
                    {
                        logger.LogWarning("Exception OnTestPatternSampleReady. " + sendExcp.Message);
                        webRtcSession.Close();
                    }
                };
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SDPAnswerReceived. " + excp.Message);
            }
        }

        private static void SendTestPattern()
        {
            try
            {
                unsafe
                {
                    Bitmap testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);

                    // Get the stride.
                    Rectangle rect = new Rectangle(0, 0, testPattern.Width, testPattern.Height);
                    System.Drawing.Imaging.BitmapData bmpData =
                        testPattern.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                        testPattern.PixelFormat);

                    // Get the address of the first line.
                    int stride = bmpData.Stride;

                    testPattern.UnlockBits(bmpData);

                    // Initialise the video codec and color converter.
                    SIPSorceryMedia.VPXEncoder vpxEncoder = new VPXEncoder();
                    vpxEncoder.InitEncoder((uint)testPattern.Width, (uint)testPattern.Height, (uint)stride);

                    SIPSorceryMedia.ImageConvert colorConverter = new ImageConvert();

                    byte[] sampleBuffer = null;
                    byte[] encodedBuffer = null;
                    int sampleCount = 0;
                    uint rtpTimestamp = 0;

                    while (!_exit)
                    {
                        if (OnTestPatternSampleReady != null)
                        {
                            var stampedTestPattern = testPattern.Clone() as System.Drawing.Image;
                            AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                            sampleBuffer = BitmapToRGB24(stampedTestPattern as System.Drawing.Bitmap);

                            fixed (byte* p = sampleBuffer)
                            {
                                byte[] convertedFrame = null;
                                colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.BGR24, testPattern.Width, testPattern.Height, stride, VideoSubTypesEnum.I420, ref convertedFrame);

                                fixed (byte* q = convertedFrame)
                                {
                                    int encodeResult = vpxEncoder.Encode(q, convertedFrame.Length, 1, ref encodedBuffer);

                                    if (encodeResult != 0)
                                    {
                                        logger.LogWarning("VPX encode of video sample failed.");
                                        continue;
                                    }
                                }
                            }

                            stampedTestPattern.Dispose();
                            stampedTestPattern = null;

                            OnTestPatternSampleReady(rtpTimestamp, encodedBuffer);

                            encodedBuffer = null;

                            sampleCount++;
                            rtpTimestamp += VP8_TIMESTAMP_SPACING;
                        }

                        Thread.Sleep(30);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SendTestPattern. " + excp);
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
                return new byte[] { };
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
