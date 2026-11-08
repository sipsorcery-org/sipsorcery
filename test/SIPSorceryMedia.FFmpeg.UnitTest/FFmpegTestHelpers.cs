//-----------------------------------------------------------------------------
// Filename: FFmpegTestHelpers.cs
//
// Description: Shared helpers for the FFmpeg unit tests: locating the native
// FFmpeg libraries, skipping the tests that need them when they are missing,
// and deterministic raw frames to encode.
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
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorceryMedia.FFmpeg.UnitTest
{
    /// <summary>
    /// Finds and loads the native FFmpeg libraries once per test run.
    /// </summary>
    internal static unsafe class FFmpegAvailability
    {
        /// <summary>
        /// Environment variable naming the directory that holds the FFmpeg 8.x shared libraries. When it is not set
        /// the normal discovery, the PATH and an FFmpeg/bin/x64 folder above the working directory, is used.
        /// </summary>
        public const string LIB_PATH_ENV_VAR = "SIPSORCERY_FFMPEG_LIB_PATH";

        private static readonly Lazy<string> _unavailableReason = new Lazy<string>(Probe);

        public static bool IsAvailable => _unavailableReason.Value == null;

        public static string UnavailableReason => _unavailableReason.Value;

        /// <summary>
        /// The reason a test needing the given encoders cannot run, or null if it can.
        /// </summary>
        public static string SkipReasonFor(params string[] encoders)
        {
            if (!IsAvailable)
            {
                return UnavailableReason;
            }

            foreach (var encoder in encoders)
            {
                if (ffmpeg.avcodec_find_encoder_by_name(encoder) == null)
                {
                    return $"The loaded FFmpeg build ({ffmpeg.av_version_info()}) has no {encoder} encoder.";
                }
            }

            return null;
        }

        private static string Probe()
        {
            string libPath = Environment.GetEnvironmentVariable(LIB_PATH_ENV_VAR);

            try
            {
                FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_ERROR, string.IsNullOrWhiteSpace(libPath) ? null : libPath);

                // Make sure a function from each library the encoder uses actually binds.
                _ = ffmpeg.av_version_info();
                _ = ffmpeg.avcodec_version();
                _ = ffmpeg.swscale_version();
                return null;
            }
            catch (Exception excp)
            {
                return $"The FFmpeg 8.x shared libraries (avcodec-62, avutil-60, swscale-9, ...) could not be loaded " +
                    $"from {(string.IsNullOrWhiteSpace(libPath) ? "the PATH or an FFmpeg/bin folder" : libPath)}: " +
                    $"{excp.GetType().Name}: {excp.Message} Set {LIB_PATH_ENV_VAR} to the directory holding them to run this test.";
            }
        }
    }

    /// <summary>
    /// A fact that is skipped, with the reason, when FFmpeg or one of the named encoders is not available.
    /// </summary>
    public sealed class FFmpegFactAttribute : FactAttribute
    {
        public FFmpegFactAttribute(params string[] requiredEncoders)
        {
            string reason = FFmpegAvailability.SkipReasonFor(requiredEncoders);
            if (reason != null)
            {
                Skip = reason;
            }
        }
    }

    /// <summary>
    /// A theory that is skipped, with the reason, when FFmpeg or one of the named encoders is not available.
    /// </summary>
    public sealed class FFmpegTheoryAttribute : TheoryAttribute
    {
        public FFmpegTheoryAttribute(params string[] requiredEncoders)
        {
            string reason = FFmpegAvailability.SkipReasonFor(requiredEncoders);
            if (reason != null)
            {
                Skip = reason;
            }
        }
    }

    internal static class TestFrames
    {
        /// <summary>
        /// A raw frame for frame number <paramref name="index"/>: a gradient that drifts, a block that moves and a
        /// little noise, so encoders produce realistic key and inter frames. Deterministic.
        /// </summary>
        public static byte[] Create(int width, int height, VideoPixelFormatsEnum pixelFormat, int index)
        {
            uint seed = (uint)(0x2545F491 ^ (index * 0x9E3779B1) ^ (width << 12) ^ height);

            switch (pixelFormat)
            {
                case VideoPixelFormatsEnum.I420:
                    {
                        int cw = (width + 1) / 2, ch = (height + 1) / 2;
                        var frame = new byte[width * height + 2 * cw * ch];
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                frame[y * width + x] = (byte)(InBlock(x, y, width, height, index) ? 230 : ((x + 2 * y + 3 * index) & 0xFF) ^ (Next(ref seed) & 0x03));
                            }
                        }
                        for (int i = width * height; i < frame.Length; i++)
                        {
                            frame[i] = (byte)(120 + ((i + index) & 0x0F));
                        }
                        return frame;
                    }
                case VideoPixelFormatsEnum.Bgr:
                case VideoPixelFormatsEnum.Rgb:
                case VideoPixelFormatsEnum.Bgra:
                    {
                        int bpp = pixelFormat == VideoPixelFormatsEnum.Bgra ? 4 : 3;
                        var frame = new byte[width * height * bpp];
                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                int p = (y * width + x) * bpp;
                                bool block = InBlock(x, y, width, height, index);
                                frame[p] = (byte)(block ? 20 : x + index);
                                frame[p + 1] = (byte)(block ? 200 : y * 2);
                                frame[p + 2] = (byte)((x ^ y) + (Next(ref seed) & 0x07));
                                if (bpp == 4)
                                {
                                    frame[p + 3] = 0xFF;
                                }
                            }
                        }
                        return frame;
                    }
                default:
                    throw new NotSupportedException(pixelFormat.ToString());
            }
        }

        private static bool InBlock(int x, int y, int width, int height, int index)
        {
            int bx = (index * 11) % Math.Max(1, width - 32);
            int by = (index * 7) % Math.Max(1, height - 32);
            return x >= bx && x < bx + 32 && y >= by && y < by + 32;
        }

        private static uint Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }
    }

    internal static class EncodedFrames
    {
        /// <summary>
        /// True if an Annex B H264 access unit contains an IDR slice.
        /// </summary>
        public static bool H264HasIdr(byte[] accessUnit)
        {
            foreach (int type in H264NalTypes(accessUnit))
            {
                if (type == 5)
                {
                    return true;
                }
            }
            return false;
        }

        public static IEnumerable<int> H264NalTypes(byte[] accessUnit)
        {
            for (int i = 0; i + 3 < accessUnit.Length; i++)
            {
                if (accessUnit[i] == 0 && accessUnit[i + 1] == 0 && accessUnit[i + 2] == 1)
                {
                    yield return accessUnit[i + 3] & 0x1F;
                    i += 2;
                }
            }
        }

        /// <summary>
        /// True if a VP8 frame is a key frame (RFC 6386 section 9.1, bit 0 of the frame tag is 0).
        /// </summary>
        public static bool Vp8IsKeyFrame(byte[] frame) => frame.Length > 0 && (frame[0] & 0x01) == 0;
    }

    /// <summary>
    /// A stream that supports nothing but writing.
    /// </summary>
    internal sealed class WriteOnlyStream : Stream
    {
        private readonly MemoryStream _written = new MemoryStream();

        public int WriteCalls { get; private set; }

        public byte[] ToArray() => _written.ToArray();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteCalls++;
            _written.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteCalls++;
            _written.Write(buffer);
        }
    }

    /// <summary>
    /// A writable stream whose writes always fail.
    /// </summary>
    internal sealed class FailingStream : Stream
    {
        public int WriteAttempts { get; private set; }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAttempts++;
            throw new IOException("Simulated write failure.");
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            WriteAttempts++;
            throw new IOException("Simulated write failure.");
        }
    }
}
