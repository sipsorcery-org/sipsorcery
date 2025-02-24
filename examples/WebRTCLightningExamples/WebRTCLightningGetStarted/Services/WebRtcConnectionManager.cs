//-----------------------------------------------------------------------------
// Filename: WebRtcConnectionManager.cs
//
// Description: Manages the creation and lifeftime of WebRTC connections established
// with remote peers.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using QRCoder;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.FFmpeg;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace demo;

public class WebRtcConnectionManager
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
    private const int FRAMES_PER_SECOND = 5; //30;
    private const int CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS = 100;

    private const string BASE_URL = "https://localhost:5001";

    private readonly ILogger<WebRtcConnectionManager> _logger;
    private readonly PeerConnectionPayState _peerConnectionPayState;
    private readonly ILightningService _lightningService;

    public WebRtcConnectionManager(
        ILogger<WebRtcConnectionManager> logger,
        PeerConnectionPayState peerConnectionPayState,
        ILightningService lightningService)
    {
        _logger = logger;
        _peerConnectionPayState = peerConnectionPayState;
        _lightningService = lightningService;
    }

    public Task<RTCPeerConnection> CreatePeerConnection(string peerID)
    {
        var pc = new RTCPeerConnection(null);
        _peerConnectionPayState.TryAddPeer(peerID);

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

        pc.onconnectionstatechange += (state) =>
        {
            if (state is RTCPeerConnectionState.closed or
                        RTCPeerConnectionState.failed or
                        RTCPeerConnectionState.disconnected)
            {
                _peerConnectionPayState.TryRemovePeer(peerID);
            }
        };

        return Task.FromResult(pc);
    }

    private Timer CreateGenerateBitmapTimer(VideoBitmapSource bitmapSource, string peerID)
    {
        var frameConfig = new FrameConfig(DateTime.Now, null, 0, Color.Blue, FREE_PERIOD_TITLE, false);

        return new Timer(async _ =>
        {
            frameConfig = await GetUpdatedFrameConfig(frameConfig, peerID);

            var annotatedBitmap = GetAnnotatedBitmap(frameConfig);

            if (annotatedBitmap != null)
            {
                bitmapSource.SetSourceBitmap(annotatedBitmap);
                annotatedBitmap.Dispose();
            }
        },
        null, TimeSpan.Zero, TimeSpan.FromMilliseconds(CUSTOM_FRAME_GENERATE_PERIOD_MILLISECONDS));
    }

    private async Task<FrameConfig> GetUpdatedFrameConfig(FrameConfig frameConfig, string peerID)
    {
        if (_peerConnectionPayState.TryGetIsPaid(peerID))
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

            var invoiceQrCode = frameConfig.QrCodeImage;
            if(invoiceQrCode == null)
            {
                invoiceQrCode = await GenerateQRCode(peerID);
            }

            return frameConfig with
            {
                BorderColour = Color.Yellow,
                QrCodeImage =invoiceQrCode,
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

    private async Task<Bitmap> GenerateQRCode(string peerID)
    {
        var invoice = await _lightningService.CreateInvoiceAsync(10000, "Pay me for flowers LOLZ.", 600);

        using QRCodeGenerator qrGenerator = new QRCodeGenerator();
        //using QRCodeData qrCodeData = qrGenerator.CreateQrCode($"{BASE_URL}/pay?id={peerID}", QRCodeGenerator.ECCLevel.Q);
        using QRCodeData qrCodeData = qrGenerator.CreateQrCode(invoice.PaymentRequest, QRCodeGenerator.ECCLevel.Q);
        using QRCode qrCode = new QRCode(qrCodeData);

        return qrCode.GetGraphic(20);
    }

    private Bitmap? GetAnnotatedBitmap(FrameConfig frameConfig)
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
            _logger.LogError("Exception GetAnnotatedBitmap. " + excp);
        }

        return null;
    }

    private void ApplyFilters(Image image, string timeStamp, string title, Color borderColor, int transparency, Bitmap? qrCode)
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

    private void HandlePeerConnectionStateChange(RTCPeerConnection pc, VideoBitmapSource bitmapSource, string peerID)
    {
        Timer? setBitmapSourceTimer = null;

        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug($"Peer connection state change to {state}.");

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

    private async Task CloseCustomBitmapSource(Timer? setBitmapSourceTimer, VideoBitmapSource bitmapSource)
    {
        if (setBitmapSourceTimer != null)
        {
            await setBitmapSourceTimer.DisposeAsync();
            setBitmapSourceTimer = null;
        }

        await bitmapSource.CloseVideo();
        bitmapSource.Dispose();
    }

    private void SetDiagnosticLogging(RTCPeerConnection pc)
    {
        // Diagnostics.
        pc.oniceconnectionstatechange += (state) => _logger.LogDebug($"ICE connection state change to {state}.");
        pc.onsignalingstatechange += () =>
        {
            if (pc.signalingState == RTCSignalingState.have_local_offer)
            {
                _logger.LogDebug($"Local SDP set, type {pc.localDescription.type}.");
                _logger.LogDebug(pc.localDescription.sdp.ToString());
            }
            else if (pc.signalingState == RTCSignalingState.have_remote_offer)
            {
                _logger.LogDebug($"Remote SDP set, type {pc.remoteDescription.type}.");
                _logger.LogDebug(pc.remoteDescription.sdp.ToString());
            }
        };
    }
}
