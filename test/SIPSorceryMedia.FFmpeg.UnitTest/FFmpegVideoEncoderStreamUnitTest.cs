//-----------------------------------------------------------------------------
// Filename: FFmpegVideoEncoderStreamUnitTest.cs
//
// Description: Unit tests for the FFmpegVideoEncoder method that writes each
// encoded frame straight from FFmpeg's packet to a stream, rather than copying
// it into a new array, and for the encoder's argument checks, statistics,
// disposal and per frame allocations.
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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorceryMedia.FFmpeg.UnitTest
{
    public class FFmpegVideoEncoderStreamUnitTest
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
        /// Every frame written to a re-used stream is exactly the frame the array method returns, key frames, forced
        /// key frame and inter frames alike, from I420 input and from input that goes through the pixel converter.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [MemberData(nameof(CodecsAndFormats))]
        public void EncodeVideo_Stream_MatchesArrayMethod(VideoCodecsEnum codec, VideoPixelFormatsEnum format)
        {
            var expected = EncodeWithArrays(codec, format);

            using (var encoder = CreateEncoder(codec))
            {
                var stream = new MemoryStream();
                for (int i = 0; i < FRAMES; i++)
                {
                    if (i == 7)
                    {
                        encoder.ForceKeyFrame();
                    }

                    stream.SetLength(0);
                    int written = encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, format, i), format, codec, stream);

                    Assert.Equal(expected[i]?.Length ?? 0, written);
                    Assert.Equal(expected[i] ?? Array.Empty<byte>(), stream.ToArray());
                }
            }

            AssertKeyFrames(codec, expected);
        }

        /// <summary>
        /// Frames written one after another without resetting the stream are laid out back to back, each call
        /// returning what it added, so frame boundaries are kept.
        /// </summary>
        [FFmpegFact("libx264")]
        public void EncodeVideo_Stream_NotReset_AppendsWholeFrames()
        {
            var expected = EncodeWithArrays(VideoCodecsEnum.H264, VideoPixelFormatsEnum.I420);

            using (var encoder = CreateEncoder(VideoCodecsEnum.H264))
            {
                var stream = new MemoryStream();
                var lengths = new List<int>();

                for (int i = 0; i < FRAMES; i++)
                {
                    if (i == 7)
                    {
                        encoder.ForceKeyFrame();
                    }
                    long before = stream.Position;
                    int written = encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, stream);
                    Assert.Equal(stream.Position - before, written);
                    lengths.Add(written);
                }

                byte[] all = stream.ToArray();
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
        /// A stream that can only be written to, with no position, length, seeking or reading, is enough, and each
        /// frame arrives in a single write.
        /// </summary>
        [FFmpegFact("libvpx")]
        public void EncodeVideo_Stream_WriteOnlyNonSeekable_ReceivesEachFrameInOneWrite()
        {
            var expected = EncodeWithArrays(VideoCodecsEnum.VP8, VideoPixelFormatsEnum.I420);

            using (var encoder = CreateEncoder(VideoCodecsEnum.VP8))
            {
                var stream = new WriteOnlyStream();
                int framesWritten = 0;

                for (int i = 0; i < FRAMES; i++)
                {
                    if (i == 7)
                    {
                        encoder.ForceKeyFrame();
                    }
                    if (encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, stream) > 0)
                    {
                        framesWritten++;
                    }
                }

                var concatenated = new MemoryStream();
                foreach (var frame in expected)
                {
                    if (frame != null)
                    {
                        concatenated.Write(frame, 0, frame.Length);
                    }
                }

                Assert.Equal(concatenated.ToArray(), stream.ToArray());
                Assert.Equal(framesWritten, stream.WriteCalls);
            }
        }

        /// <summary>
        /// An encoder that holds frames back (libx264 with rate control look-ahead, i.e. without the zerolatency tune)
        /// returns 0, and writes nothing, until it starts producing output; the frames then arrive in order and match
        /// the array method, which returns null for the same frames. (libvpx ignores lag-in-frames in its realtime
        /// mode, so it cannot be used for this.)
        /// </summary>
        [FFmpegFact("libx264")]
        public void EncodeVideo_Stream_EncoderHoldsFramesBack_ReturnsZeroAndWritesNothingUntilOutput()
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
                var stream = new MemoryStream();

                for (int i = 0; i < frames; i++)
                {
                    long lengthBefore = stream.Length;
                    int written = encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, stream);

                    if (expected[i] == null)
                    {
                        Assert.Equal(0, written);
                        Assert.Equal(lengthBefore, stream.Length);
                    }
                    else
                    {
                        Assert.Equal(expected[i].Length, written);
                        Assert.Equal(expected[i], stream.ToArray().AsSpan((int)lengthBefore).ToArray());
                    }
                }
            }
        }

        /// <summary>
        /// A stream that cannot be written to, or none, is rejected before encoding, so no frame is consumed by the
        /// encoder and lost: the next frame encoded is the first frame the encoder has seen, a key frame identical to
        /// the reference encoder's first.
        /// </summary>
        [FFmpegFact("libx264")]
        public void EncodeVideo_Stream_ReadOnlyOrNull_ThrowsWithoutConsumingAFrame()
        {
            var expected = EncodeWithArrays(VideoCodecsEnum.H264, VideoPixelFormatsEnum.I420, frames: 2);

            using (var encoder = CreateEncoder(VideoCodecsEnum.H264))
            {
                var frame0 = TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 0);
                Assert.Throws<ArgumentException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, frame0, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, new MemoryStream(new byte[4], writable: false)));
                Assert.Throws<ArgumentNullException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, frame0, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, null));

                var stream = new MemoryStream();
                Assert.Equal(expected[0].Length, encoder.EncodeVideo(WIDTH, HEIGHT, frame0, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, stream));
                Assert.Equal(expected[0], stream.ToArray());
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
                Assert.Throws<ArgumentException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, tooSmall, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, new MemoryStream()));
                Assert.Throws<ArgumentException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, new byte[WIDTH * HEIGHT * 3 - 1], VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264, new MemoryStream()));
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

                    var stream = new MemoryStream();
                    encoder.EncodeVideo(WIDTH, HEIGHT, second, secondFormat, VideoCodecsEnum.H264, stream);
                    return stream.ToArray();
                }
            }

            byte[] expected = EncodeAfterBgra(i420, VideoPixelFormatsEnum.I420);

            Assert.NotEmpty(expected);
            Assert.Equal(expected, EncodeAfterBgra(bgr, VideoPixelFormatsEnum.Bgr));
        }

        /// <summary>
        /// When writing to the stream fails the exception reaches the caller, the encoder keeps working, and the next
        /// frame is a key frame, so a receiver that missed the lost frame can decode again.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [InlineData(VideoCodecsEnum.H264)]
        [InlineData(VideoCodecsEnum.VP8)]
        public void EncodeVideo_Stream_WriteFails_ExceptionPassedOnAndNextFrameIsKeyFrame(VideoCodecsEnum codec)
        {
            using (var encoder = CreateEncoder(codec))
            {
                var stream = new MemoryStream();
                Assert.True(encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 0), VideoPixelFormatsEnum.I420, codec, stream) > 0);

                stream.SetLength(0);
                Assert.True(encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 1), VideoPixelFormatsEnum.I420, codec, stream) > 0);
                Assert.False(IsKeyFrame(codec, stream.ToArray()));

                var failing = new FailingStream();
                Assert.Throws<IOException>(() => encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 2), VideoPixelFormatsEnum.I420, codec, failing));
                Assert.Equal(1, failing.WriteAttempts);

                stream.SetLength(0);
                Assert.True(encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 3), VideoPixelFormatsEnum.I420, codec, stream) > 0);
                Assert.True(IsKeyFrame(codec, stream.ToArray()));
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
        public void EncodeVideo_Stream_AfterDispose_ReturnsZero()
        {
            var encoder = CreateEncoder(VideoCodecsEnum.H264);
            var frame = TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, 0);
            var stream = new MemoryStream();
            Assert.True(encoder.EncodeVideo(WIDTH, HEIGHT, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, stream) > 0);

            encoder.Dispose();

            stream.SetLength(0);
            Assert.Equal(0, encoder.EncodeVideo(WIDTH, HEIGHT, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, stream));
            Assert.Equal(0, stream.Length);
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
                        var stream = new MemoryStream();
                        for (int i = 0; i < 200; i++)
                        {
                            stream.SetLength(0);
                            encoder.EncodeVideo(WIDTH, HEIGHT, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, stream);
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
        /// Once the destination has grown to the frame size, encoding to it allocates nothing per frame, whether the
        /// frame is passed to FFmpeg directly (I420) or through the pixel converter (BGR); the array method allocates
        /// at least the encoded frame.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [MemberData(nameof(CodecsAndFormats))]
        public void EncodeVideo_Stream_ReusedDestination_DoesNotAllocatePerFrame(VideoCodecsEnum codec, VideoPixelFormatsEnum format)
        {
            using (var encoder = CreateEncoder(codec))
            {
                var frames = new[] { TestFrames.Create(WIDTH, HEIGHT, format, 0), TestFrames.Create(WIDTH, HEIGHT, format, 1) };
                var stream = new MemoryStream(256 * 1024);

                for (int i = 0; i < 12; i++)
                {
                    stream.SetLength(0);
                    encoder.EncodeVideo(WIDTH, HEIGHT, frames[i & 1], format, codec, stream);
                }

                const int count = 40;
                long encodedBytes = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < count; i++)
                {
                    stream.SetLength(0);
                    encodedBytes += encoder.EncodeVideo(WIDTH, HEIGHT, frames[i & 1], format, codec, stream);
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
