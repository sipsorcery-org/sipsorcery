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
using SIPSorceryMedia.FFmpeg;
using SIPSorceryMedia.Windows.Codecs;
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
        private static string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const int FRAMES_PER_SECOND = 30;
        private const int TEST_PATTERN_SPACING_MILLISECONDS = 33;
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const int VIDEO_TIMESTAMP_SPACING = 3000;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const string LOCALHOST_CERTIFICATE_PATH = "certs/localhost.pfx";
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;
        private static Bitmap _testPattern;
        private static Timer _sendTestPatternTimer;
        private static bool _forceKeyFrame = false;
        private static long _presentationTimestamp = 0;

        //private static Vp8Codec _vp8Codec;
        private static VideoEncoder _ffmpegEncoder;
        //private static OpenH264Encoder _openH264Encoder;
        private static VideoFrameConverter _videoFrameConverter;

        private static event Action<SDPMediaTypesEnum, uint, byte[]> OnTestPatternSampleReady;

        static void Main()
        {
            Console.WriteLine("WebRTC Server Sample Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            InitialiseTestPattern();

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

            //MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.SendOnly);
            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, 
                new List<SDPMediaFormat> 
                { 
                    new SDPMediaFormat(SDPMediaFormatsEnum.H264)
                    {
                        FormatParameterAttribute = $"packetization-mode=1",
                    }
                }, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);

            //pc.OnReceiveReport += RtpSession_OnReceiveReport;
            //pc.OnSendReport += RtpSession_OnSendReport;
            pc.OnTimeout += (mediaType) => pc.Close("remote timeout");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    pc.Close("remote disconnection");
                }

                if (state == RTCPeerConnectionState.closed)
                {
                    OnTestPatternSampleReady -= pc.SendMedia;
                    pc.OnReceiveReport -= RtpSession_OnReceiveReport;
                    pc.OnSendReport -= RtpSession_OnSendReport;

                    if (OnTestPatternSampleReady == null)
                    {
                        _sendTestPatternTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    }
                }
                else if (state == RTCPeerConnectionState.connected)
                {
                    OnTestPatternSampleReady += pc.SendMedia;
                    _forceKeyFrame = true;
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
            _sendTestPatternTimer = new Timer(SendTestPattern, null, Timeout.Infinite, Timeout.Infinite);

            //_vp8Codec = new Vp8Codec();
            //_vp8Codec.InitialiseEncoder((uint)_testPattern.Width, (uint)_testPattern.Height);

            //_ffmpegEncoder = new VideoEncoder(AVCodecID.AV_CODEC_ID_VP8, _testPattern.Width, _testPattern.Height, FRAMES_PER_SECOND);
            _ffmpegEncoder = new VideoEncoder(AVCodecID.AV_CODEC_ID_H264, _testPattern.Width, _testPattern.Height, FRAMES_PER_SECOND);
            Console.WriteLine($"Codec name {_ffmpegEncoder.GetCodecName()}.");

            _videoFrameConverter = new VideoFrameConverter(
                new Size(_testPattern.Width, _testPattern.Height),
                AVPixelFormat.AV_PIX_FMT_BGRA,
                new Size(_testPattern.Width, _testPattern.Height),
                AVPixelFormat.AV_PIX_FMT_YUV420P);


            //_openH264Encoder = new OpenH264Encoder("OpenH264lib", _testPattern.Width, _testPattern.Height, 50000, 30, 100);
            //_openH264Encoder = new OpenH264Encoder("openh264-2.1.1-win64.dll", _testPattern.Width, _testPattern.Height, 50000, 30, 100);

            //Console.WriteLine($"OpneH264 version {Marshal.PtrToStringUni(OpenH264Encoder.GetCodecVersion())}.");
        }

        private static void SendTestPattern(object state)
        {
            lock (_sendTestPatternTimer)
            {
                unsafe
                {
                    if (OnTestPatternSampleReady != null)
                    {
                        var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                        AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                        var sampleBuffer = PixelConverter.BitmapToRGBA(stampedTestPattern as System.Drawing.Bitmap, _testPattern.Width, _testPattern.Height);

                        var i420Frame = _videoFrameConverter.Convert(sampleBuffer);

                        //byte[] i420Buffer = PixelConverter.RGBAtoYUV420Planar(sampleBuffer, _testPattern.Width, _testPattern.Height);
                        //var encodedBuffer = _vp8Codec.Encode(i420, _forceKeyFrame);

                        _presentationTimestamp += VIDEO_TIMESTAMP_SPACING;

                        i420Frame.key_frame = _forceKeyFrame ? 1 : 0;
                        i420Frame.pts = _presentationTimestamp;

                        byte[] encodedBuffer = _ffmpegEncoder.Encode(i420Frame);
                        if (encodedBuffer != null)
                        {
                            //Console.WriteLine($"encoded buffer: {encodedBuffer.HexStr()}");
                            Console.WriteLine($"H264 encoded buffer length {encodedBuffer.Length}.");

                            int zeroes = 0;

                            // Parse NALs from H264 bitstream.
                            int currPosn = 0;
                            for (int i = 0; i < encodedBuffer.Length; i++)
                            {
                                if (encodedBuffer[i] == 0x00)
                                {
                                    zeroes++;
                                }
                                else if (encodedBuffer[i] == 0x01 && zeroes >= 2)
                                {
                                    // This is a NAL start sequence.
                                    int nalStart = i + 1;
                                    if (nalStart - currPosn > 4)
                                    {
                                        int endPosn = nalStart - ((zeroes == 2) ? 3 : 4);
                                        int nalSize = endPosn - currPosn;

                                        //Console.WriteLine($"nal: {encodedBuffer.Skip(currPosn).Take(nalSize).ToArray().HexStr()}");
                                        Console.WriteLine($"sending nal length {nalSize}.");

                                        OnTestPatternSampleReady?.Invoke(SDPMediaTypesEnum.video, VIDEO_TIMESTAMP_SPACING, encodedBuffer.Skip(currPosn).Take(nalSize).ToArray());
                                    }

                                    currPosn = nalStart;
                                }
                                else
                                {
                                    zeroes = 0;
                                }
                            }

                            if (currPosn < encodedBuffer.Length)
                            {
                                //Console.WriteLine($"last nal: {encodedBuffer.Skip(currPosn).ToArray().HexStr()}");
                                Console.WriteLine($"sending last nal length {encodedBuffer.Length - currPosn}.");

                                OnTestPatternSampleReady?.Invoke(SDPMediaTypesEnum.video, VIDEO_TIMESTAMP_SPACING, encodedBuffer.Skip(currPosn).ToArray());
                            }
                        }

                        if (_forceKeyFrame)
                        {
                            _forceKeyFrame = false;
                        }

                        stampedTestPattern.Dispose();
                    }
                }
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
