using System;
using System.Collections.Generic;
#if NET8_0_OR_GREATER
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
#endif
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions
{
    public class PixelConverter
    {
        private static readonly Dictionary<int, ParallelOptions> _optDOP = new Dictionary<int, ParallelOptions>();

        [Obsolete("Use overload with stride parameter in order to deal with uneven dimensions.")]
        public static byte[] ToI420(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            switch (pixelFormat)
            {
                case VideoPixelFormatsEnum.I420:
                    return sample;
                case VideoPixelFormatsEnum.Bgra:
                    return PixelConverter.RGBAtoI420(sample, width, height, width * 4);
                case VideoPixelFormatsEnum.Bgr:
                    return PixelConverter.BGRtoI420(sample, width, height, width * 3);
                case VideoPixelFormatsEnum.Rgb:
                    return PixelConverter.RGBtoI420(sample, width, height, width * 3);
                default:
                    throw new ApplicationException($"Pixel format {pixelFormat} does not have an I420 conversion implemented.");
            }
        }

        /// <summary>
        /// Attempts to convert an image buffer into an I420 format.
        /// </summary>
        /// <param name="width">The width of the image in pixels.</param>
        /// <param name="height">The height of the image in pixels.</param>
        /// <param name="stride">The stride of the image. Currently this method can only convert RGB and BGR
        /// formats. For those formats the stride is typically: width x bytes per pixel. For example for
        /// a 640x480 RGB sample stride=640x3. For a 640x480 BGRA sample stride=640x4. Note in some cases 
        /// the stride could be greater than the width x bytes per pixel.</param>
        /// <param name="sample">The buffer containing the image data.</param>
        /// <param name="pixelFormat">The pixel format of the image.</param>
        /// <returns>If successful a buffer containing an I420 formatted image sample.</returns>
        public static byte[] ToI420(int width, int height, int stride, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            switch (pixelFormat)
            {
                case VideoPixelFormatsEnum.I420:
                    // No conversion needed.
                    return sample;
                case VideoPixelFormatsEnum.Bgra:
                    return PixelConverter.BGRAtoI420(sample, width, height, stride);
                case VideoPixelFormatsEnum.Bgr:
                    return PixelConverter.BGRtoI420(sample, width, height, stride);
                case VideoPixelFormatsEnum.Rgba:
                    return PixelConverter.RGBAtoI420(sample, width, height, stride);
                case VideoPixelFormatsEnum.Rgb:
                    return PixelConverter.RGBtoI420(sample, width, height, stride);
                case VideoPixelFormatsEnum.NV12:
                    return PixelConverter.NV12toI420(sample, width, height);
                default:
                    throw new ApplicationException($"Pixel format {pixelFormat} does not have an I420 conversion implemented.");
            }
        }

        [Obsolete("Use overload with stride parameter in order to deal with uneven dimensions.")]
        public static byte[] RGBAtoI420(byte[] rgba, int width, int height)
        {
            return RGBAtoI420(rgba, width, height, width * 4);
        }

        /// <summary>
        /// Converts an RGBA sample to an I420 formatted sample.
        /// </summary>
        /// <param name="rgba">The RGBA image sample.</param>
        /// <param name="width">The width in pixels of the RGBA sample.</param>
        /// <param name="height">The height in pixels of the RGBA sample.</param>
        /// <param name="stride">The stride of the RGBA sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>An I420 buffer representing the source image.</returns>
        /// <remarks>
        /// https://docs.microsoft.com/en-us/previous-versions/visualstudio/hh394035(v=vs.105)
        /// http://qiita.com/gomachan7/items/54d43693f943a0986e95
        /// </remarks>
        public static byte[] RGBAtoI420(byte[] rgba, int width, int height, int stride, int dop = 1)
        {
            if (rgba == null || rgba.Length < (stride * height))
            {
                throw new ApplicationException($"RGBA buffer supplied to RGBAtoI420 was too small, expected {stride * height} but got {rgba?.Length}.");
            }

            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;
            //int posn = 0;

            byte[] buffer = new byte[ySize + uvSize];

            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            Parallel.For(0, height, _optDOP[dop], (row) =>
            {
                int u, v, y;
                int r, g, b;

                for (int col = 0; col < width; col++)
                {
                    r = rgba[row * stride + col * 4] & 0xff;
                    g = rgba[row * stride + col * 4 + 1] & 0xff;
                    b = rgba[row * stride + col * 4 + 2] & 0xff;
                    //posn++; // Skip transparency byte.

                    y = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    u = (int)(-0.147 * r - 0.289 * g + 0.436 * b) + 128;
                    v = (int)(0.615 * r - 0.515 * g - 0.100 * b) + 128;

                    buffer[col + row * width] = (byte)(y > 255 ? 255 : y < 0 ? 0 : y);

                    int uvposn = col / 2 + row / 2 * width / 2;

                    buffer[uOffset + uvposn] = (byte)(u > 255 ? 255 : u < 0 ? 0 : u);
                    buffer[vOffset + uvposn] = (byte)(v > 255 ? 255 : v < 0 ? 0 : v);
                }
            });

            return buffer;
        }

        [Obsolete("Use overload with stride parameter in order to deal with uneven dimensions.")]
        public static byte[] RGBtoI420(byte[] rgb, int width, int height)
        {
            return RGBtoI420(rgb, width, height, width * 3);
        }

        /// <summary>
        /// Converts an RGB sample to an I420 formatted sample.
        /// </summary>
        /// <param name="rgb">The RGB image sample.</param>
        /// <param name="width">The width in pixels of the RGB sample.</param>
        /// <param name="height">The height in pixels of the RGB sample.</param>
        /// <param name="stride">The stride of the RGB sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>An I420 buffer representing the source image.</returns>
        public static byte[] RGBtoI420(byte[] rgb, int width, int height, int stride, int dop = 1)
        {
            if (rgb == null || rgb.Length < (stride * height))
            {
                throw new ApplicationException($"RGB buffer supplied to RGBtoI420 was too small, expected {stride * height} but got {rgb?.Length}.");
            }

            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;
            //int posn = 0;

            byte[] buffer = new byte[ySize + uvSize];

            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            Parallel.For(0, height, _optDOP[dop], (row) =>
            {
                int u, v, y;
                int r, g, b;

                for (int col = 0; col < width; col++)
                {
                    r = rgb[row * stride + col * 3] & 0xff;
                    g = rgb[row * stride + col * 3 + 1] & 0xff;
                    b = rgb[row * stride + col * 3 + 2] & 0xff;

                    y = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    u = (int)(-0.147 * r - 0.289 * g + 0.436 * b) + 128;
                    v = (int)(0.615 * r - 0.515 * g - 0.100 * b) + 128;

                    buffer[col + row * width] = (byte)(y > 255 ? 255 : y < 0 ? 0 : y);

                    int uvposn = col / 2 + row / 2 * width / 2;

                    buffer[uOffset + uvposn] = (byte)(u > 255 ? 255 : u < 0 ? 0 : u);
                    buffer[vOffset + uvposn] = (byte)(v > 255 ? 255 : v < 0 ? 0 : v);
                }
            });

            return buffer;
        }

        [Obsolete("Use overload with stride parameter in order to deal with uneven dimensions.")]
        public static byte[] BGRtoI420(byte[] bgr, int width, int height)
        {
            return BGRtoI420(bgr, width, height, width * 3);
        }

        /// <summary>
        /// Converts a BGR sample to an I420 formatted sample.
        /// </summary>
        /// <param name="bgr">The BGR image sample.</param>
        /// <param name="width">The width in pixels of the BGR sample.</param>
        /// <param name="height">The height in pixels of the BGR sample.</param>
        /// <param name="stride">The stride of the BGR sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>An I420 buffer representing the source image.</returns>
        public static byte[] BGRtoI420(byte[] bgr, int width, int height, int stride, int dop = 1)
        {
            if (bgr == null || bgr.Length < (stride * height))
            {
                throw new ApplicationException($"BGR buffer supplied to BGRtoI420 was too small, expected {stride * height} but got {bgr?.Length}.");
            }

            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;
            //int posn = 0;

            byte[] buffer = new byte[ySize + uvSize];

            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            Parallel.For(0, height, _optDOP[dop], (row) =>
            {
                int u, v, y;
                int r, g, b;

                for (int col = 0; col < width; col++)
                {
                    b = bgr[row * stride + col * 3] & 0xff;
                    g = bgr[row * stride + col * 3 + 1] & 0xff;
                    r = bgr[row * stride + col * 3 + 2] & 0xff;

                    y = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    u = (int)(-0.147 * r - 0.289 * g + 0.436 * b) + 128;
                    v = (int)(0.615 * r - 0.515 * g - 0.100 * b) + 128;

                    buffer[col + row * width] = (byte)(y > 255 ? 255 : y < 0 ? 0 : y);

                    int uvposn = col / 2 + row / 2 * width / 2;

                    buffer[uOffset + uvposn] = (byte)(u > 255 ? 255 : u < 0 ? 0 : u);
                    buffer[vOffset + uvposn] = (byte)(v > 255 ? 255 : v < 0 ? 0 : v);
                }
            });

            return buffer;
        }

        /// <summary>
        /// Converts a BGRA sample to an I420 formatted sample.
        /// </summary>
        /// <param name="bgra">The BGRA image sample.</param>
        /// <param name="width">The width in pixels of the BGRA sample.</param>
        /// <param name="height">The height in pixels of the BGRA sample.</param>
        /// <param name="stride">The stride of the BGRA sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>An I420 buffer representing the source image.</returns>
        public static byte[] BGRAtoI420(byte[] bgra, int width, int height, int stride, int dop = 1)
        {
            if (bgra == null || bgra.Length < (stride * height))
            {
                throw new ApplicationException($"BGRA buffer supplied to BGRAtoI420 was too small, expected {stride * height} but got {bgra?.Length}.");
            }

            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;

            byte[] buffer = new byte[ySize + uvSize];

            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            Parallel.For(0, height, _optDOP[dop], (row) =>
            {
                int u, v, y;
                int r, g, b;

                for (int col = 0; col < width; col++)
                {
                    // BGRA: Byte order is Blue, Green, Red, Alpha.
                    b = bgra[row * stride + col * 4] & 0xff;
                    g = bgra[row * stride + col * 4 + 1] & 0xff;
                    r = bgra[row * stride + col * 4 + 2] & 0xff;
                    // Alpha at index 3 is ignored.

                    y = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    u = (int)(-0.147 * r - 0.289 * g + 0.436 * b) + 128;
                    v = (int)(0.615 * r - 0.515 * g - 0.100 * b) + 128;

                    buffer[col + row * width] = (byte)(y > 255 ? 255 : y < 0 ? 0 : y);

                    int uvposn = (col / 2) + (row / 2) * (width / 2);
                    buffer[uOffset + uvposn] = (byte)(u > 255 ? 255 : u < 0 ? 0 : u);
                    buffer[vOffset + uvposn] = (byte)(v > 255 ? 255 : v < 0 ? 0 : v);
                }
            });

            return buffer;
        }


        [Obsolete("Use overload with stride parameter in order to deal with uneven dimensions.")]
        public static byte[] I420toRGB(byte[] data, int width, int height)
        {
            return I420toRGB(data, width, height, out _);
        }

        /// <summary>
        /// Converts an I420 sample to an RGB formatted sample.
        /// </summary>
        /// <param name="data">The I420 image sample.</param>
        /// <param name="width">The width in pixels of the I420 sample.</param>
        /// <param name="height">The height in pixels of the I420 sample.</param>
        /// <param name="stride">The stride to use for the desintation RGB sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>An RGB buffer representing the source image.</returns>
        public static byte[] I420toRGB(byte[] data, int width, int height, out int stride, int dop = 1)
        {
            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            if (data == null || data.Length < (ySize + uvSize))
            {
                throw new ApplicationException($"I420 buffer supplied to I420toRGB was too small, expected {ySize + uvSize} but got {data?.Length}.");
            }

            int uOffset = ySize;
            int vOffset = ySize + ySize / 4;
            int lclStride = stride = (width * 3 + 3) / 4 * 4;
            byte[] rgb = new byte[height * stride];
            //int posn = 0;

            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            Parallel.For(0, height, _optDOP[dop], (row) =>
            {
                int u, v, y;
                int r, g, b;

                for (int col = 0; col < width; col++)
                {
                    y = data[col + row * width];
                    int uvposn = col / 2 + row / 2 * width / 2;

                    u = data[uOffset + uvposn] - 128;
                    v = data[vOffset + uvposn] - 128;

                    r = (int)(y + 1.140 * v);
                    g = (int)(y - 0.395 * u - 0.581 * v);
                    b = (int)(y + 2.302 * u);

                    rgb[row * lclStride + col * 3] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                    rgb[row * lclStride + col * 3 + 1] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    rgb[row * lclStride + col * 3 + 2] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                }
            });

            return rgb;
        }

        [Obsolete("Use overload with stride parameter in order to deal with uneven dimensions.")]
        public static byte[] I420toBGR(byte[] data, int width, int height)
        {
            return I420toBGR(data, width, height, out _);
        }

        /// <summary>
        /// Converts an I420 sample to an BGR formatted sample.
        /// </summary>
        /// <param name="data">The I420 image sample.</param>
        /// <param name="width">The width in pixels of the I420 sample.</param>
        /// <param name="height">The height in pixels of the I420 sample.</param>
        /// <param name="stride">The stride to use for the desintation BGR sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>A BGR buffer representing the source image.</returns>
        public static byte[] I420toBGR(byte[] data, int width, int height, out int stride, int dop = 1)
        {
            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            if (data == null || data.Length < (ySize + uvSize))
            {
                throw new ApplicationException($"I420 buffer supplied to I420toBGR was too small, expected {ySize + uvSize} but got {data?.Length}.");
            }

            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;
            var lclStride = stride = (width * 3 + 3) / 4 * 4;
            byte[] bgr = new byte[height * stride];
            //int posn = 0;

            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            Parallel.For(0, height, _optDOP[dop], (row) =>
            {
                int u, v, y;
                int r, g, b;

                for (int col = 0; col < width; col++)
                {
                    y = data[col + row * width];
                    int uvposn = col / 2 + row / 2 * width / 2;

                    u = data[uOffset + uvposn] - 128;
                    v = data[vOffset + uvposn] - 128;

                    b = (int)(y + 1.140 * v);
                    g = (int)(y - 0.395 * u - 0.581 * v);
                    r = (int)(y + 2.302 * u);

                    bgr[row * lclStride + col * 3] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                    bgr[row * lclStride + col * 3 + 1] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    bgr[row * lclStride + col * 3 + 2] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                }
            });

            return bgr;
        }

        /// <summary>
        /// Converts a BGR formatted sample to an RGB formatted sample by swapping the red and blue channels.
        /// </summary>
        /// <param name="bgr">The BGR image sample.</param>
        /// <param name="width">The width in pixels of the image.</param>
        /// <param name="height">The height in pixels of the image.</param>
        /// <returns>An RGB buffer representing the source image.</returns>
        public static byte[] BGRtoRGB(byte[] bgr, int width, int height)
        {
            return BGRtoRGB(bgr, width, height, width * 3);
        }

        /// <summary>
        /// Converts a BGR formatted sample to an RGB formatted sample by swapping the red and blue channels.
        /// </summary>
        /// <param name="bgr">The BGR image sample.</param>
        /// <param name="width">The width in pixels of the image.</param>
        /// <param name="height">The height in pixels of the image.</param>
        /// <param name="stride">The stride of the BGR sample.</param>
        /// <returns>An RGB buffer representing the source image.</returns>
        public static byte[] BGRtoRGB(byte[] bgr, int width, int height, int stride)
        {
            if (bgr == null || bgr.Length < stride * height)
            {
                throw new ApplicationException($"BGR buffer supplied to BGRtoRGB was too small, expected {stride * height} but got {bgr?.Length}.");
            }

            int rgbStride = (width * 3 + 3) / 4 * 4;
            byte[] rgb = new byte[height * rgbStride];

#if NET8_0_OR_GREATER
            SwapRBChannelsSimd(bgr, rgb, width, height, stride, rgbStride);
#else
            SwapRBChannelsScalar(bgr, rgb, width, height, stride, rgbStride);
#endif

            return rgb;
        }

        /// <summary>
        /// Converts an RGB formatted sample to a BGR formatted sample by swapping the red and blue channels.
        /// </summary>
        /// <param name="rgb">The RGB image sample.</param>
        /// <param name="width">The width in pixels of the image.</param>
        /// <param name="height">The height in pixels of the image.</param>
        /// <returns>A BGR buffer representing the source image.</returns>
        public static byte[] RGBtoBGR(byte[] rgb, int width, int height)
        {
            return RGBtoBGR(rgb, width, height, width * 3);
        }

        /// <summary>
        /// Converts an RGB formatted sample to a BGR formatted sample by swapping the red and blue channels.
        /// </summary>
        /// <param name="rgb">The RGB image sample.</param>
        /// <param name="width">The width in pixels of the image.</param>
        /// <param name="height">The height in pixels of the image.</param>
        /// <param name="stride">The stride of the RGB sample.</param>
        /// <returns>A BGR buffer representing the source image.</returns>
        public static byte[] RGBtoBGR(byte[] rgb, int width, int height, int stride)
        {
            if (rgb == null || rgb.Length < stride * height)
            {
                throw new ApplicationException($"RGB buffer supplied to RGBtoBGR was too small, expected {stride * height} but got {rgb?.Length}.");
            }

            int bgrStride = (width * 3 + 3) / 4 * 4;
            byte[] bgr = new byte[height * bgrStride];

#if NET8_0_OR_GREATER
            SwapRBChannelsSimd(rgb, bgr, width, height, stride, bgrStride);
#else
            SwapRBChannelsScalar(rgb, bgr, width, height, stride, bgrStride);
#endif

            return bgr;
        }

        /// <summary>
        /// Swaps the red and blue channels in a 24-bit RGB/BGR image using scalar operations.
        /// This method is used for converting between BGR and RGB formats.
        /// </summary>
        private static void SwapRBChannelsScalar(byte[] src, byte[] dst, int width, int height, int srcStride, int dstStride)
        {
            for (int row = 0; row < height; row++)
            {
                int srcRowOffset = row * srcStride;
                int dstRowOffset = row * dstStride;

                for (int col = 0; col < width; col++)
                {
                    int srcPixelOffset = srcRowOffset + col * 3;
                    int dstPixelOffset = dstRowOffset + col * 3;

                    // Swap first and third byte (swaps R/B channels regardless of direction)
                    dst[dstPixelOffset] = src[srcPixelOffset + 2];     // Third byte -> position 0
                    dst[dstPixelOffset + 1] = src[srcPixelOffset + 1]; // Middle byte stays in place
                    dst[dstPixelOffset + 2] = src[srcPixelOffset];     // First byte -> position 2
                }
            }
        }

        [Obsolete("Use overload with stride parameter in order to deal with uneven dimensions.")]
        public static byte[] NV12toBGR(byte[] data, int width, int height)
        {
            return NV12toBGR(data, width, height, width * 3);
        }

        /// <summary>
        /// Converts an NV12 sample to an BGR formatted sample.
        /// </summary>
        /// <param name="data">The NV12 image sample.</param>
        /// <param name="width">The width in pixels of the NV12 sample.</param>
        /// <param name="height">The height in pixels of the NV12 sample.</param>
        /// <param name="stride">The stride to use for the desintation BGR sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>A BGR buffer representing the source image.</returns>
        public static byte[] NV12toBGR(byte[] data, int width, int height, int stride, int dop = 1)
        {
            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            if (data == null || data.Length < (ySize + uvSize))
            {
                throw new ApplicationException($"NV12 buffer supplied to NV12toBGR was too small, expected {ySize + uvSize} but got {data?.Length}.");
            }

            int uvOffset = ySize;
            byte[] bgr = new byte[height * stride];
            //int posn = 0;

            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            Parallel.For(0, height, _optDOP[dop], (row) =>
            {
                int u, v, y;
                int r, g, b;

                for (int col = 0; col < width; col++)
                {
                    y = data[col + row * width];
                    int uvposn = row / 2 * width + col / 2 * 2;

                    u = data[uvOffset + uvposn] - 128;
                    v = data[uvOffset + uvposn + 1] - 128;

                    r = (int)(y + 1.140 * v);
                    g = (int)(y - 0.395 * u - 0.581 * v);
                    b = (int)(y + 2.302 * u);

                    bgr[row * stride + col * 3] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                    bgr[row * stride + col * 3 + 1] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    bgr[row * stride + col * 3 + 2] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                }
            });

            return bgr;
        }

        /// <summary>
        /// Converts an NV12 sample to an I420 formatted sample.
        /// NV12: Y plane followed by interleaved UV plane (UVUVUV...).
        /// I420: Y plane followed by U plane, then V plane (planar format).
        /// </summary>
        /// <param name="nv12">The NV12 image sample.</param>
        /// <param name="width">The width in pixels of the NV12 sample.</param>
        /// <param name="height">The height in pixels of the NV12 sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>An I420 buffer representing the source image.</returns>
        public static byte[] NV12toI420(byte[] nv12, int width, int height, int dop = 1)
        {
            int ySize = width * height;
            int uvWidth = (width + 1) / 2;
            int uvHeight = (height + 1) / 2;
            int uvSize = uvWidth * uvHeight * 2;

            if (nv12 == null || nv12.Length < (ySize + uvSize))
            {
                throw new ApplicationException($"NV12 buffer supplied to NV12toI420 was too small, expected {ySize + uvSize} but got {nv12?.Length}.");
            }

            byte[] i420 = new byte[ySize + uvSize];

            // Copy Y plane (same layout in both formats).
            Buffer.BlockCopy(nv12, 0, i420, 0, ySize);

            int nv12UvOffset = ySize;
            int i420UOffset = ySize;
            int i420VOffset = ySize + uvWidth * uvHeight;

#if NET8_0_OR_GREATER
            // Use SIMD for de-interleaving UV plane when available
            DeinterleaveUVSimd(nv12, nv12UvOffset, i420, i420UOffset, i420VOffset, uvWidth, uvHeight);
#else
            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            // De-interleave UV plane: NV12 has UV interleaved, I420 has separate U and V planes.
            Parallel.For(0, uvHeight, _optDOP[dop], (row) =>
            {
                for (int col = 0; col < uvWidth; col++)
                {
                    int nv12Posn = nv12UvOffset + row * uvWidth * 2 + col * 2;
                    int i420UPosn = i420UOffset + row * uvWidth + col;
                    int i420VPosn = i420VOffset + row * uvWidth + col;

                    i420[i420UPosn] = nv12[nv12Posn];       // U
                    i420[i420VPosn] = nv12[nv12Posn + 1];   // V
                }
            });
#endif

            return i420;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// SIMD-optimized de-interleave of UV plane from NV12 format (UVUVUV...) to I420 format (separate U and V planes).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DeinterleaveUVSimd(byte[] src, int srcOffset, byte[] dst, int dstUOffset, int dstVOffset, int uvWidth, int uvHeight)
        {
            int totalUV = uvWidth * uvHeight;
            int i = 0;

            ref byte srcRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), srcOffset);
            ref byte dstURef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), dstUOffset);
            ref byte dstVRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), dstVOffset);

            // Process 32 UV pairs at a time (64 bytes) using Vector256
            if (Vector256.IsHardwareAccelerated)
            {
                // Indices for de-interleaving: extract U values (even positions) and V values (odd positions)
                // For byte pairs: [U0,V0,U1,V1,U2,V2,...] -> U: [U0,U1,U2,...], V: [V0,V1,V2,...]
                for (; i <= totalUV - 32; i += 32)
                {
                    // Load 64 bytes (32 UV pairs)
                    var uv0 = Vector256.LoadUnsafe(ref Unsafe.Add(ref srcRef, i * 2));
                    var uv1 = Vector256.LoadUnsafe(ref Unsafe.Add(ref srcRef, i * 2 + 32));

                    // Use shuffle to de-interleave - extract even bytes (U) and odd bytes (V)
                    var (u0, v0) = DeinterleaveVector256(uv0);
                    var (u1, v1) = DeinterleaveVector256(uv1);

                    // Combine into 256-bit vectors
                    var u = Vector256.Create(u0, u1);
                    var v = Vector256.Create(v0, v1);

                    u.StoreUnsafe(ref Unsafe.Add(ref dstURef, i));
                    v.StoreUnsafe(ref Unsafe.Add(ref dstVRef, i));
                }
            }

            // Process 16 UV pairs at a time (32 bytes) using Vector128
            if (Vector128.IsHardwareAccelerated)
            {
                for (; i <= totalUV - 16; i += 16)
                {
                    // Load 32 bytes (16 UV pairs)
                    var uv0 = Vector128.LoadUnsafe(ref Unsafe.Add(ref srcRef, i * 2));
                    var uv1 = Vector128.LoadUnsafe(ref Unsafe.Add(ref srcRef, i * 2 + 16));

                    var (u0, v0) = DeinterleaveVector128(uv0);
                    var (u1, v1) = DeinterleaveVector128(uv1);

                    var u = Vector128.Create(u0, u1);
                    var v = Vector128.Create(v0, v1);

                    u.StoreUnsafe(ref Unsafe.Add(ref dstURef, i));
                    v.StoreUnsafe(ref Unsafe.Add(ref dstVRef, i));
                }
            }

            // Handle remaining elements with scalar code
            for (; i < totalUV; i++)
            {
                Unsafe.Add(ref dstURef, i) = Unsafe.Add(ref srcRef, i * 2);
                Unsafe.Add(ref dstVRef, i) = Unsafe.Add(ref srcRef, i * 2 + 1);
            }
        }

        /// <summary>
        /// De-interleave 16 byte pairs from a Vector256 into two Vector128 containing U and V values.
        /// Input: [U0,V0,U1,V1,U2,V2,...,U15,V15]
        /// Output: U=[U0,U1,...,U15], V=[V0,V1,...,V15]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (Vector128<byte> u, Vector128<byte> v) DeinterleaveVector256(Vector256<byte> uv)
        {
            // Extract low and high 128-bit halves
            var low = uv.GetLower();   // [U0,V0,U1,V1,U2,V2,U3,V3,U4,V4,U5,V5,U6,V6,U7,V7]
            var high = uv.GetUpper();  // [U8,V8,U9,V9,U10,V10,U11,V11,U12,V12,U13,V13,U14,V14,U15,V15]

            var (uLow, vLow) = DeinterleaveVector128(low);
            var (uHigh, vHigh) = DeinterleaveVector128(high);

            // Combine halves
            var u = Vector128.Create(uLow, uHigh);
            var v = Vector128.Create(vLow, vHigh);

            return (u, v);
        }

        /// <summary>
        /// De-interleave 8 byte pairs from a Vector128 into two Vector64 containing U and V values.
        /// Input: [U0,V0,U1,V1,U2,V2,U3,V3,U4,V4,U5,V5,U6,V6,U7,V7]
        /// Output: U=[U0,U1,U2,U3,U4,U5,U6,U7], V=[V0,V1,V2,V3,V4,V5,V6,V7]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (Vector64<byte> u, Vector64<byte> v) DeinterleaveVector128(Vector128<byte> uv)
        {
            // Shuffle bytes to gather all U values in low 64 bits and V values in high 64 bits
            // This shuffle pattern extracts even indices (U) to the first 8 bytes and odd indices (V) to the last 8 bytes
            var shuffleIndices = Vector128.Create(
                (byte)0, 2, 4, 6, 8, 10, 12, 14,  // U indices (even positions)
                1, 3, 5, 7, 9, 11, 13, 15         // V indices (odd positions)
            );

            var shuffled = Vector128.Shuffle(uv, shuffleIndices);

            return (shuffled.GetLower(), shuffled.GetUpper());
        }
