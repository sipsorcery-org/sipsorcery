using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.UI.Xaml.Controls;

namespace SIPSorceryMedia.Windows.Codecs
{
    public class PixelConverter
    {
        public static byte[] BitmapToRGBA(Bitmap bmp, int width, int height)
        {
            int pixelSize = 0;
            switch (bmp.PixelFormat)
            {
                case PixelFormat.Format24bppRgb:
                    pixelSize = 3;
                    break;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    pixelSize = 4;
                    break;
                default:
                    throw new ArgumentException($"Bitmap pixel format {bmp.PixelFormat} was not recognised in BitmapToRGBA.");
            }

            BitmapData bmpDate = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, bmp.PixelFormat);
            IntPtr ptr = bmpDate.Scan0;
            byte[] buffer = new byte[width * height * 4];
            int posn = 0;

            for (int y = 0; y <= height - 1; y++)
            {
                for (int x = 0; x <= width - 1; x++)
                {
                    int pos = y * bmpDate.Stride + x * pixelSize;

                    var r = Marshal.ReadByte(ptr, pos + 0);
                    var g = Marshal.ReadByte(ptr, pos + 1);
                    var b = Marshal.ReadByte(ptr, pos + 2);

                    buffer[posn++] = r;
                    buffer[posn++] = g;
                    buffer[posn++] = b;
                    buffer[posn++] = 0x00;
                }
            }

            bmp.UnlockBits(bmpDate);

            return buffer;
        }

        // https://msdn.microsoft.com/ja-jp/library/hh394035(v=vs.92).aspx
        // http://qiita.com/gomachan7/items/54d43693f943a0986e95
        public static byte[] RGBAtoI420(byte[] rgba, int width, int height)
        {
            int size = width * height;
            int uOffset = size;
            int vOffset = size + size / 4;
            int r, g, b, y, u, v;
            int posn = 0;

            byte[] buffer = new byte[width * height * 3 / 2];

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    r = rgba[posn++] & 0xff;
                    g = rgba[posn++] & 0xff;
                    b = rgba[posn++] & 0xff;
                    posn++; // Skip transparency byte.

                    y = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    u = (int)(-0.147 * r - 0.289 * g + 0.436 * b) + 128;
                    v = (int)(0.615 * r - 0.515 * g - 0.100 * b) + 128;

                    buffer[col + row * width] = (byte)(y > 255 ? 255 : y < 0 ? 0 : y);

                    int uvposn = col / 2 + row / 2 * width / 2;

                    buffer[uOffset + uvposn] = (byte)(u > 255 ? 255 : u < 0 ? 0 : u);
                    buffer[vOffset + uvposn] = (byte)(v > 255 ? 255 : v < 0 ? 0 : v);
                }
            }

            return buffer;
        }

        public static byte[] RGBtoI420(byte[] rgb, int width, int height)
        {
            int size = width * height;
            int uOffset = size;
            int vOffset = size + size / 4;
            int r, g, b, y, u, v;
            int posn = 0;

            byte[] buffer = new byte[width * height * 3 / 2];

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    r = rgb[posn++] & 0xff;
                    g = rgb[posn++] & 0xff;
                    b = rgb[posn++] & 0xff;

                    y = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    u = (int)(-0.147 * r - 0.289 * g + 0.436 * b) + 128;
                    v = (int)(0.615 * r - 0.515 * g - 0.100 * b) + 128;

                   buffer[col + row * width] = (byte)(y > 255 ? 255 : y < 0 ? 0 : y);

                    int uvposn = col / 2 + row / 2 * width / 2;

                    buffer[uOffset + uvposn] = (byte)(u > 255 ? 255 : u < 0 ? 0 : u);
                    buffer[vOffset + uvposn] = (byte)(v > 255 ? 255 : v < 0 ? 0 : v);
                }
            }

            return buffer;
        }

        /// <remarks>
        /// Constants taken from https://en.wikipedia.org/wiki/YUV.
        /// </remarks>
        public static byte[] I420toRGB(byte[] data, int width, int height)
        {
            int size = width * height;
            int uOffset = size;
            int vOffset = size + size / 4;
            byte[] rgb = new byte[size * 3];
            int posn = 0;
            int u, v, y;
            int r, g, b;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    y = data[col + row * width];
                    int uvposn = col / 2 + row / 2 * width / 2;

                    u = data[uOffset + uvposn] - 128;
                    v = data[vOffset + uvposn] - 128;

                    r = (int)(y + 1.140 * v);
                    g = (int)(y - 0.395 * u - 0.581 * v);
                    b = (int)(y + 2.302 * u);

                    rgb[posn++] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                    rgb[posn++] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    rgb[posn++] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                }
            }

            return rgb;
        }
    }
}
