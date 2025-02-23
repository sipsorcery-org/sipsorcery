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
using System.Threading;
using QRCoder;
using System.Collections.Concurrent;

namespace demo;

class Program
{
    record FrameConfig(
        DateTime StartTime,
        Bitmap? QrCodeImage,
        int Opacity,
        Color BorderColour,
        string Title,
        bool IsPaid);

    private static string FREE_IMAGE_PATH = "media/simple_flower.jpg";
    private static string PAID_IMAGE_PATH = "media/real_flowers.jpg";
    private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
    private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
    private const int TEXT_MARGIN_PIXELS = 5;
    private const int POINTS_PER_INCH = 72;
    private const int BORDER_WIDTH = 5;
    private const int QR_CODE_DIMENSION = 200;

    private const int FREE_PERIOD_SECONDS = 3;
    private const int TRANSPARENCY_PERIOD_SECONDS = 3;
    private const int MAX_ALPHA_TRANSPARENCY = 200;
    private const string FREE_PERIOD_TITLE = "Taster Content";
    private const string TRANSITION_PERIOD_TITLE = "Pay for More";

    private const int WEBSOCKET_PORT = 8081;
    private const int FRAMES_PER_SECOND = 5; //30;
    private const string BASE_URL = "https://localhost:5001";
    private const int CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS = 100;

    private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

    private static readonly PeerConnectionPayState _peerConnectionPayState = PeerConnectionPayState.Get;

    static void Main(string[] args)
    {
        Console.WriteLine("WebRTC Lightning Demo");

        logger = AddConsoleLogger();

        StartWebSocketServer(logger);
        FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);

        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseSerilog((context, services, config) =>
        {
            config.WriteTo.Console()
                .Enrich.FromLogContext();
        });
        builder.Services.AddControllers();

        var app = builder.Build();
        app.UseRouting();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapControllers();

