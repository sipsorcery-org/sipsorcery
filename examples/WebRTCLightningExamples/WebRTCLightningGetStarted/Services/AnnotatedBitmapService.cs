//-----------------------------------------------------------------------------
// Filename: AnnotatedBitmapService.cs
//
// Description: Service class to annotate bitmaps. The original purpose was
// to serve as the video source on a WebRTC connection.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 26 Apr 2025	Aaron Clauson	Ported AnnotatedBitmapService to use fully managed ImageSharp library.
// 21 Oct 2026	Aaron Clauson	Ported from ImageSharp to SkiaSharp.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SkiaSharp;
using Net.Codecrete.QrCodeGenerator;
using System;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace demo;

public interface IAnnotatedBitmapGenerator
{
    SKBitmap? GetAnnotatedBitmap(PaidVideoFrameConfig frameConfig);
}

public class AnnotatedBitmapService : IAnnotatedBitmapGenerator
{
    private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // Height of text as a percentage of the total image height.
    private const int TEXT_MARGIN_PIXELS = 5;
    private const int BORDER_WIDTH = 5;
    private const int QR_CODE_BORDER = 2;
    private const int QR_CODE_SCALE = 3; // Determines QR code size.
    private const float TEXT_FONT_SIZE = 16.0f;
    private const string FONT_FAMILY = "Verdana";

    private static readonly SKPoint[] _outlineOffsets =
    {
        new SKPoint(-1, 0),
        new SKPoint(1, 0),
        new SKPoint(0, -1),
        new SKPoint(0, 1)
    };

    private readonly ILogger _logger;
    private readonly SKFont _font;

    private string? _lastPaymentRequest;
    private Task<SKBitmap>? _qrTask;
    private SKBitmap? _qrImage;

    public AnnotatedBitmapService(ILogger<AnnotatedBitmapService> logger)
    {
        _logger = logger;
        _font = new SKFont(SKTypeface.FromFamilyName(FONT_FAMILY, SKFontStyle.Bold), TEXT_FONT_SIZE);
    }

    public SKBitmap? GetAnnotatedBitmap(PaidVideoFrameConfig frameConfig)
    {
        if (frameConfig.LightningPaymentRequest != _lastPaymentRequest)
        {
            DisposeQrCode();
            if (!string.IsNullOrWhiteSpace(frameConfig.LightningPaymentRequest))
            {
                StartQrCodeCreateTask(frameConfig.LightningPaymentRequest, frameConfig.QrCodeLogoPath);
            }
        }

        try
        {
            using var source = SKBitmap.Decode(frameConfig.ImagePath)
                ?? throw new ApplicationException($"Failed to decode image {frameConfig.ImagePath}.");

            int width = source.Width;
            int height = source.Height;
            int pixelHeight = (int)(height * TEXT_SIZE_PERCENTAGE);

            var image = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));

            using var canvas = new SKCanvas(image);
            canvas.DrawBitmap(source, 0, 0);

            // 1) Draw border - need to draw each side separately like in original
            using (var borderPaint = new SKPaint
            {
                Color = frameConfig.BorderColour,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = BORDER_WIDTH,
                IsAntialias = true
            })
            {
                canvas.DrawLine(0, 0, 0, height, borderPaint);
                canvas.DrawLine(0, 0, width, 0, borderPaint);
                canvas.DrawLine(width, 0, width, height, borderPaint);
                canvas.DrawLine(0, height, width, height, borderPaint);
            }

            // 2) Add transparency overlay
            using (var overlayPaint = new SKPaint { Color = new SKColor(128, 128, 128, (byte)frameConfig.Opacity) })
            {
                canvas.DrawRect(0, 0, width, height, overlayPaint);
            }

            // 3) Add header and footer text with outline effect
            if (!string.IsNullOrEmpty(frameConfig.Title))
            {
                DrawOutlinedText(canvas, frameConfig.Title, width / 2f, TEXT_MARGIN_PIXELS);
            }

            var timestamp = DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff");
            DrawOutlinedText(canvas, timestamp, width / 2f, height - pixelHeight - TEXT_MARGIN_PIXELS);

            // 4) Draw QR code if present
            if (_qrTask?.IsCompletedSuccessfully == true)
            {
                _qrImage ??= _qrTask.Result;

                int xCenter = (width - _qrImage.Width) / 2;
                int yCenter = (height - _qrImage.Height) / 2;
                canvas.DrawBitmap(_qrImage, xCenter, yCenter);
            }

            return image;
        }
        catch (Exception excp)
        {
            _logger.LogError("Exception GetAnnotatedBitmap. " + excp);
            return null;
        }
    }

    /// <summary>
    /// Draws horizontally centred white text with a black outline. The y coordinate is the top of the
    /// text rather than the baseline that SkiaSharp draws from.
    /// </summary>
    private void DrawOutlinedText(SKCanvas canvas, string text, float centreX, float top)
    {
        float baseline = top - _font.Metrics.Ascent;

        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };

        // Outline (draw multiple times with offset)
        foreach (var offset in _outlineOffsets)
        {
            canvas.DrawText(text, centreX + offset.X, baseline + offset.Y, SKTextAlign.Center, _font, paint);
        }

        // Main text
        paint.Color = SKColors.White;
        canvas.DrawText(text, centreX, baseline, SKTextAlign.Center, _font, paint);
    }

    private void DisposeQrCode()
    {
        if (_qrImage is not null)
        {
            _qrImage.Dispose();
            _qrImage = null;
            _qrTask = null;
        }
    }

    private void StartQrCodeCreateTask(string paymentRequest, string qrCodeLogoPath)
    {
        _qrTask = string.IsNullOrWhiteSpace(paymentRequest)
            ? null
            : Task.Run(() => CreateQrImageWithLogo(paymentRequest, qrCodeLogoPath));

        _lastPaymentRequest = paymentRequest;
    }

    private SKBitmap CreateQrImageWithLogo(string pr, string logoPath)
    {
        const float logoWidth = 0.25f; // logo will have 25% the width of the QR code

        var qr = QrCode.EncodeText(pr, QrCode.Ecc.Medium);

        var image = qr.ToBitmap(scale: QR_CODE_SCALE, border: QR_CODE_BORDER);
        using var logo = SKBitmap.Decode(logoPath);

        if (logo != null)
        {
            // resize logo, preserving its aspect ratio
            var w = (int)Math.Round(image.Width * logoWidth);
            var h = (int)Math.Round((double)logo.Height * w / logo.Width);
            using var resizedLogo = logo.Resize(new SKSizeI(w, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));

            // draw logo in center
            using var canvas = new SKCanvas(image);
            canvas.DrawBitmap(resizedLogo, (image.Width - resizedLogo.Width) / 2, (image.Height - resizedLogo.Height) / 2);
        }
        else
        {
            _logger.LogWarning("Failed to load QR code logo from {LogoPath}.", logoPath);
        }

        return image;
    }
}