#endif

        /// <summary>
        /// Converts an I420 sample to an NV12 formatted sample.
        /// I420: Y plane followed by U plane, then V plane (planar format).
        /// NV12: Y plane followed by interleaved UV plane (UVUVUV...).
        /// </summary>
        /// <param name="i420">The I420 image sample.</param>
        /// <param name="width">The width in pixels of the I420 sample.</param>
        /// <param name="height">The height in pixels of the I420 sample.</param>
        /// <param name="dop">The degree of parallelism for converting.</param>
        /// <returns>An NV12 buffer representing the source image.</returns>
        public static byte[] I420toNV12(byte[] i420, int width, int height, int dop = 1)
        {
            int ySize = width * height;
            int uvWidth = (width + 1) / 2;
            int uvHeight = (height + 1) / 2;
            int uvSize = uvWidth * uvHeight * 2;

            if (i420 == null || i420.Length < (ySize + uvSize))
            {
                throw new ApplicationException($"I420 buffer supplied to I420toNV12 was too small, expected {ySize + uvSize} but got {i420?.Length}.");
            }

            byte[] nv12 = new byte[ySize + uvSize];

            // Copy Y plane (same layout in both formats).
            Buffer.BlockCopy(i420, 0, nv12, 0, ySize);

            int i420UOffset = ySize;
            int i420VOffset = ySize + uvWidth * uvHeight;
            int nv12UvOffset = ySize;

#if NET8_0_OR_GREATER
            // Use SIMD for interleaving U and V planes when available
            InterleaveUVSimd(i420, i420UOffset, i420VOffset, nv12, nv12UvOffset, uvWidth, uvHeight);
#else
            if (!_optDOP.ContainsKey(dop))
                _optDOP[dop] = new ParallelOptions() { MaxDegreeOfParallelism = dop };

            // Interleave UV plane: I420 has separate U and V planes, NV12 has UV interleaved.
            Parallel.For(0, uvHeight, _optDOP[dop], (row) =>
            {
                for (int col = 0; col < uvWidth; col++)
                {
                    int i420UPosn = i420UOffset + row * uvWidth + col;
                    int i420VPosn = i420VOffset + row * uvWidth + col;
                    int nv12Posn = nv12UvOffset + row * uvWidth * 2 + col * 2;

                    nv12[nv12Posn] = i420[i420UPosn];       // U
                    nv12[nv12Posn + 1] = i420[i420VPosn];   // V
                }
            });
#endif

            return nv12;
        }

