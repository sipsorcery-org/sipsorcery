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
// 27 Jan 2021  Aaron Clauson   Switched from node-dss to REST signaling.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.FFmpeg;
using WebSocketSharp.Server;
using Microsoft.Extensions.DependencyInjection;
using SIPSorceryMedia.Abstractions;
using System.Runtime.InteropServices;
using System.Threading;
using vpxmd;

namespace demo;

public enum ImageTypesEnum
{
    Free,
    Paid
}

class Program
{
    private static string FREE_IMAGE_PATH = "media/simple_flower.jpg";
    private static string PAID_IMAGE_PATH = "media/real_flowers.jpg";
    private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
    private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
    private const int TEXT_MARGIN_PIXELS = 5;
    private const int POINTS_PER_INCH = 72;
    private const int BORDER_WIDTH = 5;
    private const int QR_CODE_DIMENSION = 200;

    private const int WEBSOCKET_PORT = 8081;
    private const int TEST_PATTERN_FRAMES_PER_SECOND = 5; //30;

    private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

    private static int _frameCount = 0;
    private static DateTime _startTime;

    static void Main(string[] args)
    {
        Console.WriteLine("WebRTC Lightning Demo");

        logger = AddConsoleLogger();

        //Log.Logger = new LoggerConfiguration()
        //    .WriteTo.Console()
        //    .CreateBootstrapLogger();

        //Log.Information("Starting ASP.NET server...");

        StartWebSocketServer();

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((context, services, config) =>
        {
            config.WriteTo.Console()
                .Enrich.FromLogContext();
        });
        builder.Services.AddControllers();

        var app = builder.Build();
        //app.UseSerilogRequestLogging();
        app.UseRouting();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();

        //app.MapGet("/", async context =>
        //{
        //    await context.Response.WriteAsync("WebRTC API is running!");
        //});

        app.Run();
    }

