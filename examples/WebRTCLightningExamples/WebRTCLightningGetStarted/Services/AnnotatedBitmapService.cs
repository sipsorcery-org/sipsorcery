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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Net.Codecrete.QrCodeGenerator;
using System;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace demo;

public interface IAnnotatedBitmapGenerator
{
    Image<Rgba32>? GetAnnotatedBitmap(PaidVideoFrameConfig frameConfig);
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

    private readonly ILogger _logger;
    private readonly Font _font;

    private string? _lastPaymentRequest;
    private Task<Image>? _qrTask;
    private Image? _qrImage;

    public AnnotatedBitmapService(ILogger<AnnotatedBitmapService> logger)
    {
        _logger = logger;
        _font =  SystemFonts.CreateFont(FONT_FAMILY, TEXT_FONT_SIZE, FontStyle.Bold);
    }

    public Image<Rgba32>? GetAnnotatedBitmap(PaidVideoFrameConfig frameConfig)
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
            var image = Image.Load<Rgba32>(frameConfig.ImagePath);
            int width = image.Width;
            int height = image.Height;
            int pixelHeight = (int)(height * TEXT_SIZE_PERCENTAGE);

            image.Mutate(ctx =>
            {
                // 1) Draw border - need to draw each side separately like in original
                var borderColor = frameConfig.BorderColour;
                var borderWidth = BORDER_WIDTH;
                var borderPen = new SolidPen(borderColor, borderWidth);

                ctx.DrawLine(borderPen, new PointF(0, 0), new PointF(0, height));
                ctx.DrawLine(borderPen, new PointF(0, 0), new PointF(width, 0));
                ctx.DrawLine(borderPen, new PointF(width, 0), new PointF(width, height));
                ctx.DrawLine(borderPen, new PointF(0, height), new PointF(width, height));

                // 2) Add transparency overlay
                var overlayColor = new Rgba32(128, 128, 128, (byte)frameConfig.Opacity);
                ctx.Fill(overlayColor, new RectangleF(0, 0, width, height));

                // 3) Add header and footer text with outline effect
                var textOptions = new RichTextOptions(_font)
                {
                    Origin = new PointF(width / 2f, TEXT_MARGIN_PIXELS),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };

                // Draw title with outline
                if (!string.IsNullOrEmpty(frameConfig.Title))
                {
                    // Outline (draw multiple times with offset)
                    for (int i = 0; i < 4; i++)
                    {
                        var offset = i switch
                        {
                            0 => new PointF(-1, 0),
                            1 => new PointF(1, 0),
                            2 => new PointF(0, -1),
                            3 => new PointF(0, 1),
                            _ => PointF.Empty
                        };

                        textOptions.Origin = new PointF(width / 2f + offset.X, TEXT_MARGIN_PIXELS + offset.Y);
                        ctx.DrawText(textOptions, frameConfig.Title, Color.Black);
                    }

                    // Main text
                    textOptions.Origin = new PointF(width / 2f, TEXT_MARGIN_PIXELS);
                    ctx.DrawText(textOptions, frameConfig.Title, Color.White);
                }

                // Draw timestamp with outline
                var timestamp = DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff");
                var footerOptions = new RichTextOptions(_font)
                {
                    Origin = new PointF(width / 2f, height - TEXT_FONT_SIZE - TEXT_MARGIN_PIXELS),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };

                // Outline
                for (int i = 0; i < 4; i++)
                {
                    var offset = i switch
                    {
                        0 => new PointF(-1, 0),
                        1 => new PointF(1, 0),
                        2 => new PointF(0, -1),
                        3 => new PointF(0, 1),
                        _ => PointF.Empty
                    };

                    footerOptions.Origin = new PointF(width / 2f + offset.X,
                        height - pixelHeight - TEXT_MARGIN_PIXELS + offset.Y);
                    ctx.DrawText(footerOptions, timestamp, Color.Black);
                }

                // Main text
                footerOptions.Origin = new PointF(width / 2f, height - pixelHeight - TEXT_MARGIN_PIXELS);
                ctx.DrawText(footerOptions, timestamp, Color.White);

                // 4) Draw QR code if present
                if (_qrTask?.IsCompletedSuccessfully == true)
                {
                    _qrImage ??= _qrTask.Result;

                    int xCenter = (width - _qrImage.Width) / 2;
                    int yCenter = (height - _qrImage.Height) / 2;
                    ctx.DrawImage(_qrImage, new Point(xCenter, yCenter), 1f);
                }
            });

            return image;
        }
        catch (Exception excp)
        {
            _logger.LogError("Exception GetAnnotatedBitmap. " + excp);
            return null;
        }
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

    private Image CreateQrImageWithLogo(string pr, string logoPath)
    {
        const float logoWidth = 0.25f; // logo will have 15% the width of the QR code 

        var qr = QrCode.EncodeText(pr, QrCode.Ecc.Medium);

        var image = qr.ToBitmap(scale: QR_CODE_SCALE, border: QR_CODE_BORDER);
        var logo = Image.Load(logoPath);

        // resize logo
        var w = (int)Math.Round(image.Width * logoWidth);
        logo.Mutate(logo => logo.Resize(w, 0));

        // draw logo in center
        var topLeft = new Point((image.Width - logo.Width) / 2, (image.Height - logo.Height) / 2);
        image.Mutate(img => img.DrawImage(logo, topLeft, 1));

        //logo.Dispose();

        return image;
    }
}