#if NET8_0_OR_GREATER
        /// <summary>
        /// SIMD-optimized interleave of separate U and V planes from I420 format to NV12 format (UVUVUV...).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void InterleaveUVSimd(byte[] src, int srcUOffset, int srcVOffset, byte[] dst, int dstOffset, int uvWidth, int uvHeight)
        {
            int totalUV = uvWidth * uvHeight;
            int i = 0;

            ref byte srcURef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), srcUOffset);
            ref byte srcVRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), srcVOffset);
            ref byte dstRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), dstOffset);

            // Process 32 U/V values at a time using Vector256
            if (Vector256.IsHardwareAccelerated)
            {
                for (; i <= totalUV - 32; i += 32)
                {
                    // Load 32 U values and 32 V values
                    var u = Vector256.LoadUnsafe(ref Unsafe.Add(ref srcURef, i));
                    var v = Vector256.LoadUnsafe(ref Unsafe.Add(ref srcVRef, i));

                    // Interleave U and V values
                    var (uv0, uv1) = InterleaveVector256(u, v);

                    // Store 64 bytes (32 UV pairs)
                    uv0.StoreUnsafe(ref Unsafe.Add(ref dstRef, i * 2));
                    uv1.StoreUnsafe(ref Unsafe.Add(ref dstRef, i * 2 + 32));
                }
            }

            // Process 16 U/V values at a time using Vector128
            if (Vector128.IsHardwareAccelerated)
            {
                for (; i <= totalUV - 16; i += 16)
                {
                    // Load 16 U values and 16 V values
                    var u = Vector128.LoadUnsafe(ref Unsafe.Add(ref srcURef, i));
                    var v = Vector128.LoadUnsafe(ref Unsafe.Add(ref srcVRef, i));

                    // Interleave U and V values
                    var (uv0, uv1) = InterleaveVector128(u, v);

                    // Store 32 bytes (16 UV pairs)
                    uv0.StoreUnsafe(ref Unsafe.Add(ref dstRef, i * 2));
                    uv1.StoreUnsafe(ref Unsafe.Add(ref dstRef, i * 2 + 16));
                }
            }

            // Handle remaining elements with scalar code
            for (; i < totalUV; i++)
            {
                Unsafe.Add(ref dstRef, i * 2) = Unsafe.Add(ref srcURef, i);
                Unsafe.Add(ref dstRef, i * 2 + 1) = Unsafe.Add(ref srcVRef, i);
            }
        }

        /// <summary>
        /// Interleave two Vector256 of U and V values into two Vector256 of interleaved UV pairs.
        /// Input: U=[U0,U1,...,U31], V=[V0,V1,...,V31]
        /// Output: UV0=[U0,V0,U1,V1,...,U15,V15], UV1=[U16,V16,...,U31,V31]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (Vector256<byte> uv0, Vector256<byte> uv1) InterleaveVector256(Vector256<byte> u, Vector256<byte> v)
        {
            // Get low and high halves
            var uLow = u.GetLower();   // U0-U15
            var uHigh = u.GetUpper();  // U16-U31
            var vLow = v.GetLower();   // V0-V15
            var vHigh = v.GetUpper();  // V16-V31

            // Interleave low halves -> first 32 bytes
            var (uv0Low, uv0High) = InterleaveVector128ToTwo(uLow, vLow);
            var uv0 = Vector256.Create(uv0Low, uv0High);

            // Interleave high halves -> second 32 bytes
            var (uv1Low, uv1High) = InterleaveVector128ToTwo(uHigh, vHigh);
            var uv1 = Vector256.Create(uv1Low, uv1High);

            return (uv0, uv1);
        }

        /// <summary>
        /// Interleave two Vector128 of U and V values into two Vector128 of interleaved UV pairs.
        /// Input: U=[U0,U1,...,U15], V=[V0,V1,...,V15]
        /// Output: UV0=[U0,V0,U1,V1,...,U7,V7], UV1=[U8,V8,...,U15,V15]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (Vector128<byte> uv0, Vector128<byte> uv1) InterleaveVector128(Vector128<byte> u, Vector128<byte> v)
        {
            return InterleaveVector128ToTwo(u, v);
        }

        /// <summary>
        /// Interleave two Vector128 of 16 bytes each into two Vector128 of interleaved pairs.
        /// Input: A=[A0,A1,...,A15], B=[B0,B1,...,B15]
        /// Output: Out0=[A0,B0,A1,B1,...,A7,B7], Out1=[A8,B8,...,A15,B15]
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static (Vector128<byte> out0, Vector128<byte> out1) InterleaveVector128ToTwo(Vector128<byte> a, Vector128<byte> b)
        {
            // Create interleave shuffle patterns for low and high halves
            // Low: takes elements 0-7 from A and B, interleaves them
            // Pattern for first 8 pairs: A0,B0,A1,B1,A2,B2,A3,B3,A4,B4,A5,B5,A6,B6,A7,B7
            var shuffleLowA = Vector128.Create((byte)0, 255, 1, 255, 2, 255, 3, 255, 4, 255, 5, 255, 6, 255, 7, 255);
            var shuffleLowB = Vector128.Create((byte)255, 0, 255, 1, 255, 2, 255, 3, 255, 4, 255, 5, 255, 6, 255, 7);
            
            // Pattern for second 8 pairs: A8,B8,A9,B9,...,A15,B15
            var shuffleHighA = Vector128.Create((byte)8, 255, 9, 255, 10, 255, 11, 255, 12, 255, 13, 255, 14, 255, 15, 255);
            var shuffleHighB = Vector128.Create((byte)255, 8, 255, 9, 255, 10, 255, 11, 255, 12, 255, 13, 255, 14, 255, 15);

            // Shuffle and OR to combine
            var out0 = Vector128.Shuffle(a, shuffleLowA) | Vector128.Shuffle(b, shuffleLowB);
            var out1 = Vector128.Shuffle(a, shuffleHighA) | Vector128.Shuffle(b, shuffleHighB);

            return (out0, out1);
        }

        /// <summary>
        /// SIMD-optimized swap of R and B channels for converting between BGR and RGB formats.
        /// Processes pixels in batches using Vector128 operations where possible.
        /// </summary>
        private static void SwapRBChannelsSimd(byte[] src, byte[] dst, int width, int height, int srcStride, int dstStride)
        {
            for (int row = 0; row < height; row++)
            {
                int srcRowOffset = row * srcStride;
                int dstRowOffset = row * dstStride;
                SwapRBChannelsRowSimd(src, dst, srcRowOffset, dstRowOffset, width);
            }
        }

        /// <summary>
        /// SIMD swap of R and B channels for a single row.
        /// Uses Vector128 operations to process 16 pixels (48 bytes) at a time.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SwapRBChannelsRowSimd(byte[] src, byte[] dst, int srcOffset, int dstOffset, int width)
        {
            int pixelBytes = width * 3;
            int i = 0;

            ref byte srcRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(src), srcOffset);
            ref byte dstRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(dst), dstOffset);

            // Process 16 pixels at a time (48 bytes) using Vector128
            // Since 48 bytes spans 3 Vector128 chunks (16 bytes each), we process all 3 together
            if (Vector128.IsHardwareAccelerated)
            {
                // Pre-computed shuffle patterns for swapping R and B channels across 3 Vector128 chunks
                // These patterns handle the cross-boundary shuffling required for 3-byte pixels

                // Output 0 (bytes 0-15): needs pixels 0-4 swapped, plus B5 from v1
                // Pixel 0: swap v0[0,1,2] -> out[2,1,0]
                // Pixel 1: swap v0[3,4,5] -> out[5,4,3]
                // Pixel 2: swap v0[6,7,8] -> out[8,7,6]
                // Pixel 3: swap v0[9,10,11] -> out[11,10,9]
                // Pixel 4: swap v0[12,13,14] -> out[14,13,12]
                // Position 15: B5 = v1[1] (input byte 17 = v1[1])
                var shuf0FromV0 = Vector128.Create(
                    (byte)2, 1, 0,   // Pixel 0: B,G,R
                    5, 4, 3,         // Pixel 1
                    8, 7, 6,         // Pixel 2
                    11, 10, 9,       // Pixel 3
                    14, 13, 12,      // Pixel 4
                    128              // B5 from v1
                );
                var shuf0FromV1 = Vector128.Create(
                    (byte)128, 128, 128, 128, 128, 128, 128, 128,
                    128, 128, 128, 128, 128, 128, 128, 1  // B5 at position 15
                );

                // Output 1 (bytes 16-31): needs G5, R5, then pixels 6-9 swapped, plus B10, G10 from v2
                // Position 0: G5 = v1[0] (input byte 16)
                // Position 1: R5 = v0[15] (input byte 15)
                // Pixel 6: swap v1[2,3,4] -> out[4,3,2]
                // Pixel 7: swap v1[5,6,7] -> out[7,6,5]
                // Pixel 8: swap v1[8,9,10] -> out[10,9,8]
                // Pixel 9: swap v1[11,12,13] -> out[13,12,11]
                // Position 14: B10 = v2[0] (input byte 32)
                // Position 15: G10 = v1[15] (input byte 31)
                var shuf1FromV0 = Vector128.Create(
                    (byte)128, 15,   // R5 at position 1
                    128, 128, 128,
                    128, 128, 128,
                    128, 128, 128,
                    128, 128, 128,
                    128, 128
                );
                var shuf1FromV1 = Vector128.Create(
                    (byte)0, 128,    // G5 at position 0
                    4, 3, 2,         // Pixel 6
                    7, 6, 5,         // Pixel 7
                    10, 9, 8,        // Pixel 8
                    13, 12, 11,      // Pixel 9
                    128, 15          // G10 at position 15
                );
                var shuf1FromV2 = Vector128.Create(
                    (byte)128, 128, 128, 128, 128, 128, 128, 128,
                    128, 128, 128, 128, 128, 128, 0, 128  // B10 at position 14
                );

                // Output 2 (bytes 32-47): needs R10, then pixels 11-15 swapped
                // Position 0: R10 = v1[14] (input byte 30)
                // Pixel 11: swap v2[1,2,3] -> out[3,2,1]
                // Pixel 12: swap v2[4,5,6] -> out[6,5,4]
                // Pixel 13: swap v2[7,8,9] -> out[9,8,7]
                // Pixel 14: swap v2[10,11,12] -> out[12,11,10]
                // Pixel 15: swap v2[13,14,15] -> out[15,14,13]
                var shuf2FromV1 = Vector128.Create(
                    (byte)14,        // R10 at position 0
                    128, 128, 128,
                    128, 128, 128,
                    128, 128, 128,
                    128, 128, 128,
                    128, 128, 128
                );
                var shuf2FromV2 = Vector128.Create(
                    (byte)128,       // R10 from v1
                    3, 2, 1,         // Pixel 11
                    6, 5, 4,         // Pixel 12
                    9, 8, 7,         // Pixel 13
                    12, 11, 10,      // Pixel 14
                    15, 14, 13       // Pixel 15
                );

                for (; i <= pixelBytes - 48; i += 48)
                {
                    var v0 = Vector128.LoadUnsafe(ref Unsafe.Add(ref srcRef, i));
                    var v1 = Vector128.LoadUnsafe(ref Unsafe.Add(ref srcRef, i + 16));
                    var v2 = Vector128.LoadUnsafe(ref Unsafe.Add(ref srcRef, i + 32));

                    var result0 = Vector128.Shuffle(v0, shuf0FromV0) | Vector128.Shuffle(v1, shuf0FromV1);
                    var result1 = Vector128.Shuffle(v0, shuf1FromV0) | Vector128.Shuffle(v1, shuf1FromV1) | Vector128.Shuffle(v2, shuf1FromV2);
                    var result2 = Vector128.Shuffle(v1, shuf2FromV1) | Vector128.Shuffle(v2, shuf2FromV2);

                    result0.StoreUnsafe(ref Unsafe.Add(ref dstRef, i));
                    result1.StoreUnsafe(ref Unsafe.Add(ref dstRef, i + 16));
                    result2.StoreUnsafe(ref Unsafe.Add(ref dstRef, i + 32));
                }
            }

            // Handle remaining bytes with scalar code
            for (; i < pixelBytes - 2; i += 3)
            {
                Unsafe.Add(ref dstRef, i) = Unsafe.Add(ref srcRef, i + 2);
                Unsafe.Add(ref dstRef, i + 1) = Unsafe.Add(ref srcRef, i + 1);
                Unsafe.Add(ref dstRef, i + 2) = Unsafe.Add(ref srcRef, i);
            }
        }
#endif
    }
}