    private static void StartWebSocketServer()
    {
        // Start web socket.
        Console.WriteLine("Starting web socket server...");
        var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
        webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = () => CreatePeerConnection());
        webSocketServer.Start();

        Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
    }

    private static Task<RTCPeerConnection> CreatePeerConnection()
    {
        //RTCConfiguration config = new RTCConfiguration
        //{
        //    iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
        //    certificates = new List<RTCCertificate> { new RTCCertificate { Certificate = cert } }
        //};
        //var pc = new RTCPeerConnection(config);
        var pc = new RTCPeerConnection(null);

        //var testPatternSource = new VideoTestPatternSource(new SIPSorceryMedia.Encoders.VideoEncoder());
        SIPSorceryMedia.FFmpeg.FFmpegInit.Initialise(SIPSorceryMedia.FFmpeg.FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);
        var testPatternSource = new VideoTestPatternSource(new FFmpegVideoEncoder());
        testPatternSource.SetFrameRate(TEST_PATTERN_FRAMES_PER_SECOND);
        //testPatternSource.SetMaxFrameRate(true);
        //var videoEndPoint = new SIPSorceryMedia.FFmpeg.FFmpegVideoEndPoint();
        //videoEndPoint.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);
        //testPatternSource.RestrictFormats(format => format.Codec == VideoCodecsEnum.H264);
        //var videoEndPoint = new SIPSorceryMedia.Windows.WindowsEncoderEndPoint();
        //var videoEndPoint = new SIPSorceryMedia.Encoders.VideoEncoderEndPoint();

        MediaStreamTrack track = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(track);

        //testPatternSource.OnVideoSourceRawSample += videoEndPoint.ExternalVideoSourceRawSample;
        //testPatternSource.OnVideoSourceRawSample += MesasureTestPatternSourceFrameRate;
        testPatternSource.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());

        pc.onconnectionstatechange += async (state) =>
        {
            logger.LogDebug($"Peer connection state change to {state}.");

            if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                await testPatternSource.CloseVideo();
                testPatternSource.Dispose();
            }
            else if (state == RTCPeerConnectionState.connected)
            {
                await testPatternSource.StartVideo();
            }
        };

        // Diagnostics.
        //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
        //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
        //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
        pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
        pc.onsignalingstatechange += () =>
        {
            if (pc.signalingState == RTCSignalingState.have_local_offer)
            {
                logger.LogDebug($"Local SDP set, type {pc.localDescription.type}.");
                logger.LogDebug(pc.localDescription.sdp.ToString());
            }
            else if (pc.signalingState == RTCSignalingState.have_remote_offer)
            {
                logger.LogDebug($"Remote SDP set, type {pc.remoteDescription.type}.");
                logger.LogDebug(pc.remoteDescription.sdp.ToString());
            }
        };

        return Task.FromResult(pc);
    }

    private void SendSample(object state)
    {
        if (WebRtcSession.IsClosed)
        {
            Dispose();
        }
        else if (!_isDisposed)
        {
            int transparency = 0;
            ImageType = ImageTypesEnum.Free;
            string title = null;
            Bitmap qrCode = null;

            if (_isPaid)
            {
                ImageType = ImageTypesEnum.Paid;
            }
            else
            {
                if (DateTime.Now.Subtract(_startTime).TotalSeconds < FREE_PERIOD_SECONDS)
                {
                    BorderColor = Color.Blue;
                    title = FREE_PERIOD_TITLE;
                }
                else if (DateTime.Now.Subtract(_startTime).TotalSeconds < (FREE_PERIOD_SECONDS + TRANSPARENCY_PERIOD_SECONDS))
                {
                    BorderColor = Color.Yellow;
                    double remaining = FREE_PERIOD_SECONDS + TRANSPARENCY_PERIOD_SECONDS - DateTime.Now.Subtract(_startTime).TotalSeconds;
                    transparency = (int)(MAX_ALPHA_TRANSPARENCY - MAX_ALPHA_TRANSPARENCY * (remaining / TRANSPARENCY_PERIOD_SECONDS));
                    title = TRANSITION_PERIOD_TITLE;
                    qrCode = _qrCodeImage;
                }
                else
                {
                    BorderColor = Color.Orange;
                    transparency = MAX_ALPHA_TRANSPARENCY;
                    title = TRANSITION_PERIOD_TITLE;
                    qrCode = _qrCodeImage;
                }
            }

            if (Monitor.TryEnter(WebRtcSession))
            {
                var sampleBuffer = WebRtcDaemon.GetVideoSample(ImageType, BorderColor, title, transparency, qrCode);

                if (sampleBuffer != null && !_isDisposed)
                {

                    unsafe
                    {
                        byte[] encodedBuffer = null;

                        fixed (byte* p = sampleBuffer)
                        {
                            byte[] convertedFrame = null;
                            _colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.BGR24, _width, _height, _stride, VideoSubTypesEnum.I420, ref convertedFrame);

                            fixed (byte* q = convertedFrame)
                            {
                                int encodeResult = _vpxEncoder.Encode(q, convertedFrame.Length, 1, ref encodedBuffer);

                                if (encodeResult != 0)
                                {
                                    Console.WriteLine("VPX encode of video sample failed.");
                                }
                            }

                            WebRtcSession.SendMedia(SDPMediaTypesEnum.video, _rtpTimestamp, encodedBuffer);
                            _rtpTimestamp += VP8_TIMESTAMP_SPACING;
                        }
                    }

                    _sendSampleTimer.Change(VIDEO_SAMPLING_PERIOD, Timeout.Infinite);
                }

                Monitor.Exit(WebRtcSession);
            }
        }
    }

    private static byte[] GetVideoSample(ImageTypesEnum imageType, Color borderColor, string title, int transparency, Bitmap qrCode)
    {
        try
        {
            unsafe
            {
                string imagePath = null;

                switch (imageType)
                {
                    case ImageTypesEnum.Free:
                        imagePath = FREE_IMAGE_PATH;
                        break;
                    case ImageTypesEnum.Paid:
                        imagePath = PAID_IMAGE_PATH;
                        break;
                    default:
                        imagePath = FREE_IMAGE_PATH;
                        break;
                }

                Bitmap testPattern = new Bitmap(imagePath);

                byte[] sampleBuffer = null;

                var stampedTestPattern = testPattern.Clone() as System.Drawing.Image;
                ApplyFilters(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), title, borderColor, transparency, qrCode);
                sampleBuffer = BitmapToRGB24(stampedTestPattern as System.Drawing.Bitmap);

                stampedTestPattern.Dispose();
                stampedTestPattern = null;

                return sampleBuffer;
            }
        }
        catch (Exception excp)
        {
            Console.WriteLine("Exception GetVideoSample. " + excp);
            return null;
        }
    }

    private static void ApplyFilters(System.Drawing.Image image, string timeStamp, string title, Color borderColor, int transparency, Bitmap qrCode)
    {
        int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

        Graphics g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        // Draw border.
        using (var pen = new Pen(borderColor, BORDER_WIDTH))
        {
            g.DrawLine(pen, new Point(0, 0), new Point(0, image.Height));
            g.DrawLine(pen, new Point(0, 0), new Point(image.Width, 0));
            g.DrawLine(pen, new Point(0, image.Height), new Point(image.Width, image.Height));
            g.DrawLine(pen, new Point(image.Width, 0), new Point(image.Width, image.Height));
        }

        // Add transparency.
        using (Brush brush = new SolidBrush(Color.FromArgb(transparency, Color.Gray)))
        {
            g.FillRectangle(brush, new Rectangle(0, 0, image.Width, image.Height));
        }

        // Add header and footer text.
        using (StringFormat format = new StringFormat())
        {
            format.LineAlignment = StringAlignment.Center;
            format.Alignment = StringAlignment.Center;

            using (Font f = new Font("Tahoma", pixelHeight, GraphicsUnit.Pixel))
            {
                using (var gPath = new GraphicsPath())
                {
                    float emSize = g.DpiY * f.Size / POINTS_PER_INCH;
                    if (title != null)
                    {
                        gPath.AddString(title, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, TEXT_MARGIN_PIXELS, image.Width, pixelHeight), format);
                    }

                    gPath.AddString(timeStamp /* + " -- " + fps.ToString("0.00") + " fps" */, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, image.Height - (pixelHeight + TEXT_MARGIN_PIXELS), image.Width, pixelHeight), format);
                    g.FillPath(Brushes.White, gPath);
                    g.DrawPath(new Pen(Brushes.Black, pixelHeight * TEXT_OUTLINE_REL_THICKNESS), gPath);
                }
            }
        }

        // Add QR Code.
        if (qrCode != null)
        {
            int xCenter = (image.Width - QR_CODE_DIMENSION) / 2;
            int yCenter = (image.Height - QR_CODE_DIMENSION) / 2;
            Rectangle dstRect = new Rectangle(xCenter, yCenter, QR_CODE_DIMENSION, QR_CODE_DIMENSION);
            g.DrawImage(qrCode, dstRect);
        }
    }

    private static byte[] BitmapToRGB24(Bitmap bitmap)
    {
        try
        {
            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            var length = bitmapData.Stride * bitmapData.Height;

            byte[] bytes = new byte[length];

            // Copy bitmap to byte[]
            Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
            bitmap.UnlockBits(bitmapData);

            return bytes;
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
    /// </summary>
    private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
    {
        var seriLogger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
            .WriteTo.Console()
            .CreateLogger();
        var factory = new SerilogLoggerFactory(seriLogger);
        SIPSorcery.LogFactory.Set(factory);
        return factory.CreateLogger<Program>();
    }
}
