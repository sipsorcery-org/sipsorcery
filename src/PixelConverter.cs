using System;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorceryMedia.Abstractions
{
    public class PixelConverter
    {
        public static byte[] ToI420(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            byte[] i420Buffer = null;

            switch (pixelFormat)
            {
                case VideoPixelFormatsEnum.I420:
                    // No conversion needed.
                    i420Buffer = sample;
                    break;
                case VideoPixelFormatsEnum.Bgra:
                    i420Buffer = PixelConverter.RGBAtoI420(sample, width, height);
                    break;
                case VideoPixelFormatsEnum.Bgr:
                    i420Buffer = PixelConverter.BGRtoI420(sample, width, height);
                    break;
                case VideoPixelFormatsEnum.Rgb:
                    i420Buffer = PixelConverter.RGBtoI420(sample, width, height);
                    break;
                default:
                    throw new ApplicationException($"Pixel format {pixelFormat} does not have an I420 conversion implemented.");
            }

            return i420Buffer;
        }

        // https://docs.microsoft.com/en-us/previous-versions/visualstudio/hh394035(v=vs.105)
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

        public static byte[] I420toBGR(byte[] data, int width, int height)
        {
            int size = width * height;
            int uOffset = size;
            int vOffset = size + size / 4;
            byte[] bgr = new byte[size * 3];
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

                    b = (int)(y + 1.140 * v);
                    g = (int)(y - 0.395 * u - 0.581 * v);
                    r = (int)(y + 2.302 * u);

                    bgr[posn++] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                    bgr[posn++] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    bgr[posn++] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                }
            }

            return bgr;
        }

        public static byte[] NV12toBGR(byte[] data, int width, int height)
        {
            int size = width * height;
            int uvOffset = size;
            byte[] bgr = new byte[size * 3];
            int posn = 0;
            int u, v, y;
            int r, g, b;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    y = data[col + row * width];
                    int uvposn = row / 2 * width + col / 2 * 2;

                    u = data[uvOffset + uvposn] - 128;
                    v = data[uvOffset + uvposn + 1] - 128;

                    r = (int)(y + 1.140 * v);
                    g = (int)(y - 0.395 * u - 0.581 * v);
                    b = (int)(y + 2.302 * u);

                    bgr[posn++] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                    bgr[posn++] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    bgr[posn++] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                }
            }

            return bgr;
        }
    }
}
