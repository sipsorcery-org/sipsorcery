//-----------------------------------------------------------------------------
// Filename: H264EncodeTests.cs
//
// Description: The SIPSorcery wire-up of the encoder (E8). H264Codec's access
// units must decode cleanly with the port's decoder, directly and through RTP
// packets and SIPSorcery's H264Depacketiser, start each key frame with the
// parameter sets, follow the key frame interval, size, bitrate and frame rate
// settings, set before the first frame or changed mid-stream, and give the same
// stream from every pixel format of the same picture. P5.7 adds the encoder
// threads: a slice a thread, decoding cleanly and holding the bitrate.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E8).
// 27 Nov 2026  Claude          The EncoderThreads tests (P5.7).
// 27 Nov 2026  Claude          The FrameSkip test.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

public class H264EncodeTests
{
    private const int RtpMaxPayload = 1200;
    private const int NAL_SLICE = 1, NAL_IDR = 5, NAL_SPS = 7, NAL_PPS = 8;

    private readonly ITestOutputHelper _output;

    public H264EncodeTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// A moving test picture as packed RGB: a diagonal gradient, a textured band that scrolls,
    /// and a bright box that moves across, so there is detail and motion to code.
    /// </summary>
    internal static byte[] RgbFrame(int width, int height, int index)
    {
        var rgb = new byte[width * height * 3];
        int boxX = (index * 5) % Math.Max(1, width - width / 4), boxY = height / 3;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 3;
                int r = (x * 255 / width + index * 3) & 0xff;
                int g = (y * 255 / height) & 0xff;
                int b = ((x + y + index * 2) * 2) & 0xff;
                if (y > height * 2 / 3 && ((((x + index * 4) >> 3) ^ (y >> 3)) & 1) != 0)
                {
                    r = g = b = 40;
                }
                if (x >= boxX && x < boxX + width / 4 && y >= boxY && y < boxY + height / 4)
                {
                    r = 250; g = 240; b = 60;
                }
                rgb[o] = (byte)r;
                rgb[o + 1] = (byte)g;
                rgb[o + 2] = (byte)b;
            }
        }
        return rgb;
    }

    internal static unsafe byte[] ToI420(byte[] rgb, int width, int height)
    {
        int cw = (width + 1) / 2, ch = (height + 1) / 2;
        var i420 = new byte[width * height + 2 * cw * ch];
        fixed (byte* s = rgb)
        fixed (byte* d = i420)
        {
            RgbToI420.ConvertRgb(s, width * 3, VideoPixelFormatsEnum.Rgb, width, height, d, width, d + width * height,
                                 d + width * height + cw * ch, cw);
        }
        return i420;
    }

    internal static byte[] I420Frame(int width, int height, int index) => ToI420(RgbFrame(width, height, index), width, height);

    private static List<int> NalTypes(byte[] au) => AccessUnits.FindNals(au).Select(n => au[n.Offset] & 0x1f).ToList();

    private static bool IsIdr(byte[] au) => NalTypes(au).Contains(NAL_IDR);

    private static double PsnrY(byte[] a, byte[] b, int width, int height)
    {
        double sse = 0;
        for (int i = 0; i < width * height; i++)
        {
            int d = a[i] - b[i];
            sse += d * d;
        }
        return sse == 0 ? 99 : 10 * Math.Log10(255.0 * 255 * width * height / sse);
    }

    // The luma of an I420 frame cropped to the even size the encoder codes.
    private static byte[] Luma(byte[] i420, int width, int height, int codedWidth, int codedHeight)
    {
        var y = new byte[codedWidth * codedHeight];
        for (int r = 0; r < codedHeight; r++)
        {
            Array.Copy(i420, r * width, y, r * codedWidth, codedWidth);
        }
        return y;
    }

    [Theory]
    [InlineData(176, 144)]
    [InlineData(320, 240)]
    [InlineData(640, 360)]
    [InlineData(100, 50)]
    public void Encode_RoundTripsThroughTheDecoder(int width, int height)
    {
        using var encoder = new H264Codec();
        using var decoder = new H264Codec();
        int frames = 60, encoded = 0;
        double psnrSum = 0;

        for (int i = 0; i < frames; i++)
        {
            byte[] src = I420Frame(width, height, i);
            byte[] au = encoder.EncodeVideo(width, height, src, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            if (i == 0)
            {
                Assert.NotNull(au);
            }
            if (au == null)
            {
                continue;
            }
            ++encoded;

            VideoSample pic = Assert.Single(decoder.DecodeVideo(au, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
            Assert.False(decoder.KeyFrameRequired, $"Frame {i} did not decode cleanly.");
            Assert.Equal(((uint)width, (uint)height), (pic.Width, pic.Height));
            psnrSum += PsnrY(Luma(src, width, height, width, height), pic.Sample, width, height);
        }

        double psnr = psnrSum / encoded;
        _output.WriteLine($"{width}x{height}: {encoded} of {frames} frames, average luma PSNR {psnr:F2} dB.");
        Assert.True(encoded >= frames * 9 / 10, $"Only {encoded} of {frames} frames were encoded.");
        Assert.True(psnr > 30, $"Average luma PSNR {psnr:F2} dB.");
    }

    // The slice NAL units of an access unit.
    private static int SliceCount(byte[] au) => NalTypes(au).Count(t => t is NAL_SLICE or NAL_IDR);

    /// <summary>
    /// More encoder threads code a slice each, and the stream still decodes cleanly to much the
    /// same picture at much the same bitrate as one thread's. A small frame gets fewer slices, as
    /// OpenH264 sets them under rate control: one for 48 macroblocks or fewer, and otherwise
    /// no more than its rows allow a row group (GOM) each, brought down to an even count. 640x32
    /// would be refused more than one, so it is one slice rather than an error.
    /// </summary>
    [Theory]
    [InlineData(640, 360, 2, 2)]
    [InlineData(640, 360, 3, 3)]
    [InlineData(640, 360, 4, 4)]
    [InlineData(176, 144, 4, 4)]
    [InlineData(144, 96, 4, 2)]
    [InlineData(128, 96, 4, 1)]
    [InlineData(100, 50, 3, 1)]
    [InlineData(640, 32, 4, 1)]
    [InlineData(16, 16, 4, 1)]
    public void Encode_ThreadsCodeASliceEach(int width, int height, int threads, int slices)
    {
        using var single = new H264Codec();
        using var encoder = new H264Codec { EncoderThreads = threads };
        using var decoder = new H264Codec();
        int frames = 60, encoded = 0, singleEncoded = 0;
        long bytes = 0, singleBytes = 0;
        double psnrSum = 0;
        var sliceCounts = new SortedSet<int>();

        for (int i = 0; i < frames; i++)
        {
            byte[] src = I420Frame(width, height, i);
            byte[] au = encoder.EncodeVideo(width, height, src, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            byte[] singleAu = single.EncodeVideo(width, height, src, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            if (i == 0)
            {
                Assert.NotNull(au);
                Assert.True(IsIdr(au));
            }
            if (singleAu != null)
            {
                ++singleEncoded;
                singleBytes += singleAu.Length;
                Assert.Equal(1, SliceCount(singleAu));
            }
            if (au == null)
            {
                continue;
            }
            ++encoded;
            bytes += au.Length;
            sliceCounts.Add(SliceCount(au));

            VideoSample pic = Assert.Single(decoder.DecodeVideo(au, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
            Assert.False(decoder.KeyFrameRequired, $"Frame {i} did not decode cleanly.");
            Assert.Equal(((uint)width, (uint)height), (pic.Width, pic.Height));
            psnrSum += PsnrY(Luma(src, width, height, width, height), pic.Sample, width, height);
        }

        double psnr = psnrSum / encoded;
        _output.WriteLine($"{width}x{height} on {threads} threads: {encoded} of {frames} frames ({singleEncoded} on one), " +
                          $"{bytes} bytes ({singleBytes} on one), {string.Join("/", sliceCounts)} slices, average luma PSNR {psnr:F2} dB.");
        Assert.Equal(new[] { slices }, sliceCounts);
        // Each slice's header, and prediction that stops at its edges, take bits the rate control
        // makes up for: four slices of 640x360 drop a few more frames than one.
        Assert.True(encoded >= singleEncoded - frames / 10, $"Only {encoded} of {frames} frames were encoded, {singleEncoded} on one thread.");
        Assert.InRange(bytes, singleBytes * 0.85, singleBytes * 1.15);
        Assert.True(psnr > 30, $"Average luma PSNR {psnr:F2} dB.");
    }

    /// <summary>
    /// A thread count set mid-stream starts a new stream with a key frame, the decoder follows
    /// it, and setting the count it already has changes nothing.
    /// </summary>
    [Fact]
    public void Encode_EncoderThreadsChangeStartsWithAKeyFrame()
    {
        const int width = 320, height = 240;
        using var encoder = new H264Codec();
        using var decoder = new H264Codec();

        foreach (var (threads, i) in new[] { (1, 0), (1, 1), (3, 2), (3, 3), (3, 4), (1, 5), (1, 6), (4, 7), (2, 8) })
        {
            encoder.EncoderThreads = threads;
            Assert.Equal(threads, encoder.EncoderThreads);
            byte[] au = encoder.EncodeVideo(width, height, I420Frame(width, height, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            bool newStream = i is 0 or 2 or 5 or 7 or 8;
            if (au == null && !newStream)
            {
                // The rate control dropped a frame after a key frame.
                continue;
            }
            Assert.Equal(newStream, IsIdr(au));
            Assert.Equal(threads, SliceCount(au));
            VideoSample pic = Assert.Single(decoder.DecodeVideo(au, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
            Assert.False(decoder.KeyFrameRequired, $"Frame {i} did not decode cleanly.");
        }
    }

    /// <summary>OpenH264 codes the even size below an odd one, losing the last column and row.</summary>
    [Fact]
    public void Encode_OddSizeLosesTheLastColumnAndRow()
    {
        const int width = 101, height = 51;
        using var encoder = new H264Codec();
        using var decoder = new H264Codec();

        byte[] src = I420Frame(width, height, 0);
        byte[] au = encoder.EncodeVideo(width, height, src, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
        VideoSample pic = Assert.Single(decoder.DecodeVideo(au, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        Assert.False(decoder.KeyFrameRequired);
        Assert.Equal((100u, 50u), (pic.Width, pic.Height));
        Assert.True(PsnrY(Luma(src, width, height, 100, 50), pic.Sample, 100, 50) > 30);
    }

    /// <summary>
    /// Every IDR access unit starts with the SPS and PPS, which signal Constrained Baseline;
    /// the others hold P slices alone. Key frames come at the interval and when forced, and the interval counts from the last one.
    /// </summary>
    [Fact]
    public void Encode_KeyFramesFollowTheIntervalAndCarryTheParameterSets()
    {
        const int width = 320, height = 240;
        using var encoder = new H264Codec { KeyframeIntervalFrames = 10, TargetBitrateKbps = 2000 };
        var idrs = new List<int>();
        byte[] parameterSetsSeen = null;

        for (int i = 0; i < 40; i++)
        {
            if (i == 15)
            {
                encoder.ForceKeyFrame();
            }
            byte[] au = encoder.EncodeVideo(width, height, I420Frame(width, height, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            Assert.NotNull(au);
            List<int> types = NalTypes(au);
            if (types.Contains(NAL_IDR))
            {
                idrs.Add(i);
                Assert.Equal(new[] { NAL_SPS, NAL_PPS }, types.Take(2));
                Assert.All(types.Skip(2), t => Assert.Equal(NAL_IDR, t));

                var nals = AccessUnits.FindNals(au);
                Assert.Equal(66, au[nals[0].Offset + 1]);
                // constraint_set0 and constraint_set1: Constrained Baseline, as 42e01f offers.
                Assert.Equal(0xc0, au[nals[0].Offset + 2] & 0xc0);
                // The same parameter sets, ids included, at every IDR.
                byte[] parameterSets = au.AsSpan(0, nals[2].StartCode).ToArray();
                parameterSetsSeen ??= parameterSets;
                Assert.True(parameterSetsSeen.AsSpan().SequenceEqual(parameterSets), $"Frame {i} has different parameter sets.");
            }
            else
            {
                Assert.All(types, t => Assert.Equal(NAL_SLICE, t));
            }
        }

        _output.WriteLine($"IDR frames: {string.Join(", ", idrs)}");
        // A forced key frame starts the interval again.
        Assert.Equal(new[] { 0, 10, 15, 25, 35 }, idrs);
    }

    [Fact]
    public void Encode_KeyFrameIntervalZeroSendsOnlyTheFirstUnlessForced()
    {
        const int width = 176, height = 144;
        using var encoder = new H264Codec { KeyframeIntervalFrames = 0 };
        var idrs = new List<int>();

        for (int i = 0; i < 70; i++)
        {
            if (i == 50)
            {
                encoder.ForceKeyFrame();
            }
            byte[] au = encoder.EncodeVideo(width, height, I420Frame(width, height, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            if (au != null && IsIdr(au))
            {
                idrs.Add(i);
            }
        }

        Assert.Equal(new[] { 0, 50 }, idrs);
    }

    /// <summary>
    /// A new interval set mid-stream applies from the next frame, counted from the last key
    /// frame, and 0 then stops them.
    /// </summary>
    [Fact]
    public void Encode_KeyFrameIntervalChangeAppliesFromTheNextFrame()
    {
        const int width = 176, height = 144;
        using var encoder = new H264Codec { KeyframeIntervalFrames = 30, TargetBitrateKbps = 1000 };
        var idrs = new List<int>();

        for (int i = 0; i < 45; i++)
        {
            if (i == 12)
            {
                // 12 frames since the last key frame, so the next is one.
                encoder.KeyframeIntervalFrames = 5;
            }
            else if (i == 24)
            {
                encoder.KeyframeIntervalFrames = 0;
            }
            byte[] au = encoder.EncodeVideo(width, height, I420Frame(width, height, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            Assert.NotNull(au);
            if (IsIdr(au))
            {
                idrs.Add(i);
            }
        }

        _output.WriteLine($"IDR frames: {string.Join(", ", idrs)}");
        Assert.Equal(new[] { 0, 12, 17, 22 }, idrs);
    }

    /// <summary>A new frame size starts a new stream, with a key frame the decoder follows.</summary>
    [Fact]
    public void Encode_SizeChangeStartsWithAKeyFrame()
    {
        using var encoder = new H264Codec();
        using var decoder = new H264Codec();

        foreach (var (w, h, i) in new[] { (320, 240, 0), (320, 240, 1), (176, 144, 2), (176, 144, 3), (320, 240, 4) })
        {
            byte[] au = encoder.EncodeVideo(w, h, I420Frame(w, h, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            Assert.Equal(i is 0 or 2 or 4, IsIdr(au));
            VideoSample pic = Assert.Single(decoder.DecodeVideo(au, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
            Assert.False(decoder.KeyFrameRequired);
            Assert.Equal(((uint)w, (uint)h), (pic.Width, pic.Height));
        }
    }

    /// <summary>
    /// The same picture in every pixel format, packed or with padded rows, through every encode
    /// method, gives the same stream as its I420 conversion.
    /// </summary>
    [Fact]
    public unsafe void Encode_EveryPixelFormatAndMethodGivesTheSameStream()
    {
        const int width = 176, height = 144, frames = 8;
        var rgb = Enumerable.Range(0, frames).Select(i => RgbFrame(width, height, i)).ToList();

        List<byte[]> Stream(Func<H264Codec, int, byte[]> encode)
        {
            using var codec = new H264Codec();
            return Enumerable.Range(0, frames).Select(i => encode(codec, i)).ToList();
        }

        var reference = Stream((c, i) => c.EncodeVideo(width, height, ToI420(rgb[i], width, height), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        Assert.All(reference, Assert.NotNull);

        var streams = new Dictionary<string, List<byte[]>>();
        foreach (var format in new[] { VideoPixelFormatsEnum.Rgb, VideoPixelFormatsEnum.Bgr, VideoPixelFormatsEnum.Rgba, VideoPixelFormatsEnum.Bgra })
        {
            streams[format.ToString()] = Stream((c, i) => c.EncodeVideo(width, height, Packed(rgb[i], format, width, height, 0), format, VideoCodecsEnum.H264));
            // Rows padded with junk, through the buffer writer.
            streams[format + " strided"] = Stream((c, i) =>
            {
                int stride = RgbToI420.PackedStride(format, width) + 20;
                var writer = new ArrayBufferWriter<byte>();
                int length = c.EncodeVideo(width, height, Packed(rgb[i], format, width, height, 20), stride, format, writer);
                Assert.Equal(writer.WrittenCount, length);
                return writer.WrittenSpan.ToArray();
            });
            // From native memory, through EncodeVideoFaster.
            streams[format + " raw"] = Stream((c, i) =>
            {
                byte[] packed = Packed(rgb[i], format, width, height, 8);
                fixed (byte* p = packed)
                {
                    var image = new RawImage { Width = width, Height = height, Stride = RgbToI420.PackedStride(format, width) + 8, Sample = (IntPtr)p, PixelFormat = format };
                    return c.EncodeVideoFaster(image, VideoCodecsEnum.H264);
                }
            });
        }

        streams["I420 strided"] = Stream((c, i) =>
        {
            const int stride = width + 16;
            byte[] frame = Strided(ToI420(rgb[i], width, height), width, height, stride);
            var writer = new ArrayBufferWriter<byte>();
            c.EncodeVideo(width, height, frame, stride, VideoPixelFormatsEnum.I420, writer);
            return writer.WrittenSpan.ToArray();
        });
        streams["NV12"] = Stream((c, i) => c.EncodeVideo(width, height, ToNv12(ToI420(rgb[i], width, height), width, height), VideoPixelFormatsEnum.NV12, VideoCodecsEnum.H264));

        foreach (var (name, stream) in streams)
        {
            for (int i = 0; i < frames; i++)
            {
                Assert.True(reference[i].AsSpan().SequenceEqual(stream[i]), $"{name} frame {i} differs from the I420 stream.");
            }
        }
    }

    // The RGB picture in a packed format, each row followed by `pad` junk bytes.
    private static byte[] Packed(byte[] rgb, VideoPixelFormatsEnum format, int width, int height, int pad)
    {
        int bpp = format is VideoPixelFormatsEnum.Rgba or VideoPixelFormatsEnum.Bgra ? 4 : 3;
        bool bgr = format is VideoPixelFormatsEnum.Bgr or VideoPixelFormatsEnum.Bgra;
        int stride = width * bpp + pad;
        var dst = new byte[stride * height];
        new Random(1).NextBytes(dst);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int s = (y * width + x) * 3, d = y * stride + x * bpp;
                dst[d + (bgr ? 2 : 0)] = rgb[s];
                dst[d + 1] = rgb[s + 1];
                dst[d + (bgr ? 0 : 2)] = rgb[s + 2];
                if (bpp == 4)
                {
                    dst[d + 3] = 0x5a;
                }
            }
        }
        return dst;
    }

    // An I420 frame with luma rows of `stride` and chroma rows of half that, rounded up.
    private static byte[] Strided(byte[] i420, int width, int height, int stride)
    {
        int cw = (width + 1) / 2, ch = (height + 1) / 2, cs = (stride + 1) / 2;
        var dst = new byte[stride * height + 2 * cs * ch];
        new Random(2).NextBytes(dst);
        for (int y = 0; y < height; y++)
        {
            Array.Copy(i420, y * width, dst, y * stride, width);
        }
        for (int p = 0; p < 2; p++)
        {
            for (int y = 0; y < ch; y++)
            {
                Array.Copy(i420, width * height + p * cw * ch + y * cw, dst, stride * height + p * cs * ch + y * cs, cw);
            }
        }
        return dst;
    }

    private static byte[] ToNv12(byte[] i420, int width, int height)
    {
        int cw = (width + 1) / 2, ch = (height + 1) / 2;
        var nv12 = new byte[width * height + 2 * cw * ch];
        Array.Copy(i420, nv12, width * height);
        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                nv12[width * height + y * width + 2 * x] = i420[width * height + y * cw + x];
                nv12[width * height + y * width + 2 * x + 1] = i420[width * height + cw * ch + y * cw + x];
            }
        }
        return nv12;
    }

    /// <summary>libyuv's BT.601 limited range values for the primaries, white and black.</summary>
    [Theory]
    [InlineData(255, 255, 255, 235, 128, 128)]
    [InlineData(0, 0, 0, 16, 128, 128)]
    [InlineData(255, 0, 0, 82, 90, 240)]
    [InlineData(0, 255, 0, 144, 54, 34)]
    [InlineData(0, 0, 255, 41, 240, 110)]
    public unsafe void RgbConversion_MatchesLibyuv(byte r, byte g, byte b, byte y, byte u, byte v)
    {
        var rgb = new byte[] { r, g, b, r, g, b, r, g, b, r, g, b };
        var dst = new byte[6];
        fixed (byte* s = rgb)
        fixed (byte* d = dst)
        {
            RgbToI420.ConvertRgb(s, 6, VideoPixelFormatsEnum.Rgb, 2, 2, d, 2, d + 4, d + 5, 1);
        }
        Assert.Equal(new[] { y, y, y, y, u, v }, dst);
    }

    /// <summary>RGB in, RGB out: the encoder's conversion is the one the decoder undoes.</summary>
    [Fact]
    public void Encode_RgbComesBackAsTheSameColours()
    {
        const int width = 320, height = 240;
        using var encoder = new H264Codec { TargetBitrateKbps = 4000 };
        using var decoder = new H264Codec();
        byte[] rgb = RgbFrame(width, height, 0);

        byte[] au = encoder.EncodeVideo(width, height, rgb, VideoPixelFormatsEnum.Rgb, VideoCodecsEnum.H264);
        VideoSample pic = Assert.Single(decoder.DecodeVideo(au, VideoPixelFormatsEnum.Rgb, VideoCodecsEnum.H264));
        Assert.Equal(rgb.Length, pic.Sample.Length);

        double error = 0;
        for (int i = 0; i < rgb.Length; i++)
        {
            error += Math.Abs(rgb[i] - pic.Sample[i]);
        }
        error /= rgb.Length;
        _output.WriteLine($"Mean absolute RGB error {error:F2}.");
        Assert.True(error < 4, $"Mean absolute RGB error {error:F2}.");
    }

    /// <summary>
    /// The rate control holds the target over a few seconds of video, and a lower target set
    /// mid-stream brings the rate down.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Encode_HoldsTheTargetBitrate(int threads)
    {
        const int width = 640, height = 480, fps = 30;
        using var encoder = new H264Codec { TargetBitrateKbps = 800, FrameRate = fps, KeyframeIntervalFrames = 60, EncoderThreads = threads };

        long Bytes(int from, int count)
        {
            long total = 0;
            for (int i = from; i < from + count; i++)
            {
                total += encoder.EncodeVideo(width, height, I420Frame(width, height, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264)?.Length ?? 0;
            }
            return total;
        }

        double first = Bytes(0, 120) * 8.0 / (120.0 / fps) / 1000;
        encoder.TargetBitrateKbps = 300;
        Bytes(120, 30);
        double second = Bytes(150, 120) * 8.0 / (120.0 / fps) / 1000;
        _output.WriteLine($"{threads} thread(s), 800 kbps target: {first:F0} kbps; 300 kbps target: {second:F0} kbps.");

        Assert.InRange(first, 800 * 0.6, 800 * 1.3);
        Assert.InRange(second, 300 * 0.6, 300 * 1.3);
    }

    /// <summary>
    /// <see cref="H264Codec.FrameSkip"/>, on by default, lets the rate control skip frames to
    /// hold a bitrate too low for the picture, giving no output for them. Off, every frame is
    /// coded and the bitrate runs over instead, whether it is turned off before the first frame
    /// or mid-stream.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Encode_FrameSkipOffCodesEveryFrame(bool offFromTheStart)
    {
        const int width = 640, height = 480;
        using var encoder = new H264Codec { TargetBitrateKbps = 100, KeyframeIntervalFrames = 60 };
        Assert.True(encoder.FrameSkip);

        int Skipped(int from, int count)
        {
            int skipped = 0;
            for (int i = from; i < from + count; i++)
            {
                var au = encoder.EncodeVideo(width, height, I420Frame(width, height, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
                if (au == null || au.Length == 0)
                {
                    skipped++;
                }
            }
            return skipped;
        }

        if (offFromTheStart)
        {
            encoder.FrameSkip = false;
            int skipped = Skipped(0, 90);
            _output.WriteLine($"Off from the start: {skipped} of 90 frames skipped.");
            Assert.Equal(0, skipped);
        }
        else
        {
            int skippedOn = Skipped(0, 90);
            encoder.FrameSkip = false;
            Assert.False(encoder.FrameSkip);
            int skippedOff = Skipped(90, 90);
            _output.WriteLine($"On: {skippedOn} of 90 frames skipped; then off: {skippedOff} of 90.");
            Assert.True(skippedOn > 0, "At 100 kbps the rate control should skip frames.");
            Assert.Equal(0, skippedOff);
        }
    }

    /// <summary>
    /// A frame rate set mid-stream shares the bitrate out over the new rate: a set target is
    /// held at the new rate, and the automatic one (0.1 bits a pixel) follows it down.
    /// </summary>
    [Theory]
    [InlineData(500)]
    [InlineData(0)]
    public void Encode_FrameRateChangeSharesTheBitrateOutAgain(int targetKbps)
    {
        const int width = 640, height = 360;
        using var encoder = new H264Codec { TargetBitrateKbps = targetKbps, FrameRate = 30, KeyframeIntervalFrames = 60 };

        double Kbps(int from, int count, int fps)
        {
            long total = 0;
            for (int i = from; i < from + count; i++)
            {
                total += encoder.EncodeVideo(width, height, I420Frame(width, height, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264)?.Length ?? 0;
            }
            return total * 8.0 / ((double)count / fps) / 1000;
        }
        double Expected(int fps) => targetKbps > 0 ? targetKbps : width * height * fps * 0.1 / 1000;

        double first = Kbps(0, 90, 30);
        encoder.FrameRate = 10;
        Kbps(90, 30, 10);
        double second = Kbps(120, 90, 10);
        _output.WriteLine($"30 fps: {first:F0} kbps for {Expected(30):F0}; 10 fps: {second:F0} kbps for {Expected(10):F0}.");

        Assert.InRange(first, Expected(30) * 0.6, Expected(30) * 1.3);
        Assert.InRange(second, Expected(10) * 0.6, Expected(10) * 1.3);
    }

    /// <summary>
    /// Through RTP: each access unit packetised as SIPSorcery sends it (STAP-A for the parameter
    /// sets, FU-A for slices over the payload limit) and put back together by H264Depacketiser.
    /// On four threads an access unit is four slices.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void Encode_RtpRoundTripDecodesCleanly(int threads)
    {
        const int width = 640, height = 360;
        using var encoder = new H264Codec { TargetBitrateKbps = 1500, EncoderThreads = threads };
        using var direct = new H264Codec();
        using var viaRtp = new H264Codec();
        var depacketiser = new H264Depacketiser();
        ushort seq = 65530;
        uint timestamp = 0;
        int fragmented = 0;

        for (int i = 0; i < 30; i++)
        {
            byte[] au = encoder.EncodeVideo(width, height, I420Frame(width, height, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264);
            if (au == null)
            {
                continue;
            }

            var packets = AccessUnits.Packetise(au, RtpMaxPayload);
            fragmented += packets.Any(p => (p.Payload[0] & 0x1f) == 28) ? 1 : 0;
            MemoryStream assembled = null;
            foreach (var (payload, marker) in packets)
            {
                assembled = depacketiser.ProcessRTPPayload(payload, seq++, timestamp, marker ? 1 : 0, out _);
            }
            timestamp += 3000;
            Assert.NotNull(assembled);

            byte[] a = Assert.Single(direct.DecodeVideo(au, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264)).Sample;
            byte[] b = Assert.Single(viaRtp.DecodeVideo(assembled.ToArray(), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264)).Sample;
            Assert.False(viaRtp.KeyFrameRequired, $"Frame {i} did not decode cleanly after the round trip.");
            Assert.True(a.AsSpan().SequenceEqual(b), $"Frame {i} decoded differently after the round trip.");
        }

        Assert.True(fragmented > 0, "No access unit needed FU-A.");
    }

    [Fact]
    public void Encode_RejectsBadArguments()
    {
        var codec = new H264Codec();
        byte[] frame = new byte[176 * 144 * 3 / 2];
        Assert.Throws<ArgumentNullException>(() => codec.EncodeVideo(176, 144, null, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        Assert.Throws<NotSupportedException>(() => codec.EncodeVideo(176, 144, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8));
        Assert.Throws<NotSupportedException>(() => codec.EncodeVideo(176, 144, frame, (VideoPixelFormatsEnum)99, VideoCodecsEnum.H264));
        Assert.Throws<ArgumentException>(() => codec.EncodeVideo(15, 144, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        Assert.Throws<ArgumentException>(() => codec.EncodeVideo(176, 144, new byte[100], VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        Assert.Throws<ArgumentException>(() => codec.EncodeVideo(176, 144, frame, 100, VideoPixelFormatsEnum.I420, new ArrayBufferWriter<byte>()));
        Assert.Throws<ArgumentException>(() => codec.EncodeVideoFaster(new RawImage { Width = 176, Height = 144 }, VideoCodecsEnum.H264));
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.TargetBitrateKbps = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.FrameRate = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.FrameRate = 61);
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.KeyframeIntervalFrames = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.EncoderThreads = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.EncoderThreads = 5);

        Assert.NotNull(codec.EncodeVideo(176, 144, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        // Settings change on a running encoder.
        codec.TargetBitrateKbps = 500;
        codec.FrameRate = 15;
        codec.KeyframeIntervalFrames = 5;
        codec.EncoderThreads = 2;
        Assert.NotNull(codec.EncodeVideo(176, 144, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        codec.Dispose();
        codec.Dispose();
        Assert.Throws<ObjectDisposedException>(() => codec.EncodeVideo(176, 144, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
    }
}
