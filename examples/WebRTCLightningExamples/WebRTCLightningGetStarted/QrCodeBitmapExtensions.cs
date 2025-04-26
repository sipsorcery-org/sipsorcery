//
// QR code generator library (.NET)
// https://github.com/manuelbl/QrCodeGenerator
//
// Copyright (c) 2021 Manuel Bleichenbacher
// Licensed under MIT License
// https://opensource.org/licenses/MIT
//
//
// Copied from: https://raw.githubusercontent.com/manuelbl/QrCodeGenerator/refs/heads/master/Demo-ImageSharp/QrCodeBitmapExtensions.cs

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.IO;

namespace Net.Codecrete.QrCodeGenerator
{
    public static class QrCodeBitmapExtensions
    {
        /// <inheritdoc cref="ToBitmap(QrCode, int, int)"/>
        /// <param name="background">The background color.</param>
        /// <param name="foreground">The foreground color.</param>
        public static Image ToBitmap(this QrCode qrCode, int scale, int border, Color foreground, Color background)
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
            var image = new Image<Rgb24>(dim, dim);

            image.Mutate(img =>
            {
                // draw background
                img.Fill(background);

                // draw modules
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if (qrCode.GetModule(x, y))
                        {
                            img.Fill(foreground, new Rectangle((x + border) * scale, (y + border) * scale, scale, scale));
                        }
                    }
                }
            });

            return image;
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
        /// The resulting bitmap uses the pixel format <see cref="PixelFormat.Format24bppRgb"/>.
        /// If not specified, the foreground color is black (0x000000) und the background color always white (0xFFFFFF).
        /// </para>
        /// </summary>
        /// <param name="scale">The width and height, in pixels, of each module.</param>
        /// <param name="border">The number of border modules to add to each of the four sides.</param>
        /// <returns>The created bitmap representing this QR code.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="scale"/> is 0 or negative, <paramref name="border"/> is negative
        /// or the resulting image is wider than 32,768 pixels.</exception>
        public static Image ToBitmap(this QrCode qrCode, int scale, int border)
        {
            return qrCode.ToBitmap(scale, border, Color.Black, Color.White);
        }

        /// <inheritdoc cref="ToPng(QrCode, int, int)"/>
        /// <param name="background">The background color.</param>
        /// <param name="foreground">The foreground color.</param>
        public static byte[] ToPng(this QrCode qrCode, int scale, int border, Color foreground, Color background)
        {
            using var image = qrCode.ToBitmap(scale, border, foreground, background);
            using var ms = new MemoryStream();
            image.SaveAsPng(ms);
            return ms.ToArray();
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
            return qrCode.ToPng(scale, border, Color.Black, Color.White);
        }

        /// <inheritdoc cref="SaveAsPng(QrCode, string, int, int)"/>
        /// <param name="background">The background color.</param>
        /// <param name="foreground">The foreground color.</param>
        public static void SaveAsPng(this QrCode qrCode, string filename, int scale, int border, Color foreground, Color background)
        {
            using Image image = qrCode.ToBitmap(scale, border, foreground, background);
            image.SaveAsPng(filename);
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
            qrCode.SaveAsPng(filename, scale, border, Color.Black, Color.White);
        }
    }
}
