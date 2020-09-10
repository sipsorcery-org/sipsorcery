using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

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

            int cnt = 0;
            for (int y = 0; y <= height - 1; y++)
            {
                for (int x = 0; x <= width - 1; x++)
                {
                    int pos = y * bmpDate.Stride + x * pixelSize;

                    var r = Marshal.ReadByte(ptr, pos + 0);
                    var g = Marshal.ReadByte(ptr, pos + 1);
                    var b = Marshal.ReadByte(ptr, pos + 2);

                    buffer[cnt + 0] = r; // r
                    buffer[cnt + 1] = g; // g
                    buffer[cnt + 2] = b; // b
                    buffer[cnt + 3] = 0x00;         // a
                    cnt += 4;
                }
            }

            bmp.UnlockBits(bmpDate);

            return buffer;
        }

        // https://msdn.microsoft.com/ja-jp/library/hh394035(v=vs.92).aspx
        // http://qiita.com/gomachan7/items/54d43693f943a0986e95
        public static byte[] RGBAtoYUV420Planar(byte[] rgba, int width, int height)
        {
            int frameSize = width * height;
            int yIndex = 0;
            int uIndex = frameSize;
            int vIndex = frameSize + (frameSize / 4);
            int r, g, b, y, u, v;
            int index = 0;

            byte[] buffer = new byte[width * height * 3 / 2];

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    r = rgba[index * 4 + 0] & 0xff;
                    g = rgba[index * 4 + 1] & 0xff;
                    b = rgba[index * 4 + 2] & 0xff;
                    // a = rgba[index * 4 + 3] & 0xff; unused

                    y = (int)(0.257 * r + 0.504 * g + 0.098 * b) + 16;
                    u = (int)(0.439 * r - 0.368 * g - 0.071 * b) + 128;
                    v = (int)(-0.148 * r - 0.291 * g + 0.439 * b) + 128;

                    buffer[yIndex++] = (byte)((y < 0) ? 0 : ((y > 255) ? 255 : y));

                    if (j % 2 == 0 && index % 2 == 0)
                    {
                        buffer[uIndex++] = (byte)((u < 0) ? 0 : ((u > 255) ? 255 : u));
                        buffer[vIndex++] = (byte)((v < 0) ? 0 : ((v > 255) ? 255 : v));
                    }

                    index++;
                }
            }

            return buffer;
        }

        public static byte[] RGBtoI420(byte[] rgb, int width, int height)
        {
            int frameSize = width * height;
            int yIndex = 0;
            int uIndex = frameSize;
            int vIndex = frameSize + (frameSize / 4);
            int r, g, b, y, u, v;
            int index = 0;

            byte[] buffer = new byte[width * height * 3 / 2];

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    r = rgb[index * 3 + 0] & 0xff;
                    g = rgb[index * 3 + 1] & 0xff;
                    b = rgb[index * 3 + 2] & 0xff;

                    y = (int)(0.257 * r + 0.504 * g + 0.098 * b) + 16;
                    u = (int)(0.439 * r - 0.368 * g - 0.071 * b) + 128;
                    v = (int)(-0.148 * r - 0.291 * g + 0.439 * b) + 128;

                    buffer[yIndex++] = (byte)((y < 0) ? 0 : ((y > 255) ? 255 : y));

                    if (j % 2 == 0 && index % 2 == 0)
                    {
                        buffer[uIndex++] = (byte)((u < 0) ? 0 : ((u > 255) ? 255 : u));
                        buffer[vIndex++] = (byte)((v < 0) ? 0 : ((v > 255) ? 255 : v));
                    }

                    index++;
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

                    r = (int)(y + 1.370705f * v);
                    g = (int)(y - 0.698001f * v + 0.337633f * u);
                    b = (int)(y + 1.732446f * u);

                    rgb[posn++] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                    rgb[posn++] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    rgb[posn++] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                }
            }

            return rgb;
        }
    }
}
