//-----------------------------------------------------------------------------
// Filename: VP8CodecUnitTest.cs
//
// Description: Unit tests for logic in VP8Codec.cs.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 11 Nov 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions.V1;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class VP8CodecUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public VP8CodecUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = TestLogger.GetLogger(output).CreateLogger(this.GetType().Name);
        }

        /// <summary>
        /// Not a real test. Used to double check the existing SIPSorceryMedia VP8 encoder.
        /// </summary>
        //[Fact]
        //public void EncodeTestPattern()
        //{
        //    SIPSorceryMedia.Encoders.VideoEncoder vp8Encoder = new SIPSorceryMedia.Encoders.VideoEncoder();

        //    using (StreamReader sr = new StreamReader("testpattern.i420"))
        //    {
        //        byte[] buffer = new byte[sr.BaseStream.Length];
        //        sr.BaseStream.Read(buffer, 0, buffer.Length);
        //        var encodedSample = vp8Encoder.EncodeVideo(640, 480, buffer, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);

        //        logger.LogDebug(encodedSample.ToHexStr());
        //    }
        //}

        /// <summary>
        /// Not a real test. Used to double check the existing SIPSorceryMedia VP8 encoder.
        /// </summary>
        //[Fact]
        //public void EncodeTestPatternBitmap()
        //{
        //    SIPSorceryMedia.Encoders.VideoEncoder vp8Encoder = new SIPSorceryMedia.Encoders.VideoEncoder();

        //    using(var bmp = new Bitmap("testpattern_32x24.bmp"))
        //    {
        //        var i420 = ImgHelper.BitmapToI420(bmp);
        //        var encodedSample = vp8Encoder.EncodeVideo(bmp.Width, bmp.Height, i420, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);

        //        logger.LogDebug(encodedSample.ToHexStr());
        //    }
        //}

        /// <summary>
        /// Not a real test. Used to double check the existing SIPSorceryMedia VP8 encoder.
        /// </summary>
        //[Fact]
        //public unsafe void DecodeTestPattern()
        //{
        //    SIPSorceryMedia.Encoders.VideoEncoder vp8Encoder = new SIPSorceryMedia.Encoders.VideoEncoder();

        //    string hexKeyFrame = File.ReadAllText("testpattern_keyframe.vp8");
        //    byte[] buffer = HexStr.ParseHexStr(hexKeyFrame.Trim());

        //    var encodedSample = vp8Encoder.DecodeVideo(buffer, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8).First();

        //    fixed (byte* bmpPtr = encodedSample.Sample)
        //    {
        //        Bitmap bmp = new Bitmap((int)encodedSample.Width, (int)encodedSample.Height, (int)encodedSample.Width * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(bmpPtr));
        //        bmp.Save("decodetestpattern.bmp");
        //        bmp.Dispose();
        //    }
        //}

        /// <summary>
        /// Not a real test. Used to double check the existing SIPSorceryMedia VP8 encoder.
        /// </summary>
        //[Fact]
        //public unsafe void DecodeTestPatternHex()
        //{
        //    SIPSorceryMedia.Encoders.VideoEncoder vp8Encoder = new SIPSorceryMedia.Encoders.VideoEncoder();

        //    string hexKeyFrame = "9019009d012a2000180000070885858899848802020275ba24f8de73c58dbdeeeb752712ff80fc8ee701f51cfee1f8e5c007f80ff0dfe73c003fa21e881d603fc07f8e7a287fa3ff25f023fab9fe6bfc4fc00ff1cfe65f3ff800ff46f00fbc5f6f3d5bfdb9cbc7f27fc6dfc88e101fc01f51bfca3f103f29f3817e19fd0ff1d3f243900fa07fe03fc6ff18bf93ed02ff2dfebdfcdff557fa07ba3fecdf8abeb97e10fe9bf8ddf403fc1ff8bff33feaffae5fd73ff9f801fd33f606fd1ff6c52ce5c70fb5b31d19c4d1585982a1d52c92d5044bc6aa90fef98e25c70b5cf745c149e105a557265f8bc910ddd4cb886b7cab7d10d34adb33e89d81e79b23b3a3ff957ee062251d2a350da030f3835bc63663210934f752180ffb727ff1ac46176ff32907dd7e3136e783b35efaa7942bfd44dd8a235af1bffe17985fffecf7417c6a03bfc1a1ff1e474a5479d36a984997847937cf7de46dc9d8424924a7dc90824d92568e635ab5c4cab28adeee56ffca4b7028431c57bf29ffd0701a77d57d889e00cdf4246f94c7b8194e9ad794bf04e08f5e8dfd8e3ba85c9a53b79e07c0a6d522e450d2ba59615f4f32eec7ae529aa1a871fffda4ab9f0eb584bb38392ba87671a35de7973c05c29fff88a95de247f6655a0f2e8797ffd68abf90d359fcde42b78024fce7892f06dd5575f4aa219675afcc85394428ebbbf936ebb3d81f450fab8eef7b81ef5d6227a3b407ffc14c75532c8d63acc8dcdf9b3a1ffedf5b100dab2fd860df7d26843529006b70dacfc8268965c55bf618fc8ff4f04fe10332828dc085ff0aab9895f725562063dda67442d6b9ca8be8c3b70f554050da944adfe1cc2376c6281e4fff013f0f100955110987a750de86d1fb7fe1aba62217c31dda0724eea48372f9e61f8838a080ee4e1bd3233ea3afefabf5cf05f77fe410622f9ef87d3d537ff8a73b22787a00542a940442bfad80c41fb5d46080bba901d21ade640c613c61ad4b15f8a0f91da42ccfa575ee4957adff967140aff4a206acf3c9ab3782d143b9466924de898db1c9cbd5b63736ffc89bda8a44f6f1082f8517a52ad728935e1f0c34927f73600b6dab38ff1e6608ed9b15428092f08bb3e62955bd4bd5513f624fb5ae3618e8dbfeaf992bbc3282ad97653164983f4f2438fad2f7f683b5d6fc6175bb07d3a65ea3483b32fe2125349d3a92c79c011b6c15056ad73bd3620402d301057a904ab755692eb271d2475b6f48acf2538ef6f637d65dfe3f8b70d4603bad4b837def9978d193795afe313bb7ffca3bfcc1aa3dfdf3e325249c59e8b81868f080801ecc7824bb0f0e50ecb3c86ca7e0487fff85bee14ad77c104158879fd1cddd63327ef8fff9b5f84c597dd4723025d87f1dd79bdcd6b7d62625b45f6de1ecb49739363d3ed99fe0fd4d62898af987fc2cda27c6b4bd6816557338d93ddc25632b668fe7fffd70e1027eb39241eb02077844bb7888a09659b1508601742cbdc438ac3bd51130a3fc7caab667259a10914a1743685e196f66df1f4ec0365e69dbab16259d65cb406275c560664079ffd4779362e1f875d3ffe440dd4fe464d64800";
        //    byte[] buffer = HexStr.ParseHexStr(hexKeyFrame.Trim());

        //    var encodedSample = vp8Encoder.DecodeVideo(buffer, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8).First();

        //    fixed (byte* bmpPtr = encodedSample.Sample)
        //    {
        //        Bitmap bmp = new Bitmap((int)encodedSample.Width, (int)encodedSample.Height, (int)encodedSample.Width * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(bmpPtr));
        //        bmp.Save("decodetestpattern_32x24.bmp");
        //        bmp.Dispose();
        //    }
        //}

        /// <summary>
        /// Not a real test. Used to double check the existing SIPSorceryMedia VP8 encoder.
        /// </summary>
        //[Fact]
        //public unsafe void RoundtripTestPatternEncode()
        //{
        //    SIPSorceryMedia.Encoders.VideoEncoder vp8Encoder = new SIPSorceryMedia.Encoders.VideoEncoder();
        //    SIPSorceryMedia.Encoders.VideoEncoder vp8Decoder = new SIPSorceryMedia.Encoders.VideoEncoder();

        //    using (StreamReader sr = new StreamReader("testpattern.i420"))
        //    {
        //        byte[] buffer = new byte[sr.BaseStream.Length];
        //        sr.BaseStream.Read(buffer, 0, buffer.Length);
        //        var encodedSample = vp8Encoder.EncodeVideo(640, 480, buffer, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);

        //        var decodedSample = vp8Decoder.DecodeVideo(encodedSample, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8).First();

        //        fixed (byte* bmpPtr = decodedSample.Sample)
        //        {
        //            Bitmap bmp = new Bitmap((int)decodedSample.Width, (int)decodedSample.Height, (int)decodedSample.Width * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(bmpPtr));
        //            bmp.Save("roundtrip_testpattern.bmp");
        //            bmp.Dispose();
        //        }
        //    }
        //}

        [Fact]
        public unsafe void DecodeKeyFrame()
        {
            string hexKeyFrame = File.ReadAllText("testpattern_keyframe.vp8");
            byte[] buffer = HexStr.ParseHexStr(hexKeyFrame.Trim());

            VP8Codec vp8Codec = new VP8Codec();

            var rgbSamples = vp8Codec.DecodeVideo(buffer, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);

            Assert.Single(rgbSamples);

            var rgbSample = rgbSamples.First();

            fixed (byte* bmpPtr = rgbSample.Sample)
            {
                Bitmap bmp = new Bitmap((int)rgbSample.Width, (int)rgbSample.Height, (int)rgbSample.Width * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, new IntPtr(bmpPtr));
                bmp.Save("decodekeyframe.bmp");
                bmp.Dispose();
            }
        }
    }
}
