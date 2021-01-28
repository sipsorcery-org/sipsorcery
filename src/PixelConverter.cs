using System;

namespace SIPSorceryMedia.Abstractions
{
    public class PixelConverter
    {
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
                    return PixelConverter.RGBAtoI420(sample, width, height, stride);
                case VideoPixelFormatsEnum.Bgr:
                    return PixelConverter.BGRtoI420(sample, width, height, stride);
                case VideoPixelFormatsEnum.Rgb:
                    return PixelConverter.RGBtoI420(sample, width, height, stride);
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
        /// <returns>An I420 buffer representing the source image.</returns>
        /// <remarks>
        /// https://docs.microsoft.com/en-us/previous-versions/visualstudio/hh394035(v=vs.105)
        /// http://qiita.com/gomachan7/items/54d43693f943a0986e95
        /// </remarks>
        public static byte[] RGBAtoI420(byte[] rgba, int width, int height, int stride)
        {
            if (rgba == null || rgba.Length < (stride * height))
            {
                throw new ApplicationException($"RGBA buffer supplied to RGBAtoI420 was too small, expected {stride * height} but got {rgba?.Length}.");
            }

            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;
            int r, g, b, y, u, v;
            //int posn = 0;

            byte[] buffer = new byte[ySize + uvSize];

            for (int row = 0; row < height; row++)
            {
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
            }

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
        /// <returns>An I420 buffer representing the source image.</returns>
        public static byte[] RGBtoI420(byte[] rgb, int width, int height, int stride)
        {
            if (rgb == null || rgb.Length < (stride * height))
            {
                throw new ApplicationException($"RGB buffer supplied to RGBtoI420 was too small, expected {stride * height} but got {rgb?.Length}.");
            }

            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;
            int r, g, b, y, u, v;
            //int posn = 0;

            byte[] buffer = new byte[ySize + uvSize];

            for (int row = 0; row < height; row++)
            {
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
            }

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
        /// <returns>An I420 buffer representing the source image.</returns>
        public static byte[] BGRtoI420(byte[] bgr, int width, int height, int stride)
        {
            if (bgr == null || bgr.Length < (stride * height))
            {
                throw new ApplicationException($"BGR buffer supplied to BGRtoI420 was too small, expected {stride * height} but got {bgr?.Length}.");
            }

            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;
            int r, g, b, y, u, v;
            //int posn = 0;

            byte[] buffer = new byte[ySize + uvSize];

            for (int row = 0; row < height; row++)
            {
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
            }

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
        /// <returns>An RGB buffer representing the source image.</returns>
        public static byte[] I420toRGB(byte[] data, int width, int height, out int stride)
        {
            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            if (data == null || data.Length < (ySize + uvSize))
            {
                throw new ApplicationException($"I420 buffer supplied to I420toRGB was too small, expected {ySize + uvSize} but got {data?.Length}.");
            }

            int uOffset = ySize;
            int vOffset = ySize + ySize / 4;
            stride = (width * 3 + 3) / 4 * 4;
            byte[] rgb = new byte[height * stride];
            //int posn = 0;
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

                    rgb[row * stride + col * 3] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                    rgb[row * stride + col * 3 + 1] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    rgb[row * stride + col * 3 + 2] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                }
            }

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
        /// <returns>A BGR buffer representing the source image.</returns>
        public static byte[] I420toBGR(byte[] data, int width, int height, out int stride)
        {
            int ySize = width * height;
            int uvSize = ((width + 1) / 2) * ((height + 1) / 2) * 2;
            if (data == null || data.Length < (ySize + uvSize))
            {
                throw new ApplicationException($"I420 buffer supplied to I420toBGR was too small, expected {ySize + uvSize} but got {data?.Length}.");
            }

            int uOffset = ySize;
            int vOffset = ySize + uvSize / 2;
            stride = (width * 3 + 3) / 4 * 4;
            byte[] bgr = new byte[height * stride];
            //int posn = 0;
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

                    bgr[row * stride + col * 3] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                    bgr[row * stride + col * 3 + 1] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    bgr[row * stride + col * 3 + 2] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                }
            }

            return bgr;
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
        /// <returns>A BGR buffer representing the source image.</returns>
        public static byte[] NV12toBGR(byte[] data, int width, int height, int stride)
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

                    bgr[row * stride + col * 3] = (byte)(b > 255 ? 255 : b < 0 ? 0 : b);
                    bgr[row * stride + col * 3 + 1] = (byte)(g > 255 ? 255 : g < 0 ? 0 : g);
                    bgr[row * stride + col * 3 + 2] = (byte)(r > 255 ? 255 : r < 0 ? 0 : r);
                }
            }

            return bgr;
        }
    }
}
