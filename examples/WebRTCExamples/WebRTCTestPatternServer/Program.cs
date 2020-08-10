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
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Ffmpeg;
using SIPSorcery.Net;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

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
            PeerConnection?.Close("remote party close");
        }
    }

    class Program
    {
        private static string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const int FRAMES_PER_SECOND = 30;
        private const int TEST_PATTERN_SPACING_MILLISECONDS = 33;
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const int FRAME_TIMESTAMP_SPACING = 3000;
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static VideoEncoder _videoEncoder;
        private static VideoFrameConverter _videoFrameConverter;
        private static Bitmap _testPattern;
        private static Timer _sendTestPatternTimer;

        private static event Action<SDPMediaTypesEnum, uint, byte[]> OnTestPatternSampleReady;

        static void Main()
        {
            Console.WriteLine("WebRTC Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            FfmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_DEBUG);

            InitialiseTestPattern();

            // The time callback periods will be adjusted when it's required.
            _sendTestPatternTimer = new Timer(SendTestPattern, null, Timeout.Infinite, Timeout.Infinite);

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
                _sendTestPatternTimer?.Dispose();
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static async Task<RTCPeerConnection> SendSDPOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}.");

            var pc = new RTCPeerConnection(null);

            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);

            pc.OnReceiveReport += RtpSession_OnReceiveReport;
            pc.OnSendReport += RtpSession_OnSendReport;
            pc.OnTimeout += (mediaType) => pc.Close("remote timeout");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    pc.Close("remote disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    OnTestPatternSampleReady -= pc.SendMedia;
                    pc.OnReceiveReport -= RtpSession_OnReceiveReport;
                    pc.OnSendReport -= RtpSession_OnSendReport;
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    OnTestPatternSampleReady += pc.SendMedia;
                    _sendTestPatternTimer.Change(0, TEST_PATTERN_SPACING_MILLISECONDS);
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

        private static void InitialiseTestPattern()
        {
            _testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);

            // Initialise the video codec and color converter.
            _videoEncoder = new VideoEncoder(AVCodecID.AV_CODEC_ID_VP8, _testPattern.Width, _testPattern.Height, FRAMES_PER_SECOND);
            _videoFrameConverter = new VideoFrameConverter(
                new Size(_testPattern.Width, _testPattern.Height),
                AVPixelFormat.AV_PIX_FMT_BGR24,
                new Size(_testPattern.Width, _testPattern.Height),
                AVPixelFormat.AV_PIX_FMT_YUV420P);
        }

        private static void SendTestPattern(object state)
        {
            if (OnTestPatternSampleReady != null)
            {
                lock (_sendTestPatternTimer)
                {
                    var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                    try
                    {
                        AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                        byte[] bmpBuffer = BitmapToRGB24(stampedTestPattern as System.Drawing.Bitmap);

                        var i420Frame = _videoFrameConverter.Convert(bmpBuffer);
                        i420Frame.key_frame = 1;

                        unsafe
                        {
                            byte[] encodedFrame = _videoEncoder.Encode(&i420Frame);

                            if (encodedFrame != null)
                            {
                                OnTestPatternSampleReady?.Invoke(SDPMediaTypesEnum.video, FRAME_TIMESTAMP_SPACING, encodedFrame);
                            }
                        }
                    }
                    finally
                    {
                        stampedTestPattern.Dispose();
                    }
                }
            }
            else
            {
                _sendTestPatternTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        private static byte[] BitmapToRGB24(Bitmap bitmap)
        {
            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var length = bitmapData.Stride * bitmapData.Height;

            byte[] bytes = new byte[length];

            // Copy bitmap to byte[]
            Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
            bitmap.UnlockBits(bitmapData);

            return bytes;
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