        app.Run();
    }

    private static void StartWebSocketServer(Microsoft.Extensions.Logging.ILogger logger)
    {
        // Start web socket.
        Console.WriteLine("Starting web socket server...");
        var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
        webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = async () =>
        {
            var pc = await CreatePeerConnection(peer.ID);
            logger.LogDebug($"Peer connection {peer.ID} successfully created.");
            _peerConnectionPayState.TryAddPeer(peer.ID);
            pc.onconnectionstatechange += (state) =>
            {
                if (state is RTCPeerConnectionState.closed or
                            RTCPeerConnectionState.failed or
                            RTCPeerConnectionState.disconnected)
                {
                    _peerConnectionPayState.TryRemovePeer(peer.ID);
                }
            };
            return pc;
        });
        webSocketServer.Start();

        Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
    }

    private static Task<RTCPeerConnection> CreatePeerConnection(string peerID)
    {
        var pc = new RTCPeerConnection(null);

        Bitmap sourceBitmap = new Bitmap(FREE_IMAGE_PATH);

        var bitmapSource = new VideoBitmapSource(new FFmpegVideoEncoder());
        bitmapSource.SetFrameRate(FRAMES_PER_SECOND);
        bitmapSource.SetSourceBitmap(sourceBitmap);

        MediaStreamTrack track = new MediaStreamTrack(bitmapSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(track);

        bitmapSource.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += (formats) => bitmapSource.SetVideoSourceFormat(formats.First());

        HandlePeerConnectionStateChange(pc, bitmapSource, peerID);
        SetDiagnosticLogging(pc);

        return Task.FromResult(pc);
    }

    private static Timer CreateGenerateBitmapTimer(VideoBitmapSource bitmapSource, string peerID)
    {
        var frameConfig = new FrameConfig(DateTime.Now, null, 0, Color.Blue, FREE_PERIOD_TITLE, false);

        return new Timer(_ =>
        {
            frameConfig = GetUpdatedFrameConfig(frameConfig, peerID);

            var annotatedBitmap = GetAnnotatedBitmap(frameConfig);

            if (annotatedBitmap != null)
            {
                bitmapSource.SetSourceBitmap(annotatedBitmap);
                annotatedBitmap.Dispose();
            }
        },
        null, TimeSpan.Zero, TimeSpan.FromMilliseconds(CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS));
    }

    private static FrameConfig GetUpdatedFrameConfig(FrameConfig frameConfig, string peerID)
    {
        if(_peerConnectionPayState.TryGetIsPaid(peerID))
        {
            return frameConfig with
            {
                BorderColour = Color.Pink,
                Title = string.Empty,
                IsPaid = true,
                QrCodeImage = null,
                Opacity = 0
            };
        }

        if (DateTime.Now.Subtract(frameConfig.StartTime).TotalSeconds < FREE_PERIOD_SECONDS)
        {
            return frameConfig with
            {
                BorderColour = Color.Blue,
                Title = FREE_PERIOD_TITLE
            };
        }
        else if (DateTime.Now.Subtract(frameConfig.StartTime).TotalSeconds < (FREE_PERIOD_SECONDS + TRANSPARENCY_PERIOD_SECONDS))
        {
            double freeSecondsRemaining = FREE_PERIOD_SECONDS + TRANSPARENCY_PERIOD_SECONDS - DateTime.Now.Subtract(frameConfig.StartTime).TotalSeconds;

            return frameConfig with
            {
                BorderColour = Color.Yellow,
                QrCodeImage = frameConfig.QrCodeImage ?? GenerateQRCode(peerID),
                Opacity = (int)(MAX_ALPHA_TRANSPARENCY - MAX_ALPHA_TRANSPARENCY * (freeSecondsRemaining/ TRANSPARENCY_PERIOD_SECONDS)),
                Title = TRANSITION_PERIOD_TITLE
            };
        }
        else
        {
            return frameConfig with
            {
                BorderColour = Color.Orange,
                Opacity = MAX_ALPHA_TRANSPARENCY,
                Title = TRANSITION_PERIOD_TITLE
            };
        }
    }

    private static Bitmap GenerateQRCode(string peerID)
    {
        using QRCodeGenerator qrGenerator = new QRCodeGenerator();
        using QRCodeData qrCodeData = qrGenerator.CreateQrCode($"{BASE_URL}/pay?id={peerID}", QRCodeGenerator.ECCLevel.Q);
        using QRCode qrCode = new QRCode(qrCodeData);

        return qrCode.GetGraphic(20);
    }

    private static Bitmap? GetAnnotatedBitmap(FrameConfig frameConfig)
    {
        try
        {
            unsafe
            {
                string imagePath = frameConfig.IsPaid ? PAID_IMAGE_PATH : FREE_IMAGE_PATH;
                Bitmap baseBitmap = new Bitmap(imagePath);

                var baseImage = baseBitmap.Clone() as Image;
                if (baseImage != null)
                {
                    ApplyFilters(
                        baseImage,
                        DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"),
                        frameConfig.Title,
                        frameConfig.BorderColour,
                        frameConfig.Opacity,
                        frameConfig.QrCodeImage);

                    baseBitmap.Dispose();

                    return baseImage as Bitmap;
                }
            }
        }
        catch (Exception excp)
        {
            Console.WriteLine("Exception GetAnnotatedBitmap. " + excp);
        }

        return null;
    }

    private static void ApplyFilters(Image image, string timeStamp, string title, Color borderColor, int transparency, Bitmap? qrCode)
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

    private static void HandlePeerConnectionStateChange(RTCPeerConnection pc, VideoBitmapSource bitmapSource, string peerID)
    {
        Timer? setBitmapSourceTimer = null;

        pc.onconnectionstatechange += async (state) =>
        {
            logger.LogDebug($"Peer connection state change to {state}.");

            if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                await CloseCustomBitmapSource(setBitmapSourceTimer, bitmapSource);
            }
            else if (state == RTCPeerConnectionState.connected)
            {
                await bitmapSource.StartVideo();

                if (setBitmapSourceTimer == null)
                {
                    setBitmapSourceTimer = CreateGenerateBitmapTimer(bitmapSource, peerID);
                }
            }
        };
    }

    private static async Task CloseCustomBitmapSource(Timer? setBitmapSourceTimer, VideoBitmapSource bitmapSource)
    {
        if (setBitmapSourceTimer != null)
        {
            await setBitmapSourceTimer.DisposeAsync();
            setBitmapSourceTimer = null;
        }

        await bitmapSource.CloseVideo();
        bitmapSource.Dispose();
    }

    private static void SetDiagnosticLogging(RTCPeerConnection pc)
    {
        // Diagnostics.
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
