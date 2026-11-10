//-----------------------------------------------------------------------------
// Filename: FFmpegVideoEncoderBufferWriterUnitTest.cs
//
// Description: Unit tests for the FFmpegVideoEncoder method that writes each
// encoded frame straight from FFmpeg's packet to an IBufferWriter<byte>,
// rather than copying it into a new array, and for the encoder's argument
// checks, statistics, disposal and per frame allocations.
//
// The tests encode with the native FFmpeg libraries and are skipped when those
// cannot be loaded, see FFmpegAvailability.
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
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorceryMedia.FFmpeg.UnitTest
{
    public class FFmpegVideoEncoderBufferWriterUnitTest
    {
        private const int WIDTH = 320;
        private const int HEIGHT = 240;
        private const int FRAMES = 12;

        /// <summary>
        /// Creates an encoder configured to produce the same bytes every time for the same input, so two instances
        /// can be compared: a single encoding thread, and for libvpx a fixed speed (a positive cpu-used in realtime
        /// mode means libvpx picks the speed from how long frames take to encode).
        /// </summary>
        private static FFmpegVideoEncoder CreateEncoder(VideoCodecsEnum codec, Dictionary<string, string> extraOptions = null)
        {
            var options = new Dictionary<string, string>();
            if (codec == VideoCodecsEnum.VP8)
            {
                options["cpu-used"] = "-5";
            }
            if (extraOptions != null)
            {
                foreach (var option in extraOptions)
                {
                    options[option.Key] = option.Value;
                }
            }

            var encoder = new FFmpegVideoEncoder(options);
            encoder.SetThreadCount(1);
            return encoder;
        }

        public static IEnumerable<object[]> CodecsAndFormats()
        {
            foreach (var codec in new[] { VideoCodecsEnum.H264, VideoCodecsEnum.VP8 })
            {
                foreach (var format in new[] { VideoPixelFormatsEnum.I420, VideoPixelFormatsEnum.Bgr })
                {
                    yield return new object[] { codec, format };
                }
            }
        }

        /// <summary>
        /// Encodes the test sequence with the array returning method, forcing a key frame part way through.
        /// </summary>
        private static List<byte[]> EncodeWithArrays(VideoCodecsEnum codec, VideoPixelFormatsEnum format, int frames = FRAMES)
        {
            using (var encoder = CreateEncoder(codec))
            {
                var encoded = new List<byte[]>();
                for (int i = 0; i < frames; i++)
                {
                    if (i == 7)
                    {
                        encoder.ForceKeyFrame();
                    }
                    encoded.Add(encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, format, i), format, codec));
                }
                return encoded;
            }
        }

        /// <summary>
        /// Every frame written to a re-used buffer writer is exactly the frame the array method returns, key frames,
        /// forced key frame and inter frames alike, from I420 input and from input that goes through the pixel
        /// converter.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [MemberData(nameof(CodecsAndFormats))]
        public void EncodeVideo_BufferWriter_MatchesArrayMethod(VideoCodecsEnum codec, VideoPixelFormatsEnum format)
        {
            var expected = EncodeWithArrays(codec, format);

            using (var encoder = CreateEncoder(codec))
            {
                var output = new ArrayBufferWriter<byte>();
                for (int i = 0; i < FRAMES; i++)
                {
                    if (i == 7)
                    {
                        encoder.ForceKeyFrame();
                    }

                    output.ResetWrittenCount();
                    int written = encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, format, i), format, codec, output);

                    Assert.Equal(expected[i]?.Length ?? 0, written);
                    Assert.Equal(expected[i] ?? Array.Empty<byte>(), output.WrittenSpan.ToArray());
                }
            }

            AssertKeyFrames(codec, expected);
        }

        /// <summary>
        /// Frames written one after another without resetting the writer are laid out back to back, each call
        /// returning what it added, so frame boundaries are kept.
        /// </summary>
        [FFmpegFact("libx264")]
        public void EncodeVideo_BufferWriter_NotReset_AppendsWholeFrames()
        {
            var expected = EncodeWithArrays(VideoCodecsEnum.H264, VideoPixelFormatsEnum.I420);

            using (var encoder = CreateEncoder(VideoCodecsEnum.H264))
            {
                var output = new ArrayBufferWriter<byte>();
                var lengths = new List<int>();

                for (int i = 0; i < FRAMES; i++)
                {
                    if (i == 7)
                    {
                        encoder.ForceKeyFrame();
                    }
                    int before = output.WrittenCount;
                    int written = encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, output);
                    Assert.Equal(output.WrittenCount - before, written);
                    lengths.Add(written);
                }

                byte[] all = output.WrittenSpan.ToArray();
                int offset = 0;
                for (int i = 0; i < FRAMES; i++)
                {
                    Assert.Equal(expected[i] ?? Array.Empty<byte>(), all.AsSpan(offset, lengths[i]).ToArray());
                    offset += lengths[i];
                }
                Assert.Equal(all.Length, offset);
            }
        }

        /// <summary>
        /// The writer is asked for one span of the frame's length, which is committed with one Advance, so each frame
        /// is contiguous in the writer and a writer that grows can size its buffer for the frame in one step.
        /// </summary>
        [FFmpegFact("libvpx")]
        public void EncodeVideo_BufferWriter_EachFrameIsOneSpanOfItsLength()
        {
            var expected = EncodeWithArrays(VideoCodecsEnum.VP8, VideoPixelFormatsEnum.I420);

            using (var encoder = CreateEncoder(VideoCodecsEnum.VP8))
            {
                var output = new RecordingBufferWriter();

                for (int i = 0; i < FRAMES; i++)
                {
                    if (i == 7)
                    {
                        encoder.ForceKeyFrame();
                    }

                    output.Calls.Clear();
                    int before = output.WrittenCount;
                    int written = encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, output);

                    int length = expected[i]?.Length ?? 0;
                    Assert.Equal(length, written);
                    Assert.Equal(length == 0 ? string.Empty : $"GetSpan({length}), Advance({length})", string.Join(", ", output.Calls));
                    Assert.Equal(expected[i] ?? Array.Empty<byte>(), output.WrittenSpan.Slice(before).ToArray());
                }
            }
        }

        /// <summary>
        /// An encoder that holds frames back (libx264 with rate control look-ahead, i.e. without the zerolatency tune)
        /// returns 0, and writes nothing, until it starts producing output; the frames then arrive in order and match
        /// the array method, which returns null for the same frames. (libvpx ignores lag-in-frames in its realtime
        /// mode, so it cannot be used for this.)
        /// </summary>
        [FFmpegFact("libx264")]
        public void EncodeVideo_BufferWriter_EncoderHoldsFramesBack_ReturnsZeroAndWritesNothingUntilOutput()
        {
            const int frames = 24;
            var lookAhead = new Dictionary<string, string> { ["tune"] = "film", ["x264-params"] = "rc-lookahead=4" };
            var expected = new List<byte[]>();

            using (var reference = CreateEncoder(VideoCodecsEnum.H264, lookAhead))
            {
                for (int i = 0; i < frames; i++)
                {
                    expected.Add(reference.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
                }
            }

            // The encoder really does hold frames back, and then outputs them.
            Assert.Null(expected[0]);
            Assert.NotNull(expected[frames - 1]);
            Assert.True(EncodedFrames.H264HasIdr(expected.Find(f => f != null)));

            using (var encoder = CreateEncoder(VideoCodecsEnum.H264, lookAhead))
            {
                var output = new ArrayBufferWriter<byte>();

                for (int i = 0; i < frames; i++)
                {
                    int before = output.WrittenCount;
                    int written = encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, output);

                    if (expected[i] == null)
                    {
                        Assert.Equal(0, written);
                        Assert.Equal(before, output.WrittenCount);
                    }
                    else
                    {
                        Assert.Equal(expected[i].Length, written);
                        Assert.Equal(expected[i], output.WrittenSpan.Slice(before).ToArray());
                    }
                }
            }
        }

        /// <summary>
        /// A null writer is rejected before encoding, so no frame is consumed by the encoder and lost: the next frame
        /// encoded is the first frame the encoder has seen, a key frame identical to the reference encoder's first.
        /// </summary>
        [FFmpegFact("libx264")]
        public void EncodeVideo_BufferWriter_Null_ThrowsWithoutConsumingAFrame()
        {
            var expected = EncodeWithArrays(VideoCodecsEnum.H264, VideoPixelFormatsEnum.I420, frames: 2);

            using (var encoder = CreateEncoder(VideoCodecsEnum.H264))
            {
                var frame0 = TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 0);
                Assert.Throws<ArgumentNullException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, frame0, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, null));

                var output = new ArrayBufferWriter<byte>();
                Assert.Equal(expected[0].Length, encoder.EncodeVideo(WIDTH, HEIGHT, frame0, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, output));
                Assert.Equal(expected[0], output.WrittenSpan.ToArray());
            }
        }

        /// <summary>
        /// FFmpeg reads the sample through a pointer, so a sample shorter than the frame is refused rather than read
        /// past its end, whether it is a span or an array, and the array method refuses a null sample.
        /// </summary>
        [FFmpegFact("libx264")]
        public void EncodeVideo_SampleTooSmallOrNull_Throws()
        {
            using (var encoder = CreateEncoder(VideoCodecsEnum.H264))
            {
                var tooSmall = new byte[WIDTH * HEIGHT];
                Assert.Throws<ArgumentException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, tooSmall, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, new ArrayBufferWriter<byte>()));
                Assert.Throws<ArgumentException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, new byte[WIDTH * HEIGHT * 3 - 1], VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264, new ArrayBufferWriter<byte>()));
                Assert.Throws<ArgumentException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, tooSmall, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
                Assert.Throws<ArgumentNullException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, null, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
            }
        }

        /// <summary>
        /// A change of input pixel format at the same size gets a pixel converter for the new format, rather than the
        /// one made for the previous format, which would read the sample with the wrong layout and, from BGRA to BGR,
        /// past its end: the BGR frame encodes to the same bytes as the same frame converted to I420 by a BGR converter of
        /// its own and given as I420.
        /// </summary>
        [FFmpegFact("libx264")]
        public unsafe void EncodeVideo_PixelFormatChangesAtSameSize_ConvertsWithNewFormat()
        {
            byte[] bgra = TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.Bgra, 0);
            byte[] bgr = TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.Bgr, 1);

            byte[] i420 = new byte[WIDTH * HEIGHT * 3 / 2];
            using (var converter = new VideoFrameConverter(WIDTH, HEIGHT, AVPixelFormat.AV_PIX_FMT_BGR24, WIDTH, HEIGHT, AVPixelFormat.AV_PIX_FMT_YUV420P))
            {
                fixed (byte* pBgr = bgr)
                {
                    // The converter's output planes are packed back to back, with no row padding.
                    AVFrame converted = converter.Convert(pBgr);
                    new ReadOnlySpan<byte>(converted.data[0], i420.Length).CopyTo(i420);
                }
            }

            byte[] EncodeAfterBgra(byte[] second, VideoPixelFormatsEnum secondFormat)
            {
                using (var encoder = CreateEncoder(VideoCodecsEnum.H264))
                {
                    encoder.EncodeVideo(WIDTH, HEIGHT, bgra, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.H264);

                    var output = new ArrayBufferWriter<byte>();
                    encoder.EncodeVideo(WIDTH, HEIGHT, second, secondFormat, VideoCodecsEnum.H264, output);
                    return output.WrittenSpan.ToArray();
                }
            }

            byte[] expected = EncodeAfterBgra(i420, VideoPixelFormatsEnum.I420);

            Assert.NotEmpty(expected);
            Assert.Equal(expected, EncodeAfterBgra(bgr, VideoPixelFormatsEnum.Bgr));
        }

        /// <summary>
        /// When the buffer writer cannot hand out memory the exception reaches the caller, nothing is committed to the
        /// writer, the encoder keeps working, and the next frame is a key frame, so a receiver that missed the lost
        /// frame can decode again.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [InlineData(VideoCodecsEnum.H264)]
        [InlineData(VideoCodecsEnum.VP8)]
        public void EncodeVideo_BufferWriter_WriteFails_ExceptionPassedOnAndNextFrameIsKeyFrame(VideoCodecsEnum codec)
        {
            using (var encoder = CreateEncoder(codec))
            {
                var output = new ArrayBufferWriter<byte>();
                Assert.True(encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 0), VideoPixelFormatsEnum.I420, codec, output) > 0);

                output.ResetWrittenCount();
                Assert.True(encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 1), VideoPixelFormatsEnum.I420, codec, output) > 0);
                Assert.False(IsKeyFrame(codec, output.WrittenSpan.ToArray()));

                var failing = new FailingBufferWriter();
                Assert.Throws<InvalidOperationException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 2), VideoPixelFormatsEnum.I420, codec, failing));
                Assert.Equal(1, failing.Requests);
                Assert.Equal(0, failing.AdvanceCalls);

                output.ResetWrittenCount();
                Assert.True(encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 3), VideoPixelFormatsEnum.I420, codec, output) > 0);
                Assert.True(IsKeyFrame(codec, output.WrittenSpan.ToArray()));
            }
        }

        /// <summary>
        /// Subscribing to the encoder statistics before the first frame, when there is no encoder context yet, does not
        /// stop that frame being encoded, through the array method or through Encode with an AVFrame, which reaches the
        /// frame rate check before it creates the encoder context.
        /// </summary>
        [FFmpegFact("libx264")]
        public unsafe void Encode_StatisticsSubscribedBeforeFirstFrame_Encodes()
        {
            var frame = TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 0);

            using (var encoder = CreateEncoder(VideoCodecsEnum.H264))
            {
                int statistics = 0;
                encoder.OnVideoEncoderStatistics += (sender, stats) => statistics++;

                Assert.NotNull(encoder.EncodeVideo(WIDTH, HEIGHT, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
                Assert.Equal(1, statistics);
            }

            using (var encoder = CreateEncoder(VideoCodecsEnum.H264))
            {
                int statistics = 0;
                encoder.OnVideoEncoderStatistics += (sender, stats) => statistics++;

                fixed (byte* pFrame = frame)
                {
                    AVFrame avFrame = encoder.MakeFrame(pFrame, WIDTH, HEIGHT);
                    Assert.NotNull(encoder.Encode(AVCodecID.AV_CODEC_ID_H264, &avFrame, 30));
                }
                Assert.Equal(1, statistics);
            }
        }

        /// <summary>
        /// A disposed encoder writes nothing and returns 0, as the array method returns null.
        /// </summary>
        [FFmpegFact("libx264")]
        public void EncodeVideo_BufferWriter_AfterDispose_ReturnsZero()
        {
            var encoder = CreateEncoder(VideoCodecsEnum.H264);
            var frame = TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 0);
            var output = new ArrayBufferWriter<byte>();
            Assert.True(encoder.EncodeVideo(WIDTH, HEIGHT, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, output) > 0);

            encoder.Dispose();

            output.ResetWrittenCount();
            Assert.Equal(0, encoder.EncodeVideo(WIDTH, HEIGHT, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, output));
            Assert.Equal(0, output.WrittenCount);
            Assert.Null(encoder.EncodeVideo(WIDTH, HEIGHT, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        }

        /// <summary>
        /// Disposing while another thread is encoding waits for the frame in progress, rather than freeing the codec
        /// context and packet underneath it.
        /// </summary>
        [FFmpegFact("libx264")]
        public void Dispose_WhileEncodingOnAnotherThread_IsSafe()
        {
            var frame = TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 0);

            for (int run = 0; run < 20; run++)
            {
                var encoder = CreateEncoder(VideoCodecsEnum.H264);
                using (var started = new ManualResetEventSlim())
                {
                    var encodeTask = Task.Run(() =>
                    {
                        var output = new ArrayBufferWriter<byte>();
                        for (int i = 0; i < 200; i++)
                        {
                            output.ResetWrittenCount();
                            encoder.EncodeVideo(WIDTH, HEIGHT, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, output);
                            started.Set();
                        }
                    });

                    started.Wait(TimeSpan.FromSeconds(10));
                    Thread.Sleep(run % 5);
                    encoder.Dispose();

                    encodeTask.Wait(TimeSpan.FromSeconds(30));
                    Assert.True(encodeTask.IsCompleted);
                    Assert.Null(encodeTask.Exception);
                }
            }
        }

        /// <summary>
        /// Once the writer has grown to the frame size, encoding to it allocates nothing per frame, whether the frame
        /// is passed to FFmpeg directly (I420) or through the pixel converter (BGR); the array method allocates at
        /// least the encoded frame.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [MemberData(nameof(CodecsAndFormats))]
        public void EncodeVideo_BufferWriter_ReusedDestination_DoesNotAllocatePerFrame(VideoCodecsEnum codec, VideoPixelFormatsEnum format)
        {
            using (var encoder = CreateEncoder(codec))
            {
                var frames = new[] { TestFrames.Create(WIDTH, HEIGHT, format, 0), TestFrames.Create(WIDTH, HEIGHT, format, 1) };
                var output = new ArrayBufferWriter<byte>(256 * 1024);

                for (int i = 0; i < 12; i++)
                {
                    output.ResetWrittenCount();
                    encoder.EncodeVideo(WIDTH, HEIGHT, frames[i & 1], format, codec, output);
                }

                const int count = 40;
                long encodedBytes = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < count; i++)
                {
                    output.ResetWrittenCount();
                    encodedBytes += encoder.EncodeVideo(WIDTH, HEIGHT, frames[i & 1], format, codec, output);
                }
                long allocatedPerFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / count;

                Assert.True(encodedBytes > 0);
                Assert.True(allocatedPerFrame < 64, $"Allocated {allocatedPerFrame} bytes per frame.");

                long arrayBefore = GC.GetAllocatedBytesForCurrentThread();
                var array = encoder.EncodeVideo(WIDTH, HEIGHT, frames[0], format, codec);
                Assert.True(GC.GetAllocatedBytesForCurrentThread() - arrayBefore >= (array?.Length ?? 0));
            }
        }

        private static bool IsKeyFrame(VideoCodecsEnum codec, byte[] frame) =>
            codec == VideoCodecsEnum.H264 ? EncodedFrames.H264HasIdr(frame) : EncodedFrames.Vp8IsKeyFrame(frame);

        /// <summary>
        /// Checks the encoded sequence has the key frames the settings ask for: the first frame and the forced one.
        /// </summary>
        private static void AssertKeyFrames(VideoCodecsEnum codec, List<byte[]> frames)
        {
            Assert.True(IsKeyFrame(codec, frames[0]));
            Assert.True(IsKeyFrame(codec, frames[7]));
            Assert.False(IsKeyFrame(codec, frames[1]));
            Assert.False(IsKeyFrame(codec, frames[8]));
        }
    }
}
