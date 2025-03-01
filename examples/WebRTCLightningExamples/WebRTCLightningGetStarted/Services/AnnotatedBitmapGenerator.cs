//-----------------------------------------------------------------------------
// Filename: AnnotatedBitmapGenerator.cs
//
// Description: Class to generate annotated Bitmaps. The original purpose was
// to serve as the source for a video source on a webrtc connection.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 25 Feb 2025	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using QRCoder;
using System.Drawing.Drawing2D;
using System.Drawing;
using System;
using Microsoft.Extensions.Logging;

namespace demo;

public interface IAnnotatedBitmapGenerator
{
    Bitmap? GetAnnotatedBitmap(FrameConfig frameConfig);
}

public class AnnotatedBitmapGenerator : IAnnotatedBitmapGenerator
{
    private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // Height of text as a percentage of the total image height.
    private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f;  // Black text outline thickness is set as a percentage of text height in pixels.
    private const int TEXT_MARGIN_PIXELS = 5;
    private const int POINTS_PER_INCH = 72;
    private const int BORDER_WIDTH = 5;
    private const int QR_CODE_DIMENSION = 200;

    private readonly ILogger _logger;

    public AnnotatedBitmapGenerator(ILogger<AnnotatedBitmapGenerator> logger)
    {
        _logger = logger;
    }

    public Bitmap? GetAnnotatedBitmap(FrameConfig frameConfig)
    {
        try
        {
            unsafe
            {
                string imagePath = frameConfig.ImagePath;
                using (Bitmap baseBitmap = new Bitmap(imagePath))
                {
                    var baseImage = baseBitmap.Clone() as Image;
                    if (baseImage != null)
                    {
                        ApplyFilters(
                            baseImage,
                            DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"),
                            frameConfig.Title,
                            frameConfig.BorderColour,
                            frameConfig.Opacity,
                            frameConfig.LightningPaymentRequest);

                        return baseImage as Bitmap;
                    }
                }
            }
        }
        catch (Exception excp)
        {
            _logger.LogError("Exception GetAnnotatedBitmap. " + excp);
        }

        return null;
    }

    private void ApplyFilters(Image image, string timeStamp, string title, Color borderColor, int transparency, string? lightningPaymentRequest)
    {
        int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

        using Graphics g = Graphics.FromImage(image);
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

        // TODO. VERY INEFFICIENT. Need to find a way to optimise this and avoid the problems that come with using shared Bitmaps for multi-threaded access.
        // Add QR Code.
        if (!string.IsNullOrWhiteSpace(lightningPaymentRequest))
        {
            using var qrCode = GenerateQRCode(lightningPaymentRequest);

            int xCenter = (image.Width - QR_CODE_DIMENSION) / 2;
            int yCenter = (image.Height - QR_CODE_DIMENSION) / 2;
            Rectangle dstRect = new Rectangle(xCenter, yCenter, QR_CODE_DIMENSION, QR_CODE_DIMENSION);
            g.DrawImage(qrCode, dstRect);
        }
    }

    private Bitmap GenerateQRCode(string lightningPaymentRequest)
    {
        using QRCodeGenerator qrGenerator = new();
        using QRCodeData qrCodeData = qrGenerator.CreateQrCode(lightningPaymentRequest, QRCodeGenerator.ECCLevel.Q);
        using QRCode qrCode = new(qrCodeData);

        Bitmap qrBitmap = qrCode.GetGraphic(20);
        return qrBitmap;
    }
}
