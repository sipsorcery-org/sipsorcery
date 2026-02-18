//-----------------------------------------------------------------------------
// Filename: ImgHelper.cs
//
// Description: Helper method to do pixel conversions and extract image data
// from VPX image objects.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Vpx.Net.UnitTest
{
    public unsafe class ImgHelper
    {
        public static byte[] I420toBGR(
            byte* yPlane, int yStride,
            byte* uPlane, int uStride,
            byte* vPlane, int vStride,
            int width, int height)
        {
            int size = width * height;
            byte[] rgb = new byte[size * 3];
            int posn = 0;
            int u, v, y;
            int r, g, b;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    y = yPlane[col + row * yStride];
                    u = uPlane[col / 2 + (row / 2) * uStride] - 128;
                    v = vPlane[col / 2 + (row / 2) * vStride] - 128;

                    r = (int)(y + 1.140 * v);
                    g = (int)(y - 0.395 * u - 0.581 * v);
                    b = (int)(y + 2.302 * u);

                    rgb[posn++] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                    rgb[posn++] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    rgb[posn++] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                }
            }

            return rgb;
        }

        public static byte[] BGRtoI420(byte[] bgr, int width, int height)
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
                    b = bgr[posn++] & 0xff;
                    g = bgr[posn++] & 0xff;
                    r = bgr[posn++] & 0xff;

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

        public static byte[] BitmapToByteArray(Bitmap bitmap)
        {
            BitmapData bmpdata = null;

            try
            {
                bmpdata = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                int numbytes = bmpdata.Stride * bitmap.Height;
                byte[] bytedata = new byte[numbytes];
                IntPtr ptr = bmpdata.Scan0;

                Marshal.Copy(ptr, bytedata, 0, numbytes);

                return bytedata;
            }
            finally
            {
                if (bmpdata != null)
                {
                    bitmap.UnlockBits(bmpdata);
                }
            }
        }

        public static byte[] BitmapToI420(Bitmap bmp)
        {
             return BGRtoI420(BitmapToByteArray(bmp), bmp.Width, bmp.Height);
        }
    }
}
