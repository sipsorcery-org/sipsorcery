//
// QR code generator library (.NET)
// https://github.com/manuelbl/QrCodeGenerator
//
// Copyright (c) 2021 Manuel Bleichenbacher
// Licensed under MIT License
// https://opensource.org/licenses/MIT
//
//
// Adapted from: https://raw.githubusercontent.com/manuelbl/QrCodeGenerator/refs/heads/master/Demo-ImageSharp/QrCodeBitmapExtensions.cs
// and ported from ImageSharp to SkiaSharp.

using SkiaSharp;
using System;
using System.IO;

namespace Net.Codecrete.QrCodeGenerator
{
    public static class QrCodeBitmapExtensions
    {
        /// <inheritdoc cref="ToBitmap(QrCode, int, int)"/>
        /// <param name="background">The background color.</param>
        /// <param name="foreground">The foreground color.</param>
        public static SKBitmap ToBitmap(this QrCode qrCode, int scale, int border, SKColor foreground, SKColor background)
        {
            // check arguments
            if (scale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Value out of range");
            }
            if (border < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(border), "Value out of range");
            }

            int size = qrCode.Size;
            int dim = (size + border * 2) * scale;

            if (dim > short.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Scale or border too large");
            }

            // create bitmap
            var bitmap = new SKBitmap(new SKImageInfo(dim, dim, SKColorType.Rgba8888, SKAlphaType.Premul));

            using (var canvas = new SKCanvas(bitmap))
            using (var paint = new SKPaint { Color = foreground, Style = SKPaintStyle.Fill, IsAntialias = false })
            {
                // draw background
                canvas.Clear(background);

                // draw modules
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if (qrCode.GetModule(x, y))
                        {
                            canvas.DrawRect((x + border) * scale, (y + border) * scale, scale, scale, paint);
                        }
                    }
                }
            }

            return bitmap;
        }

        /// <summary>
        /// Creates a bitmap (raster image) of this QR code.
        /// <para>
        /// The <paramref name="scale"/> parameter specifies the scale of the image, which is
        /// equivalent to the width and height of each QR code module. Additionally, the number
        /// of modules to add as a border to all four sides can be specified.
        /// </para>
        /// <para>
        /// For example, <c>ToBitmap(scale: 10, border: 4)</c> means to pad the QR code with 4 white
        /// border modules on all four sides, and use 10&#xD7;10 pixels to represent each module.
        /// </para>
        /// <para>
        /// The resulting bitmap uses the pixel format <see cref="SKColorType.Rgba8888"/>.
        /// If not specified, the foreground color is black (0x000000) und the background color always white (0xFFFFFF).
        /// </para>
        /// </summary>
        /// <param name="scale">The width and height, in pixels, of each module.</param>
        /// <param name="border">The number of border modules to add to each of the four sides.</param>
        /// <returns>The created bitmap representing this QR code.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is 0 or negative, <paramref name="border"/> is negative
        /// or the resulting image is wider than 32,768 pixels.</exception>
        public static SKBitmap ToBitmap(this QrCode qrCode, int scale, int border)
        {
            return qrCode.ToBitmap(scale, border, SKColors.Black, SKColors.White);
        }

        /// <inheritdoc cref="ToPng(QrCode, int, int)"/>
        /// <param name="background">The background color.</param>
        /// <param name="foreground">The foreground color.</param>
        public static byte[] ToPng(this QrCode qrCode, int scale, int border, SKColor foreground, SKColor background)
        {
            using var bitmap = qrCode.ToBitmap(scale, border, foreground, background);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        /// <summary>
        /// Creates a PNG image of this QR code and returns it as a byte array.
        /// <para>
        /// The <paramref name="scale"/> parameter specifies the scale of the image, which is
        /// equivalent to the width and height of each QR code module. Additionally, the number
        /// of modules to add as a border to all four sides can be specified.
        /// </para>
        /// <para>
        /// For example, <c>ToPng(scale: 10, border: 4)</c> means to pad the QR code with 4 white
        /// border modules on all four sides, and use 10&#xD7;10 pixels to represent each module.
        /// </para>
        /// <para>
        /// If not specified, the foreground color is black (0x000000) und the background color always white (0xFFFFFF).
        /// </para>
        /// </summary>
        /// <param name="scale">The width and height, in pixels, of each module.</param>
        /// <param name="border">The number of border modules to add to each of the four sides.</param>
        /// <returns>The created bitmap representing this QR code.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is 0 or negative, <paramref name="border"/> is negative
        /// or the resulting image is wider than 32,768 pixels.</exception>
        public static byte[] ToPng(this QrCode qrCode, int scale, int border)
        {
            return qrCode.ToPng(scale, border, SKColors.Black, SKColors.White);
        }

        /// <inheritdoc cref="SaveAsPng(QrCode, string, int, int)"/>
        /// <param name="background">The background color.</param>
        /// <param name="foreground">The foreground color.</param>
        public static void SaveAsPng(this QrCode qrCode, string filename, int scale, int border, SKColor foreground, SKColor background)
        {
            File.WriteAllBytes(filename, qrCode.ToPng(scale, border, foreground, background));
        }

        /// <summary>
        /// Saves this QR code as a PNG file.
        /// <para>
        /// The <paramref name="scale"/> parameter specifies the scale of the image, which is
        /// equivalent to the width and height of each QR code module. Additionally, the number
        /// of modules to add as a border to all four sides can be specified.
        /// </para>
        /// <para>
        /// For example, <c>SaveAsPng("qrcode.png", scale: 10, border: 4)</c> means to pad the QR code with 4 white
        /// border modules on all four sides, and use 10&#xD7;10 pixels to represent each module.
        /// </para>
        /// <para>
        /// If not specified, the foreground color is black (0x000000) und the background color always white (0xFFFFFF).
        /// </para>
        /// </summary>
        /// <param name="scale">The width and height, in pixels, of each module.</param>
        /// <param name="border">The number of border modules to add to each of the four sides.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is 0 or negative, <paramref name="border"/> is negative
        /// or the resulting image is wider than 32,768 pixels.</exception>
        public static void SaveAsPng(this QrCode qrCode, string filename, int scale, int border)
        {
            qrCode.SaveAsPng(filename, scale, border, SKColors.Black, SKColors.White);
        }
    }
}
