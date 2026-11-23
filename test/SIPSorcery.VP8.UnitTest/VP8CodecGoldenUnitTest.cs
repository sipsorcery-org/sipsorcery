//-----------------------------------------------------------------------------
// Filename: VP8CodecGoldenUnitTest.cs
//
// Description: Pins the exact bytes the VP8 encoder produces for a set of
// deterministic frame sequences, so that changes made purely to reduce memory
// allocations (re-used conversion and output buffers, encoding straight from
// the caller's planes, writing to a stream) can be shown not to change a single
// bit of the encoded output.
//
// The expected digests were produced by running this file, unchanged, against
// the encoder as it was before those changes (commit b293c065d). It only uses
// the array returning API that existed then, so it can be re-run against that
// commit to re-derive them.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 06 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class VP8CodecGoldenUnitTest
    {
        private readonly Xunit.Abstractions.ITestOutputHelper _output;

        public VP8CodecGoldenUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// Name, expected SHA-256 of the length prefixed encoded frames.
        /// </summary>
        public static IEnumerable<object[]> GoldenScenarios()
        {
            foreach (var scenario in Vp8GoldenScenario.All)
            {
                yield return new object[] { scenario.Name, scenario.ExpectedDigest };
            }
        }

        /// <summary>
        /// The array returning encoder produces exactly the expected frames.
        /// </summary>
        [Theory]
        [MemberData(nameof(GoldenScenarios))]
        public void EncodeVideo_MatchesGoldenOutput(string scenarioName, string expectedDigest)
        {
            var scenario = Vp8GoldenScenario.Get(scenarioName);

            var frames = scenario.Run((codec, width, height, sample, pixelFormat) =>
                codec.EncodeVideo(width, height, sample, pixelFormat, VideoCodecsEnum.VP8));

            string digest = Vp8GoldenScenario.Digest(frames);
            _output.WriteLine($"{scenarioName}: {frames.Count} frames, {Vp8GoldenScenario.TotalLength(frames)} bytes, digest {digest}");

            Assert.Equal(expectedDigest, digest);
        }
    }

    /// <summary>
    /// A deterministic sequence of raw frames fed to a freshly configured <see cref="VP8Codec"/>.
    /// </summary>
    public sealed class Vp8GoldenScenario
    {
        public delegate byte[] EncodeOne(VP8Codec codec, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat);

        public string Name { get; private set; }
        public string ExpectedDigest { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public VideoPixelFormatsEnum PixelFormat { get; private set; }
        public int FrameCount { get; private set; }
        public int KeyframeInterval { get; private set; } = 30;
        public int BaseQIndex { get; private set; } = 32;
        public bool IntraFallback { get; private set; }
        public int ForceKeyFrameAt { get; private set; } = -1;

        public static readonly Vp8GoldenScenario[] All =
        {
            // One keyframe and then inter frames, default settings.
            new Vp8GoldenScenario { Name = "I420_64x48_Default", Width = 64, Height = 48, PixelFormat = VideoPixelFormatsEnum.I420, FrameCount = 12,
                ExpectedDigest = "C056A33FE9CFC8463026E8850338BD8E7651E7FEDB2279C01180CDC94B101E9E" },
            // Short GOP, intra fallback, a forced key frame part way through and a non default quantizer.
            new Vp8GoldenScenario { Name = "I420_176x144_Gop4_IntraFallback_Forced", Width = 176, Height = 144, PixelFormat = VideoPixelFormatsEnum.I420, FrameCount = 10,
                KeyframeInterval = 4, IntraFallback = true, BaseQIndex = 60, ForceKeyFrameAt = 6,
                ExpectedDigest = "F91A6E284ACB7A51B29D608F9227AD272312EE25957B08B9CD66F7848CF67320" },
            // The quantizer extremes, which give the largest and smallest frames.
            new Vp8GoldenScenario { Name = "I420_96x64_Q0", Width = 96, Height = 64, PixelFormat = VideoPixelFormatsEnum.I420, FrameCount = 4, BaseQIndex = 0,
                ExpectedDigest = "49107CD0BD3563353FAC76BEA67F21ABBF727FDF318C1FE98C01D85B2EC71A42" },
            new Vp8GoldenScenario { Name = "I420_96x64_Q127", Width = 96, Height = 64, PixelFormat = VideoPixelFormatsEnum.I420, FrameCount = 4, BaseQIndex = 127,
                ExpectedDigest = "4512DDBCE05F2508584F5B9B06F2DFD6B2EAE977A179EC23BC7362B7C0D9F038" },
            // Every frame a key frame.
            new Vp8GoldenScenario { Name = "I420_80x64_AllKeyframes", Width = 80, Height = 64, PixelFormat = VideoPixelFormatsEnum.I420, FrameCount = 4, KeyframeInterval = 1,
                ExpectedDigest = "452F8181C7E1A9280FAB84853AEB5892F697B48C1E29C9C2233BBB992E1A1BC2" },
            // Each input format that is converted to I420 before encoding.
            new Vp8GoldenScenario { Name = "Bgr_160x96", Width = 160, Height = 96, PixelFormat = VideoPixelFormatsEnum.Bgr, FrameCount = 5,
                ExpectedDigest = "9826A2C4078BCEE6BCDBC28280BDC104B449C8CE4AB65E7639DC2B00582968B4" },
            new Vp8GoldenScenario { Name = "Bgra_160x96", Width = 160, Height = 96, PixelFormat = VideoPixelFormatsEnum.Bgra, FrameCount = 5,
                ExpectedDigest = "763DC39C2984E8342D67AF362EE2E4F32ADA3A1FE986BE9A4C448ABD9C906DFB" },
            new Vp8GoldenScenario { Name = "Rgb_160x96", Width = 160, Height = 96, PixelFormat = VideoPixelFormatsEnum.Rgb, FrameCount = 5,
                ExpectedDigest = "6FCEC483EBE7AB7AEA785F49E6CF72EF11A792BBFCF4296333686CA316D2E4EB" },
            new Vp8GoldenScenario { Name = "Rgba_160x96", Width = 160, Height = 96, PixelFormat = VideoPixelFormatsEnum.Rgba, FrameCount = 5,
                ExpectedDigest = "7E62E76F6D759A4CBAAE7FBBCC2DAF1E30E380F0AF525CA60EDC908B905B24F9" },
            new Vp8GoldenScenario { Name = "NV12_160x96_IntraFallback", Width = 160, Height = 96, PixelFormat = VideoPixelFormatsEnum.NV12, FrameCount = 5, IntraFallback = true,
                ExpectedDigest = "2E99345AC412D5DD29E598C6F55A8D9F14DBF2D9F7B2F3D1F74CCA87F130F678" },
            // A realistic size, large enough for the output buffer to live on the large object heap.
            new Vp8GoldenScenario { Name = "I420_640x480", Width = 640, Height = 480, PixelFormat = VideoPixelFormatsEnum.I420, FrameCount = 3,
                ExpectedDigest = "07FF8998E8E421297AC355B2E6F360BBE51A75B8A9280EFE4E758329A1185515" },
        };

        /// <summary>
        /// An ad hoc scenario, with default codec settings and no pinned digest, for tests that compare outputs.
        /// </summary>
        public static Vp8GoldenScenario Create(string name, int width, int height, VideoPixelFormatsEnum pixelFormat, int frameCount) =>
            new Vp8GoldenScenario { Name = name, Width = width, Height = height, PixelFormat = pixelFormat, FrameCount = frameCount };

        public static Vp8GoldenScenario Get(string name)
        {
            foreach (var scenario in All)
            {
                if (scenario.Name == name)
                {
                    return scenario;
                }
            }

            throw new ArgumentException($"Unknown scenario {name}.", nameof(name));
        }

        public VP8Codec CreateCodec() => new VP8Codec
        {
            KeyframeIntervalFrames = KeyframeInterval,
            BaseQIndex = BaseQIndex,
            EnableIntraFallback = IntraFallback,
        };

        /// <summary>
        /// Encodes the scenario's frames with a new codec, using <paramref name="encode"/> for each frame.
        /// </summary>
        public List<byte[]> Run(EncodeOne encode)
        {
            var codec = CreateCodec();
            var encoded = new List<byte[]>();

            for (int i = 0; i < FrameCount; i++)
            {
                if (i == ForceKeyFrameAt)
                {
                    codec.ForceKeyFrame();
                }

                encoded.Add(encode(codec, Width, Height, CreateFrame(i), PixelFormat));
            }

            return encoded;
        }

        /// <summary>
        /// Creates frame <paramref name="index"/> of the scenario: a gradient that moves a little each frame, a block
        /// that moves faster and some noise, so key frames, inter frames and the intra fallback decision all have
        /// something to work on.
        /// </summary>
        public byte[] CreateFrame(int index)
        {
            int w = Width, h = Height;
            // The pixel format is part of the seed so each conversion path gets different content.
            uint seed = (uint)(0x9E3779B9 ^ (index * 0x85EBCA6B) ^ (w << 16) ^ h ^ ((int)PixelFormat << 8));

            switch (PixelFormat)
            {
                case VideoPixelFormatsEnum.I420:
                    {
                        var frame = new byte[w * h * 3 / 2];
                        FillLuma(frame, 0, w, h, index, ref seed);
                        FillChroma(frame, w * h, w / 2, h / 2, index, 1, ref seed);
                        FillChroma(frame, w * h + (w / 2) * (h / 2), w / 2, h / 2, index, 1, ref seed);
                        return frame;
                    }
                case VideoPixelFormatsEnum.NV12:
                    {
                        var frame = new byte[w * h * 3 / 2];
                        FillLuma(frame, 0, w, h, index, ref seed);
                        // Interleaved U and V.
                        FillChroma(frame, w * h, w, h / 2, index, 1, ref seed);
                        return frame;
                    }
                default:
                    {
                        int bytesPerPixel = (PixelFormat == VideoPixelFormatsEnum.Bgra || PixelFormat == VideoPixelFormatsEnum.Rgba) ? 4 : 3;
                        var frame = new byte[w * h * bytesPerPixel];
                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                int p = (y * w + x) * bytesPerPixel;
                                bool inBlock = InMovingBlock(x, y, w, h, index);
                                frame[p] = (byte)(inBlock ? 30 : (x * 2 + index * 3));
                                frame[p + 1] = (byte)(inBlock ? 220 : (y * 3 + index));
                                frame[p + 2] = (byte)((x + y) + (Next(ref seed) & 0x0F));
                                if (bytesPerPixel == 4)
                                {
                                    frame[p + 3] = 0xFF;
                                }
                            }
                        }
                        return frame;
                    }
            }
        }

        private static void FillLuma(byte[] frame, int offset, int w, int h, int index, ref uint seed)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int value = InMovingBlock(x, y, w, h, index) ? 235 : (x + y * 2 + index * 4) & 0xFF;
                    value += (int)(Next(ref seed) & 0x07) - 3;
                    frame[offset + y * w + x] = (byte)Math.Max(0, Math.Min(255, value));
                }
            }
        }

        private static void FillChroma(byte[] frame, int offset, int w, int h, int index, int step, ref uint seed)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x += step)
                {
                    frame[offset + y * w + x] = (byte)(128 + ((x - y + index) & 0x1F) - 16 + (int)(Next(ref seed) & 0x03));
                }
            }
        }

        private static bool InMovingBlock(int x, int y, int w, int h, int index)
        {
            int bx = (index * 7) % Math.Max(1, w - 16);
            int by = (index * 5) % Math.Max(1, h - 16);
            return x >= bx && x < bx + 16 && y >= by && y < by + 16;
        }

        private static uint Next(ref uint state)
        {
            // xorshift32
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>
        /// SHA-256 over the frames, each prefixed with its length, as upper case hex.
        /// </summary>
        public static string Digest(IList<byte[]> frames)
        {
            using (var ms = new MemoryStream())
            {
                var lengthPrefix = new byte[4];

                foreach (var frame in frames)
                {
                    int length = frame?.Length ?? -1;
                    lengthPrefix[0] = (byte)(length >> 24);
                    lengthPrefix[1] = (byte)(length >> 16);
                    lengthPrefix[2] = (byte)(length >> 8);
                    lengthPrefix[3] = (byte)length;
                    ms.Write(lengthPrefix, 0, 4);

                    if (frame != null)
                    {
                        ms.Write(frame, 0, frame.Length);
                    }
                }

                using (var sha = SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", string.Empty);
                }
            }
        }

        public static long TotalLength(IList<byte[]> frames)
        {
            long total = 0;
            foreach (var frame in frames)
            {
                total += frame?.Length ?? 0;
            }
            return total;
        }
    }
}
