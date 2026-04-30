//-----------------------------------------------------------------------------
// Filename: PixelConverterTest.cs
//
// Description: Unit tests for the pixel conversion methods.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Nov 2020  Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Xunit;

namespace SIPSorceryMedia.Abstractions.UnitTest
{
    public class PixelConverterTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public PixelConverterTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogger.GetLogger(output).CreateLogger(this.GetType().Name);
        }

        /// <summary>
        /// Tests that a BGR24 bitmap can be round tripped to I420 and back again.
        /// </summary>
        [Fact]
        public unsafe void RoundtripBgr24ToI420Test()
        {
            Bitmap bmp = new Bitmap("img/ref-bgr24.bmp");

            // BGR formats get recognised as RGB but when rendered by WPF are treated as BGR.
            Assert.Equal(PixelFormat.Format24bppRgb, bmp.PixelFormat);

            byte[] buffer = BitmapToBuffer(bmp, out int stride);

            byte[] i420 = PixelConverter.BGRtoI420(buffer, bmp.Width, bmp.Height, stride);
            byte[] bgr = PixelConverter.I420toBGR(i420, bmp.Width, bmp.Height, out int rtStride);

            fixed (byte* s = bgr)
            {
                Bitmap roundTripBmp = new Bitmap(bmp.Width, bmp.Height, rtStride, PixelFormat.Format24bppRgb, (IntPtr)s);
                roundTripBmp.Save("RoundtripBgr24ToI420Test.bmp");
                roundTripBmp.Dispose();
            }

            bmp.Dispose();
        }

        /// <summary>
        /// Tests that a BGRA32 bitmap can be round tripped to I420 and back again.
        /// </summary>
        [Fact]
        public unsafe void RoundtripBgra32ToI420Test()
        {
            Bitmap bmp = new Bitmap("img/ref-bgra32.bmp");

            Assert.Equal(PixelFormat.Format32bppRgb, bmp.PixelFormat);

            byte[] buffer = BitmapToBuffer(bmp, out int stride);

            byte[] i420 = PixelConverter.RGBAtoI420(buffer, bmp.Width, bmp.Height, stride);
            byte[] bgr = PixelConverter.I420toBGR(i420, bmp.Width, bmp.Height, out int rtStride);

            fixed (byte* s = bgr)
            {
                Bitmap roundTripBmp = new Bitmap(bmp.Width, bmp.Height, rtStride, PixelFormat.Format24bppRgb, (IntPtr)s);
                roundTripBmp.Save("RoundtripBgra32ToI420Test.bmp");
                roundTripBmp.Dispose();
            }

            bmp.Dispose();
        }

        /// <summary>
        /// Tests that a known image in I420 (ffmpeg -pix_fmt yuv420p) can be converted to a bitmap.
        /// </summary>
        [Fact]
         public unsafe void ConvertKnownI420ToBGRTest()
        {
            int width = 640;
            int height = 480;

            byte[] i420 = File.ReadAllBytes("img/ref-i420.yuv");
            byte[] bgr = PixelConverter.I420toBGR(i420, width, height, out int rtStride);

            fixed (byte* s = bgr)
            {
                Bitmap bmp = new Bitmap(width,height, rtStride, PixelFormat.Format24bppRgb, (IntPtr)s);
                bmp.Save("ConvertKnownI420ToBGRTest.bmp");
                bmp.Dispose();
            }
        }

        /// <summary>
        /// Tests that a known image in NV12 (ffmpeg -pix_fmt nv12) can be converted to a bitmap.
        /// </summary>
        [Fact]
        public unsafe void ConvertKnownNV12ToBGRTest()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int width = 640;
            int height = 480;
            int stride = width * 3;

            byte[] nv12 = File.ReadAllBytes("img/ref-nv12.yuv");
            byte[] bgr = PixelConverter.NV12toBGR(nv12, width, height, stride);

            fixed (byte* s = bgr)
            {
                Bitmap bmp = new Bitmap(width, height, stride, PixelFormat.Format24bppRgb, (IntPtr)s);
                bmp.Save("ConvertKnownNV12ToBGRTest.bmp");
                bmp.Dispose();
            }
        }
          
        /// <summary>
        /// Tests that an I420 buffer with an odd height (as in height % 2 != 0) can be converted to BGR.
        /// </summary>
        [Fact]
        public unsafe void ConvertOddDimensionI420ToBGRTest()
        {
            int width = 4;
            int height = 3;

            byte[] i420 = new byte[20];
            byte[] bgr = PixelConverter.I420toBGR(i420, width, height, out _);

            Assert.NotNull(bgr);
            Assert.Equal(36, bgr.Length);
        }

        /// <summary>
        /// Tests that an I420 buffer with less than the required data throws an exception.
        /// </summary>
        [Fact]
        public unsafe void WrongSizeI420ToBGRTest()
        {
            int width = 720;
            int height = 405;

            byte[] i420 = new byte[width * height * 3/2];
            Assert.Throws<ApplicationException>(() => PixelConverter.I420toBGR(i420, width, height, out _));
        }

        /// <summary>
        /// Tests that a 480p image can be converted to I420 and back successfully.
        /// </summary>
        [Fact]
        public unsafe void Roundtrip_Bitmap_640x480()
        {
            int width = 640;
            int height = 480;

            using (Bitmap bmp = new Bitmap($"img/testpattern_{width}x{height}.bmp"))
            {
                byte[] bgr = BitmapToBuffer(bmp, out int stride);

                byte[] i420 = PixelConverter.BGRtoI420(bgr, width, height, stride);

                Assert.NotNull(i420);

                byte[] rtBgr = PixelConverter.I420toBGR(i420, width, height, out int rtStride);

                Assert.NotNull(rtBgr);

                fixed (byte* pBgr = rtBgr)
                {
                    Bitmap rtBmp = new Bitmap(width, height, rtStride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(pBgr));
                    rtBmp.Save($"roundtrip_bitmap_{width}x{height}.bmp");
                    rtBmp.Dispose();
                }
            }
        }

        /// <summary>
        /// Tests that an image with an uneven height can be converted to I420 and back successfully.
        /// </summary>
        [Fact]
        public unsafe void Roundtrip_Bitmap_720x405()
        {
            int width = 720;
            int height = 405;

            using (Bitmap bmp = new Bitmap($"img/testpattern_{width}x{height}.bmp"))
            {
                byte[] bgr = BitmapToBuffer(bmp, out int stride);

                byte[] i420 = PixelConverter.BGRtoI420(bgr, width, height, stride);

                Assert.NotNull(i420);

                byte[] rtBgr = PixelConverter.I420toBGR(i420, width, height, out int rtStride);

                Assert.NotNull(rtBgr);
                Assert.Equal(874800, rtBgr.Length);

                fixed (byte* pBgr = rtBgr)
                {
                    Bitmap rtBmp = new Bitmap(width, height, rtStride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(pBgr));
                    rtBmp.Save($"roundtrip_bitmap_{width}x{height}.bmp");
                    rtBmp.Dispose();
                }
            }
        }

        /// <summary>
        /// Tests that an image with an uneven width can be converted to I420 and back successfully.
        /// </summary>
        [Fact]
        public unsafe void Roundtrip_Bitmap_719x404()
        {
            int width = 719;
            int height = 404;

            using (Bitmap bmp = new Bitmap($"img/testpattern_{width}x{height}.bmp"))
            {
                byte[] bgr = BitmapToBuffer(bmp, out int stride);

                byte[] i420 = PixelConverter.BGRtoI420(bgr, width, height, stride);

                Assert.NotNull(i420);

                byte[] rtBgr = PixelConverter.I420toBGR(i420, width, height, out int rtStride);

                Assert.NotNull(rtBgr);

                fixed (byte* pBgr = rtBgr)
                {
                    Bitmap rtBmp = new Bitmap(width, height, rtStride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(pBgr));
                    rtBmp.Save($"roundtrip_bitmap_{width}x{height}.bmp");
                    rtBmp.Dispose();
                }
            }
        }

        /// <summary>
        /// Tests that an image with an uneven width and height can be converted to I420 and back successfully.
        /// </summary>
        [Fact]
        public unsafe void Roundtrip_Bitmap_719x405()
        {
            int width = 719;
            int height = 405;

            using (Bitmap bmp = new Bitmap($"img/testpattern_{width}x{height}.bmp"))
            {
                byte[] bgr = BitmapToBuffer(bmp, out int stride);

                byte[] i420 = PixelConverter.BGRtoI420(bgr, width, height, stride);

                Assert.NotNull(i420);

                byte[] rtBgr = PixelConverter.I420toBGR(i420, width, height, out int rtStride);

                Assert.NotNull(rtBgr);

                fixed (byte* pBgr = rtBgr)
                {
                    Bitmap rtBmp = new Bitmap(width, height, rtStride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(pBgr));
                    rtBmp.Save($"roundtrip_bitmap_{width}x{height}.bmp");
                    rtBmp.Dispose();
                }
            }
        }

        /// <summary>
        /// Tests that a known NV12 image can be converted to I420 and the resulting
        /// image can be rendered to a bitmap.
        /// </summary>
        [Fact]
        public unsafe void ConvertNV12ToI420Test()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int width = 640;
            int height = 480;

            byte[] nv12 = File.ReadAllBytes("img/ref-nv12.yuv");
            byte[] i420 = PixelConverter.NV12toI420(nv12, width, height);

            Assert.NotNull(i420);
            // I420 and NV12 have the same size: Y + UV = width*height + (width/2)*(height/2)*2
            Assert.Equal(nv12.Length, i420.Length);

            // Convert the I420 to BGR for visual verification.
            byte[] bgr = PixelConverter.I420toBGR(i420, width, height, out int stride);

            fixed (byte* s = bgr)
            {
                Bitmap bmp = new Bitmap(width, height, stride, PixelFormat.Format24bppRgb, (IntPtr)s);
                bmp.Save("ConvertNV12ToI420Test.bmp");
                bmp.Dispose();
            }
        }

        /// <summary>
        /// Tests that a known I420 image can be converted to NV12 and the resulting
        /// image can be rendered to a bitmap.
        /// </summary>
        [Fact]
        public unsafe void ConvertI420ToNV12Test()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int width = 640;
            int height = 480;
            int stride = width * 3;

            byte[] i420 = File.ReadAllBytes("img/ref-i420.yuv");
            byte[] nv12 = PixelConverter.I420toNV12(i420, width, height);

            Assert.NotNull(nv12);
            // I420 and NV12 have the same size: Y + UV = width*height + (width/2)*(height/2)*2
            Assert.Equal(i420.Length, nv12.Length);

            // Convert the NV12 to BGR for visual verification.
            byte[] bgr = PixelConverter.NV12toBGR(nv12, width, height, stride);

            fixed (byte* s = bgr)
            {
                Bitmap bmp = new Bitmap(width, height, stride, PixelFormat.Format24bppRgb, (IntPtr)s);
                bmp.Save("ConvertI420ToNV12Test.bmp");
                bmp.Dispose();
            }
        }

        /// <summary>
        /// Tests that an NV12 buffer can be round tripped to I420 and back to NV12.
        /// </summary>
        [Fact]
        public void RoundtripNV12ToI420Test()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int width = 640;
            int height = 480;

            byte[] originalNv12 = File.ReadAllBytes("img/ref-nv12.yuv");
            byte[] i420 = PixelConverter.NV12toI420(originalNv12, width, height);
            byte[] roundtripNv12 = PixelConverter.I420toNV12(i420, width, height);

            Assert.Equal(originalNv12.Length, roundtripNv12.Length);

            // The round-tripped NV12 should be identical to the original.
            for (int i = 0; i < originalNv12.Length; i++)
            {
                Assert.Equal(originalNv12[i], roundtripNv12[i]);
            }
        }

        /// <summary>
        /// Tests that an I420 buffer can be round tripped to NV12 and back to I420.
        /// </summary>
        [Fact]
        public void RoundtripI420ToNV12Test()
        {
            logger.LogDebug("--> " + System.Reflection.MethodBase.GetCurrentMethod().Name);
            logger.BeginScope(System.Reflection.MethodBase.GetCurrentMethod().Name);

            int width = 640;
            int height = 480;

            byte[] originalI420 = File.ReadAllBytes("img/ref-i420.yuv");
            byte[] nv12 = PixelConverter.I420toNV12(originalI420, width, height);
            byte[] roundtripI420 = PixelConverter.NV12toI420(nv12, width, height);

            Assert.Equal(originalI420.Length, roundtripI420.Length);

            // The round-tripped I420 should be identical to the original.
            for (int i = 0; i < originalI420.Length; i++)
            {
                Assert.Equal(originalI420[i], roundtripI420[i]);
            }
        }

        /// <summary>
        /// Tests that NV12 to I420 conversion works with odd dimensions.
        /// </summary>
        [Fact]
        public void ConvertOddDimensionNV12ToI420Test()
        {
            int width = 5;
            int height = 3;
            int ySize = width * height;
            int uvWidth = (width + 1) / 2;
            int uvHeight = (height + 1) / 2;
            int uvSize = uvWidth * uvHeight * 2;

            byte[] nv12 = new byte[ySize + uvSize];
            byte[] i420 = PixelConverter.NV12toI420(nv12, width, height);

            Assert.NotNull(i420);
            Assert.Equal(nv12.Length, i420.Length);
        }

        /// <summary>
        /// Tests that I420 to NV12 conversion works with odd dimensions.
        /// </summary>
        [Fact]
        public void ConvertOddDimensionI420ToNV12Test()
        {
            int width = 5;
            int height = 3;
            int ySize = width * height;
            int uvWidth = (width + 1) / 2;
            int uvHeight = (height + 1) / 2;
            int uvSize = uvWidth * uvHeight * 2;

            byte[] i420 = new byte[ySize + uvSize];
            byte[] nv12 = PixelConverter.I420toNV12(i420, width, height);

            Assert.NotNull(nv12);
            Assert.Equal(i420.Length, nv12.Length);
        }

        /// <summary>
        /// Tests that an NV12 buffer with less than the required data throws an exception.
        /// </summary>
        [Fact]
        public void WrongSizeNV12ToI420Test()
        {
            int width = 720;
            int height = 480;
            int expectedSize = width * height * 3 / 2;

            // Provide a buffer that is too small.
            byte[] nv12 = new byte[expectedSize - 1];
            Assert.Throws<ApplicationException>(() => PixelConverter.NV12toI420(nv12, width, height));
        }

        /// <summary>
        /// Tests that an I420 buffer with less than the required data throws an exception.
        /// </summary>
        [Fact]
        public void WrongSizeI420ToNV12Test()
        {
            int width = 720;
            int height = 480;
            int expectedSize = width * height * 3 / 2;

            // Provide a buffer that is too small.
            byte[] i420 = new byte[expectedSize - 1];
            Assert.Throws<ApplicationException>(() => PixelConverter.I420toNV12(i420, width, height));
        }

        /// <summary>
        /// Tests that a BGR buffer can be converted to RGB and the values are correctly swapped.
        /// </summary>
        [Fact]
        public void BGRtoRGBBasicTest()
        {
            // Create a simple 2x2 BGR image with known values
            int width = 2;
            int height = 2;
            int stride = width * 3;

            // BGR format: B,G,R for each pixel
            byte[] bgr = new byte[]
            {
                // Row 0
                10, 20, 30,  // Pixel (0,0): B=10, G=20, R=30
                40, 50, 60,  // Pixel (1,0): B=40, G=50, R=60
                // Row 1
                70, 80, 90,  // Pixel (0,1): B=70, G=80, R=90
                100, 110, 120 // Pixel (1,1): B=100, G=110, R=120
            };

            byte[] rgb = PixelConverter.BGRtoRGB(bgr, width, height, stride);

            Assert.NotNull(rgb);

            // Verify RGB output (R,G,B for each pixel)
            // Note: stride may be padded, so we need to account for that
            int outputStride = (width * 3 + 3) / 4 * 4;
            
            // Pixel (0,0): should be R=30, G=20, B=10
            Assert.Equal(30, rgb[0]);
            Assert.Equal(20, rgb[1]);
            Assert.Equal(10, rgb[2]);
            
            // Pixel (1,0): should be R=60, G=50, B=40
            Assert.Equal(60, rgb[3]);
            Assert.Equal(50, rgb[4]);
            Assert.Equal(40, rgb[5]);
            
            // Pixel (0,1): should be R=90, G=80, B=70
            Assert.Equal(90, rgb[outputStride]);
            Assert.Equal(80, rgb[outputStride + 1]);
            Assert.Equal(70, rgb[outputStride + 2]);
        }

        /// <summary>
        /// Tests that an RGB buffer can be converted to BGR and the values are correctly swapped.
        /// </summary>
        [Fact]
        public void RGBtoBGRBasicTest()
        {
            // Create a simple 2x2 RGB image with known values
            int width = 2;
            int height = 2;
            int stride = width * 3;

            // RGB format: R,G,B for each pixel
            byte[] rgb = new byte[]
            {
                // Row 0
                30, 20, 10,  // Pixel (0,0): R=30, G=20, B=10
                60, 50, 40,  // Pixel (1,0): R=60, G=50, B=40
                // Row 1
                90, 80, 70,  // Pixel (0,1): R=90, G=80, B=70
                120, 110, 100 // Pixel (1,1): R=120, G=110, B=100
            };

            byte[] bgr = PixelConverter.RGBtoBGR(rgb, width, height, stride);

            Assert.NotNull(bgr);

            // Verify BGR output (B,G,R for each pixel)
            int outputStride = (width * 3 + 3) / 4 * 4;
            
            // Pixel (0,0): should be B=10, G=20, R=30
            Assert.Equal(10, bgr[0]);
            Assert.Equal(20, bgr[1]);
            Assert.Equal(30, bgr[2]);
            
            // Pixel (1,0): should be B=40, G=50, R=60
            Assert.Equal(40, bgr[3]);
            Assert.Equal(50, bgr[4]);
            Assert.Equal(60, bgr[5]);
        }

        /// <summary>
        /// Tests that BGR to RGB conversion can be round-tripped.
        /// </summary>
        [Fact]
        public void RoundtripBGRtoRGBTest()
        {
            int width = 64;
            int height = 48;
            int stride = width * 3;
            byte[] original = new byte[stride * height];

            // Fill with test pattern
            var random = new Random(42);
            random.NextBytes(original);

            byte[] rgb = PixelConverter.BGRtoRGB(original, width, height, stride);
            byte[] roundtrip = PixelConverter.RGBtoBGR(rgb, width, height, (width * 3 + 3) / 4 * 4);

            Assert.NotNull(roundtrip);

            // Since we're using aligned strides, we need to compare pixel by pixel
            int outputStride = (width * 3 + 3) / 4 * 4;
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int srcIdx = row * stride + col * 3;
                    int dstIdx = row * outputStride + col * 3;

                    Assert.Equal(original[srcIdx], roundtrip[dstIdx]);
                    Assert.Equal(original[srcIdx + 1], roundtrip[dstIdx + 1]);
                    Assert.Equal(original[srcIdx + 2], roundtrip[dstIdx + 2]);
                }
            }
        }

        /// <summary>
        /// Tests that BGR to RGB conversion works with odd dimensions.
        /// </summary>
        [Fact]
        public void BGRtoRGBOddDimensionsTest()
        {
            int width = 5;
            int height = 3;
            int stride = width * 3;
            byte[] bgr = new byte[stride * height];

            // Fill with simple pattern
            for (int i = 0; i < bgr.Length; i += 3)
            {
                bgr[i] = (byte)(i % 256);     // B
                bgr[i + 1] = (byte)((i + 1) % 256); // G
                bgr[i + 2] = (byte)((i + 2) % 256); // R
            }

            byte[] rgb = PixelConverter.BGRtoRGB(bgr, width, height, stride);

            Assert.NotNull(rgb);

            // Verify conversion
            int outputStride = (width * 3 + 3) / 4 * 4;
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int srcIdx = row * stride + col * 3;
                    int dstIdx = row * outputStride + col * 3;

                    // RGB should have R and B swapped compared to BGR
                    Assert.Equal(bgr[srcIdx + 2], rgb[dstIdx]);     // R
                    Assert.Equal(bgr[srcIdx + 1], rgb[dstIdx + 1]); // G
                    Assert.Equal(bgr[srcIdx], rgb[dstIdx + 2]);     // B
                }
            }
        }

        /// <summary>
        /// Tests that an exception is thrown when buffer is too small.
        /// </summary>
        [Fact]
        public void BGRtoRGBWrongSizeTest()
        {
            int width = 100;
            int height = 100;
            int stride = width * 3;
            int expectedSize = stride * height;

            // Provide a buffer that is too small
            byte[] bgr = new byte[expectedSize - 1];
            Assert.Throws<ApplicationException>(() => PixelConverter.BGRtoRGB(bgr, width, height, stride));
        }

        /// <summary>
        /// Tests that an exception is thrown when buffer is too small for RGBtoBGR.
        /// </summary>
        [Fact]
        public void RGBtoBGRWrongSizeTest()
        {
            int width = 100;
            int height = 100;
            int stride = width * 3;
            int expectedSize = stride * height;

            // Provide a buffer that is too small
            byte[] rgb = new byte[expectedSize - 1];
            Assert.Throws<ApplicationException>(() => PixelConverter.RGBtoBGR(rgb, width, height, stride));
        }

        /// <summary>
        /// Tests BGR to RGB conversion with a large image to exercise SIMD paths.
        /// </summary>
        [Fact]
        public void BGRtoRGBLargeImageTest()
        {
            int width = 640;
            int height = 480;
            int stride = width * 3;
            byte[] bgr = new byte[stride * height];

            // Fill with gradient pattern
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int idx = row * stride + col * 3;
                    bgr[idx] = (byte)(col % 256);     // B varies with column
                    bgr[idx + 1] = (byte)(row % 256); // G varies with row
                    bgr[idx + 2] = (byte)((row + col) % 256); // R varies with both
                }
            }

            byte[] rgb = PixelConverter.BGRtoRGB(bgr, width, height, stride);

            Assert.NotNull(rgb);
            int outputStride = (width * 3 + 3) / 4 * 4;
            Assert.Equal(outputStride * height, rgb.Length);

            // Verify a sample of pixels
            for (int row = 0; row < height; row += 50)
            {
                for (int col = 0; col < width; col += 50)
                {
                    int srcIdx = row * stride + col * 3;
                    int dstIdx = row * outputStride + col * 3;

                    Assert.Equal(bgr[srcIdx + 2], rgb[dstIdx]);     // R
                    Assert.Equal(bgr[srcIdx + 1], rgb[dstIdx + 1]); // G
                    Assert.Equal(bgr[srcIdx], rgb[dstIdx + 2]);     // B
                }
            }
        }

        private static byte[] BitmapToBuffer(Bitmap bitmap, out int stride)
        {
            BitmapData bmpdata = null;

            try
            {
                bmpdata = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                stride = bmpdata.Stride;
                int numbytes = stride * bitmap.Height;
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
    }
}
