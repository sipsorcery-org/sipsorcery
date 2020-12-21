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
        /// Tests that a BGR24 bitmap can be roundtripped to I420 and back again.
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
