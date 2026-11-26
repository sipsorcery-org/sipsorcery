//-----------------------------------------------------------------------------
// Filename: PixelConverterColourUnitTest.cs
//
// Description: Checks that the RGB <-> YUV conversions in PixelConverter use
// ITU-R BT.601 limited range YCbCr, the colour space VP8, H264 and browsers
// use, by converting reference colours and comparing them against the values
// the standard gives.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Nov 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Media.UnitTests
{
    [Trait("Category", "unit")]
    public class PixelConverterColourUnitTest
    {
        /// <summary>
        /// The largest difference, in 8 bit code values, allowed between a conversion and its reference.
        /// </summary>
        private const int TOLERANCE = 2;

        /// <summary>
        /// How far the fixed point conversions may stray from the exact floating point BT.601 result. Rounding
        /// alone accounts for 0.5, the rest leaves room for the error in the 16.16 coefficients.
        /// </summary>
        private const double FIXED_POINT_TOLERANCE = 0.52;

        private const int WIDTH = 8;
        private const int HEIGHT = 8;

        private static readonly VideoPixelFormatsEnum[] PackedRgbFormats =
        {
            VideoPixelFormatsEnum.Bgr,
            VideoPixelFormatsEnum.Bgra,
            VideoPixelFormatsEnum.Rgb,
            VideoPixelFormatsEnum.Rgba
        };

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public PixelConverterColourUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>
        /// Reference colours as 8 bit RGB with their ITU-R BT.601 limited range Y, Cb and Cr values. The bars are
        /// the 100% and 75% colour bars of ITU-R BT.801, where 75% of full scale is 191. Grey 192 is the white of the
        /// TranscodeLoop test card, which the previous full range maths decoded as 181.
        /// </summary>
        public static TheoryData<string, int, int, int, int, int, int> ReferenceColours =>
            new TheoryData<string, int, int, int, int, int, int>
            {
                // Name,        R,   G,   B,    Y,  Cb,  Cr
                { "Black",      0,   0,   0,   16, 128, 128 },
                { "White",    255, 255, 255,  235, 128, 128 },
                { "Grey192",  192, 192, 192,  181, 128, 128 },
                { "White75",  191, 191, 191,  180, 128, 128 },
                { "Yellow75", 191, 191,   0,  162,  44, 142 },
                { "Cyan75",     0, 191, 191,  131, 156,  44 },
                { "Green75",    0, 191,   0,  112,  72,  58 },
                { "Magenta75",191,   0, 191,   84, 184, 198 },
                { "Red75",    191,   0,   0,   65, 100, 212 },
                { "Blue75",     0,   0, 191,   35, 212, 114 },
                { "Yellow",   255, 255,   0,  210,  16, 146 },
                { "Cyan",       0, 255, 255,  170, 166,  16 },
                { "Green",      0, 255,   0,  145,  54,  34 },
                { "Magenta",  255,   0, 255,  106, 202, 222 },
                { "Red",      255,   0,   0,   81,  90, 240 },
                { "Blue",       0,   0, 255,   41, 240, 110 },
            };

        /// <summary>
        /// Checks that each packed RGB format converts a solid reference colour to the BT.601 limited range
        /// Y, Cb and Cr values.
        /// </summary>
        [Theory]
        [MemberData(nameof(ReferenceColours))]
        public void ToI420MatchesBt601ReferenceTest(string name, int r, int g, int b, int y, int u, int v)
        {
            logger.LogDebug("--> {MethodName} {Colour}", TestHelper.GetCurrentMethodName(), name);

            int ySize = WIDTH * HEIGHT;
            int uvSize = ySize / 4;

            foreach (var format in PackedRgbFormats)
            {
                int bytesPerPixel = BytesPerPixel(format);
                int stride = WIDTH * bytesPerPixel;
                byte[] sample = new byte[stride * HEIGHT];

                for (int i = 0; i < WIDTH * HEIGHT; i++)
                {
                    SetPixel(sample, i * bytesPerPixel, format, r, g, b);
                }

                byte[] i420 = PixelConverter.ToI420(WIDTH, HEIGHT, stride, sample, format);

                AssertPlaneWithin(i420, 0, ySize, y, $"{name} {format} Y");
                AssertPlaneWithin(i420, ySize, uvSize, u, $"{name} {format} Cb");
                AssertPlaneWithin(i420, ySize + uvSize, uvSize, v, $"{name} {format} Cr");
            }
        }

        /// <summary>
        /// Checks that I420 and NV12 frames of a solid BT.601 limited range reference colour convert back to the
        /// expected RGB.
        /// </summary>
        [Theory]
        [MemberData(nameof(ReferenceColours))]
        public void I420ToRgbMatchesBt601ReferenceTest(string name, int r, int g, int b, int y, int u, int v)
        {
            logger.LogDebug("--> {MethodName} {Colour}", TestHelper.GetCurrentMethodName(), name);

            int ySize = WIDTH * HEIGHT;
            int uvSize = ySize / 4;
            byte[] i420 = new byte[ySize + 2 * uvSize];
            i420.AsSpan(0, ySize).Fill((byte)y);
            i420.AsSpan(ySize, uvSize).Fill((byte)u);
            i420.AsSpan(ySize + uvSize, uvSize).Fill((byte)v);

            byte[] bgr = PixelConverter.I420toBGR(i420, WIDTH, HEIGHT, out int bgrStride);
            AssertSolidImage(bgr, bgrStride, VideoPixelFormatsEnum.Bgr, r, g, b, $"{name} I420toBGR");

            byte[] rgb = PixelConverter.I420toRGB(i420, WIDTH, HEIGHT, out int rgbStride);
            AssertSolidImage(rgb, rgbStride, VideoPixelFormatsEnum.Rgb, r, g, b, $"{name} I420toRGB");

            byte[] nv12 = PixelConverter.I420toNV12(i420, WIDTH, HEIGHT);
            byte[] nv12Bgr = PixelConverter.NV12toBGR(nv12, WIDTH, HEIGHT, WIDTH * 3);
            AssertSolidImage(nv12Bgr, WIDTH * 3, VideoPixelFormatsEnum.Bgr, r, g, b, $"{name} NV12toBGR");
        }

        /// <summary>
        /// Checks that converting a grid of colours spanning the RGB cube to I420 and back loses no more than the
        /// 8 bit quantisation of limited range YCbCr. Each colour fills a 2x2 block, which shares one chroma sample,
        /// so the chroma subsampling plays no part.
        /// </summary>
        [Theory]
        [InlineData(VideoPixelFormatsEnum.Bgr)]
        [InlineData(VideoPixelFormatsEnum.Bgra)]
        [InlineData(VideoPixelFormatsEnum.Rgb)]
        [InlineData(VideoPixelFormatsEnum.Rgba)]
        public void RoundTripThroughI420IsNearLosslessTest(VideoPixelFormatsEnum format)
        {
            logger.LogDebug("--> {MethodName} {Format}", TestHelper.GetCurrentMethodName(), format);

            var colours = new List<(int R, int G, int B)>();
            for (int r = 0; r <= 255; r += 15)
            {
                for (int g = 0; g <= 255; g += 15)
                {
                    for (int b = 0; b <= 255; b += 15)
                    {
                        colours.Add((r, g, b));
                    }
                }
            }

            byte[] sample = CreateBlockImage(colours, 64, format, out int width, out int height, out int stride);

            byte[] i420 = PixelConverter.ToI420(width, height, stride, sample, format);
            byte[] bgr = PixelConverter.I420toBGR(i420, width, height, out int bgrStride);

            int bytesPerPixel = BytesPerPixel(format);
            int maxError = 0;
            long totalError = 0;

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    var source = GetPixel(sample, row * stride + col * bytesPerPixel, format);
                    var result = GetPixel(bgr, row * bgrStride + col * 3, VideoPixelFormatsEnum.Bgr);

                    foreach (int error in new[] { Math.Abs(source.R - result.R), Math.Abs(source.G - result.G), Math.Abs(source.B - result.B) })
                    {
                        maxError = Math.Max(maxError, error);
                        totalError += error;
                    }
                }
            }

            double meanError = (double)totalError / (width * height * 3);

            logger.LogDebug("{Format} round trip max channel error {MaxError}, mean {MeanError:0.000}.", format, maxError, meanError);

            Assert.True(maxError <= TOLERANCE, $"{format} round trip max channel error was {maxError}, expected at most {TOLERANCE}.");
            Assert.True(meanError < 0.75, $"{format} round trip mean channel error was {meanError:0.000}, expected under 0.75.");
        }

        /// <summary>
        /// Checks the fixed point conversions against the exact floating point BT.601 equations across sweeps of
        /// the RGB cube and of the YCbCr code values. The reference equations are derived here from the standard's
        /// luma weights rather than copied from PixelConverter's coefficient table.
        /// </summary>
        [Fact]
        public void FixedPointMatchesFloatingPointBt601Test()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            const int blocksAcross = 256;

            // RGB to YCbCr.
            var colours = new List<(int R, int G, int B)>();
            for (int r = 0; r <= 255; r += 5)
            {
                for (int g = 0; g <= 255; g += 5)
                {
                    for (int b = 0; b <= 255; b += 5)
                    {
                        colours.Add((r, g, b));
                    }
                }
            }

            byte[] bgr = CreateBlockImage(colours, blocksAcross, VideoPixelFormatsEnum.Bgr, out int width, out int height, out int stride);
            byte[] i420 = PixelConverter.BGRtoI420(bgr, width, height, stride);

            int ySize = width * height;
            int uvWidth = width / 2;
            int uvSize = ySize / 4;
            double maxForwardError = 0;

            for (int i = 0; i < colours.Count; i++)
            {
                int blockRow = i / blocksAcross;
                int blockCol = i % blocksAcross;
                Bt601RgbToYuv(colours[i].R, colours[i].G, colours[i].B, out double y, out double u, out double v);

                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int yPosn = (blockRow * 2 + dy) * width + blockCol * 2 + dx;
                        maxForwardError = Math.Max(maxForwardError, Math.Abs(i420[yPosn] - y));
                    }
                }

                int uvPosn = blockRow * uvWidth + blockCol;
                maxForwardError = Math.Max(maxForwardError, Math.Abs(i420[ySize + uvPosn] - u));
                maxForwardError = Math.Max(maxForwardError, Math.Abs(i420[ySize + uvSize + uvPosn] - v));
            }

            // YCbCr to RGB, including the many code value combinations that fall outside the RGB cube.
            var samples = new List<(int Y, int U, int V)>();
            for (int y = 0; y <= 255; y += 5)
            {
                for (int u = 0; u <= 255; u += 5)
                {
                    for (int v = 0; v <= 255; v += 5)
                    {
                        samples.Add((y, u, v));
                    }
                }
            }

            int blocksDown = (samples.Count + blocksAcross - 1) / blocksAcross;
            width = blocksAcross * 2;
            height = blocksDown * 2;
            ySize = width * height;
            uvWidth = width / 2;
            uvSize = ySize / 4;
            byte[] yuv = new byte[ySize + 2 * uvSize];
            yuv.AsSpan(ySize).Fill(128);

            for (int i = 0; i < samples.Count; i++)
            {
                int blockRow = i / blocksAcross;
                int blockCol = i % blocksAcross;

                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        yuv[(blockRow * 2 + dy) * width + blockCol * 2 + dx] = (byte)samples[i].Y;
                    }
                }

                yuv[ySize + blockRow * uvWidth + blockCol] = (byte)samples[i].U;
                yuv[ySize + uvSize + blockRow * uvWidth + blockCol] = (byte)samples[i].V;
            }

            byte[] rtBgr = PixelConverter.I420toBGR(yuv, width, height, out int rtStride);
            double maxInverseError = 0;

            for (int i = 0; i < samples.Count; i++)
            {
                int blockRow = i / blocksAcross;
                int blockCol = i % blocksAcross;
                Bt601YuvToRgb(samples[i].Y, samples[i].U, samples[i].V, out double r, out double g, out double b);

                for (int dy = 0; dy < 2; dy++)
                {
                    for (int dx = 0; dx < 2; dx++)
                    {
                        var pixel = GetPixel(rtBgr, (blockRow * 2 + dy) * rtStride + (blockCol * 2 + dx) * 3, VideoPixelFormatsEnum.Bgr);
                        maxInverseError = Math.Max(maxInverseError, Math.Abs(pixel.R - r));
                        maxInverseError = Math.Max(maxInverseError, Math.Abs(pixel.G - g));
                        maxInverseError = Math.Max(maxInverseError, Math.Abs(pixel.B - b));
                    }
                }
            }

            logger.LogDebug("Fixed point max error RGB to YUV {ForwardError:0.0000}, YUV to RGB {InverseError:0.0000}.", maxForwardError, maxInverseError);

            Assert.True(maxForwardError <= FIXED_POINT_TOLERANCE, $"RGB to YUV strayed {maxForwardError:0.0000} from the floating point BT.601 result.");
            Assert.True(maxInverseError <= FIXED_POINT_TOLERANCE, $"YUV to RGB strayed {maxInverseError:0.0000} from the floating point BT.601 result.");
        }

        /// <summary>
        /// Exact ITU-R BT.601 RGB to limited range YCbCr, from the luma weights Kr = 0.299 and Kb = 0.114.
        /// </summary>
        private static void Bt601RgbToYuv(int r, int g, int b, out double y, out double u, out double v)
        {
            const double kr = 0.299;
            const double kb = 0.114;
            const double kg = 1 - kr - kb;

            double luma = kr * r + kg * g + kb * b;

            y = 16 + 219.0 / 255 * luma;
            u = 128 + 224.0 / 255 * (b - luma) / (2 * (1 - kb));
            v = 128 + 224.0 / 255 * (r - luma) / (2 * (1 - kr));
        }

        /// <summary>
        /// Exact ITU-R BT.601 limited range YCbCr to RGB, clamped to the RGB cube.
        /// </summary>
        private static void Bt601YuvToRgb(int y, int u, int v, out double r, out double g, out double b)
        {
            const double kr = 0.299;
            const double kb = 0.114;
            const double kg = 1 - kr - kb;

            double luma = (y - 16) * 255.0 / 219;
            double pb = (u - 128) * 255.0 / 224;
            double pr = (v - 128) * 255.0 / 224;

            double rr = luma + 2 * (1 - kr) * pr;
            double bb = luma + 2 * (1 - kb) * pb;
            double gg = (luma - kr * rr - kb * bb) / kg;

            r = Math.Min(255, Math.Max(0, rr));
            g = Math.Min(255, Math.Max(0, gg));
            b = Math.Min(255, Math.Max(0, bb));
        }

        /// <summary>
        /// Lays colours out as 2x2 blocks, left to right then top to bottom, padding the last row with black.
        /// </summary>
        private static byte[] CreateBlockImage(IReadOnlyList<(int R, int G, int B)> colours, int blocksAcross, VideoPixelFormatsEnum format,
            out int width, out int height, out int stride)
        {
            int bytesPerPixel = BytesPerPixel(format);
            int blocksDown = (colours.Count + blocksAcross - 1) / blocksAcross;

            width = blocksAcross * 2;
            height = blocksDown * 2;
            stride = width * bytesPerPixel;

            byte[] buffer = new byte[stride * height];

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int index = (row / 2) * blocksAcross + col / 2;
                    var colour = index < colours.Count ? colours[index] : (R: 0, G: 0, B: 0);
                    SetPixel(buffer, row * stride + col * bytesPerPixel, format, colour.R, colour.G, colour.B);
                }
            }

            return buffer;
        }

        private static int BytesPerPixel(VideoPixelFormatsEnum format)
            => format == VideoPixelFormatsEnum.Bgra || format == VideoPixelFormatsEnum.Rgba ? 4 : 3;

        private static bool IsBlueFirst(VideoPixelFormatsEnum format)
        {
            switch (format)
            {
                case VideoPixelFormatsEnum.Bgr:
                case VideoPixelFormatsEnum.Bgra:
                    return true;
                case VideoPixelFormatsEnum.Rgb:
                case VideoPixelFormatsEnum.Rgba:
                    return false;
                default:
                    throw new ArgumentException($"{format} is not a packed RGB format.", nameof(format));
            }
        }

        private static void SetPixel(byte[] buffer, int offset, VideoPixelFormatsEnum format, int r, int g, int b)
        {
            bool blueFirst = IsBlueFirst(format);

            buffer[offset] = (byte)(blueFirst ? b : r);
            buffer[offset + 1] = (byte)g;
            buffer[offset + 2] = (byte)(blueFirst ? r : b);

            if (BytesPerPixel(format) == 4)
            {
                buffer[offset + 3] = 255;
            }
        }

        private static (int R, int G, int B) GetPixel(byte[] buffer, int offset, VideoPixelFormatsEnum format)
            => IsBlueFirst(format)
                ? (buffer[offset + 2], buffer[offset + 1], buffer[offset])
                : (buffer[offset], buffer[offset + 1], buffer[offset + 2]);

        private static void AssertPlaneWithin(byte[] buffer, int offset, int length, int expected, string description)
        {
            for (int i = offset; i < offset + length; i++)
            {
                Assert.True(Math.Abs(buffer[i] - expected) <= TOLERANCE,
                    $"{description} expected {expected}±{TOLERANCE} but got {buffer[i]} at {i - offset}.");
            }
        }

        private static void AssertSolidImage(byte[] buffer, int stride, VideoPixelFormatsEnum format, int r, int g, int b, string description)
        {
            int bytesPerPixel = BytesPerPixel(format);

            for (int row = 0; row < HEIGHT; row++)
            {
                for (int col = 0; col < WIDTH; col++)
                {
                    var pixel = GetPixel(buffer, row * stride + col * bytesPerPixel, format);

                    Assert.True(Math.Abs(pixel.R - r) <= TOLERANCE && Math.Abs(pixel.G - g) <= TOLERANCE && Math.Abs(pixel.B - b) <= TOLERANCE,
                        $"{description} expected R={r} G={g} B={b} ±{TOLERANCE} but got R={pixel.R} G={pixel.G} B={pixel.B} at ({col},{row}).");
                }
            }
        }
    }
}
