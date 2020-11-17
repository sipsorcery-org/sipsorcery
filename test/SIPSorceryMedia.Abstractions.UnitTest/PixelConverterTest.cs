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

            byte[] buffer = BitmapToBuffer(bmp);

            byte[] i420 = PixelConverter.BGRtoI420(buffer, bmp.Width, bmp.Height);
            byte[] bgr = PixelConverter.I420toBGR(i420, bmp.Width, bmp.Height);

            fixed (byte* s = bgr)
            {
                Bitmap roundTripBmp = new Bitmap(bmp.Width, bmp.Height, (int)bmp.Width * 3, PixelFormat.Format24bppRgb, (IntPtr)s);
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
            byte[] bgr = PixelConverter.I420toBGR(i420, width, height);

            fixed (byte* s = bgr)
            {
                Bitmap bmp = new Bitmap(width,height, width * 3, PixelFormat.Format24bppRgb, (IntPtr)s);
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

            byte[] nv12 = File.ReadAllBytes("img/ref-nv12.yuv");
            byte[] bgr = PixelConverter.NV12toBGR(nv12, width, height);

            fixed (byte* s = bgr)
            {
                Bitmap bmp = new Bitmap(width, height, width * 3, PixelFormat.Format24bppRgb, (IntPtr)s);
                bmp.Save("ConvertKnownNV12ToBGRTest.bmp");
                bmp.Dispose();
            }
        }

        public static byte[] BitmapToBuffer(Bitmap bitmap)
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
    }
}
