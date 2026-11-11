//-----------------------------------------------------------------------------
// Filename: VP8CodecBufferWriterUnitTest.cs
//
// Description: Unit tests for the VP8Codec EncodeVideo overload that writes
// each encoded frame to a caller supplied IBufferWriter<byte> instead of
// returning a new array.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 10 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class VP8CodecBufferWriterUnitTest
    {
        public static IEnumerable<object[]> GoldenScenarios() => VP8CodecGoldenUnitTest.GoldenScenarios();

        /// <summary>
        /// Writing to a re-used buffer writer gives exactly the frames the array returning method gives.
        /// </summary>
        [Theory]
        [MemberData(nameof(GoldenScenarios))]
        public void EncodeVideoToBufferWriter_MatchesGoldenOutput(string scenarioName, string expectedDigest)
        {
            var scenario = Vp8GoldenScenario.Get(scenarioName);
            var output = new ArrayBufferWriter<byte>();

            var frames = scenario.Run((codec, width, height, sample, pixelFormat) =>
            {
                output.ResetWrittenCount();

                int bytesWritten = codec.EncodeVideo(width, height, sample, pixelFormat, VideoCodecsEnum.VP8, output);
                Assert.Equal(output.WrittenCount, bytesWritten);

                return output.WrittenSpan.ToArray();
            });

            Assert.Equal(expectedDigest, Vp8GoldenScenario.Digest(frames));
        }

        /// <summary>
        /// Frames written one after another to the same writer without resetting it are laid out back to back, each
        /// call reporting the length it added, so what was written can be split back into the original frames.
        /// </summary>
        [Fact]
        public void EncodeVideoToBufferWriter_NotReset_AppendsWholeFrames()
        {
            var scenario = Vp8GoldenScenario.Get("I420_64x48_Default");
            var expected = scenario.Run((codec, w, h, sample, fmt) => codec.EncodeVideo(w, h, sample, fmt, VideoCodecsEnum.VP8));

            var output = new ArrayBufferWriter<byte>();
            var lengths = new List<int>();

            scenario.Run((codec, w, h, sample, fmt) =>
            {
                int before = output.WrittenCount;
                int bytesWritten = codec.EncodeVideo(w, h, sample, fmt, VideoCodecsEnum.VP8, output);
                Assert.Equal(output.WrittenCount - before, bytesWritten);
                lengths.Add(bytesWritten);
                return null;
            });

            byte[] all = output.WrittenSpan.ToArray();
            int offset = 0;
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Length, lengths[i]);
                Assert.Equal(expected[i], all.AsSpan(offset, lengths[i]).ToArray());
                offset += lengths[i];
            }
            Assert.Equal(all.Length, offset);
        }

        /// <summary>
        /// The writer is asked for one span of the frame's length, which is committed with one Advance, so each frame
        /// is contiguous in the writer and a writer that grows can size its buffer for the frame in one step.
        /// </summary>
        [Fact]
        public void EncodeVideoToBufferWriter_EachFrameIsOneSpanOfItsLength()
        {
            var scenario = Vp8GoldenScenario.Get("I420_176x144_Gop4_IntraFallback_Forced");
            var output = new RecordingBufferWriter();

            var frames = scenario.Run((codec, w, h, sample, fmt) =>
            {
                output.Calls.Clear();
                int before = output.WrittenCount;
                int bytesWritten = codec.EncodeVideo(w, h, sample, fmt, VideoCodecsEnum.VP8, output);
                Assert.Equal($"GetSpan({bytesWritten}), Advance({bytesWritten})", string.Join(", ", output.Calls));
                return output.WrittenSpan.Slice(before).ToArray();
            });

            Assert.Equal(scenario.ExpectedDigest, Vp8GoldenScenario.Digest(frames));
        }

        /// <summary>
        /// A null writer is rejected before anything is encoded, so the frame is not consumed by the encoder
        /// (advancing its key frame counter and reference frame) and then lost.
        /// </summary>
        [Fact]
        public void EncodeVideoToBufferWriter_NullOutput_ThrowsWithoutEncoding()
        {
            var codec = new VP8Codec();
            var frame = Vp8GoldenScenario.Get("I420_64x48_Default").CreateFrame(0);

            Assert.Throws<ArgumentNullException>(() => codec.EncodeVideo(64, 48, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, (IBufferWriter<byte>)null));
            Assert.Equal(0, codec.FramesSinceLastKeyframe);
        }

        [Fact]
        public void EncodeVideoToBufferWriter_CodecOtherThanVp8_Throws()
        {
            var codec = new VP8Codec();
            var frame = Vp8GoldenScenario.Get("I420_64x48_Default").CreateFrame(0);

            Assert.Throws<NotSupportedException>(() => codec.EncodeVideo(64, 48, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, new ArrayBufferWriter<byte>()));
        }

        /// <summary>
        /// A sample of the wrong size is rejected with the same exception the array method throws.
        /// </summary>
        [Fact]
        public void EncodeVideoToBufferWriter_SampleWrongSize_Throws()
        {
            var codec = new VP8Codec();

            Assert.Throws<ArgumentException>(() => codec.EncodeVideo(64, 48, new byte[64 * 48], VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, new ArrayBufferWriter<byte>()));
        }

        /// <summary>
        /// When the writer cannot hand out memory the exception reaches the caller, nothing is committed to the
        /// writer, and the next frame is a key frame without the caller having to force one, so a receiver that
        /// missed the lost frame is not left predicting from it.
        /// </summary>
        [Fact]
        public void EncodeVideoToBufferWriter_WriterFails_ExceptionPropagatesAndNextFrameIsKeyFrame()
        {
            var codec = new VP8Codec();
            var scenario = Vp8GoldenScenario.Get("I420_64x48_Default");
            var output = new ArrayBufferWriter<byte>();

            codec.EncodeVideo(64, 48, scenario.CreateFrame(0), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, output);

            // Without a failure the frames that follow are inter frames. RFC 6386 section 9.1: bit 0 of the frame tag
            // is 0 for a key frame.
            output.ResetWrittenCount();
            codec.EncodeVideo(64, 48, scenario.CreateFrame(1), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, output);
            Assert.Equal(1, output.WrittenSpan[0] & 0x01);

            var failing = new FailingBufferWriter();
            Assert.Throws<InvalidOperationException>(() => codec.EncodeVideo(64, 48, scenario.CreateFrame(2), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, failing));
            Assert.Equal(1, failing.Requests);
            Assert.Equal(0, failing.AdvanceCalls);

            output.ResetWrittenCount();
            int bytesWritten = codec.EncodeVideo(64, 48, scenario.CreateFrame(3), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, output);
            Assert.True(bytesWritten > 0);
            Assert.Equal(0, output.WrittenSpan[0] & 0x01);
        }

        /// <summary>
        /// Once the codec's buffers are sized for the resolution, encoding to a re-used buffer writer allocates next to
        /// nothing per frame, including for input that has to be converted to I420 first. The array returning method
        /// allocates at least the frame, and for converted input a whole I420 frame as well.
        /// </summary>
        [Theory]
        [InlineData(VideoPixelFormatsEnum.I420)]
        [InlineData(VideoPixelFormatsEnum.Bgr)]
        public void EncodeVideoToBufferWriter_ReusedDestination_DoesNotAllocatePerFrame(VideoPixelFormatsEnum pixelFormat)
        {
            const int width = 320, height = 240, frames = 16;

            var codec = new VP8Codec();
            var scenario = Vp8GoldenScenario.Create("AdHoc", width, height, pixelFormat, frameCount: 2);
            var inputs = new[] { scenario.CreateFrame(0), scenario.CreateFrame(1) };
            var output = new ArrayBufferWriter<byte>();

            // Size every buffer, including the writer, and get the code jitted.
            for (int i = 0; i < 8; i++)
            {
                output.ResetWrittenCount();
                codec.EncodeVideo(width, height, inputs[i & 1], pixelFormat, VideoCodecsEnum.VP8, output);
            }

            long encodedBytes = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++)
            {
                output.ResetWrittenCount();
                encodedBytes += codec.EncodeVideo(width, height, inputs[i & 1], pixelFormat, VideoCodecsEnum.VP8, output);
            }
            long allocatedPerFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / frames;
            long encodedPerFrame = encodedBytes / frames;

            Assert.True(encodedPerFrame > 1000, $"Expected realistic frames, got {encodedPerFrame} bytes per frame.");
            Assert.True(allocatedPerFrame < 1024, $"Allocated {allocatedPerFrame} bytes per frame for {encodedPerFrame} byte frames.");

            long arrayBefore = GC.GetAllocatedBytesForCurrentThread();
            codec.EncodeVideo(width, height, inputs[0], pixelFormat, VideoCodecsEnum.VP8);
            Assert.True(GC.GetAllocatedBytesForCurrentThread() - arrayBefore > allocatedPerFrame);
        }

        /// <summary>
        /// A buffer writer that records the calls made to it, over an <see cref="ArrayBufferWriter{T}"/> that keeps
        /// what is written.
        /// </summary>
        private sealed class RecordingBufferWriter : IBufferWriter<byte>
        {
            private readonly ArrayBufferWriter<byte> _written = new ArrayBufferWriter<byte>();

            public List<string> Calls { get; } = new List<string>();
            public int WrittenCount => _written.WrittenCount;
            public ReadOnlySpan<byte> WrittenSpan => _written.WrittenSpan;

            public void Advance(int count)
            {
                Calls.Add($"Advance({count})");
                _written.Advance(count);
            }

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                Calls.Add($"GetMemory({sizeHint})");
                return _written.GetMemory(sizeHint);
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                Calls.Add($"GetSpan({sizeHint})");
                return _written.GetSpan(sizeHint);
            }
        }

        /// <summary>
        /// A buffer writer that cannot hand out any memory, as a bounded writer that is full would.
        /// </summary>
        private sealed class FailingBufferWriter : IBufferWriter<byte>
        {
            public int Requests { get; private set; }
            public int AdvanceCalls { get; private set; }

            public void Advance(int count) => AdvanceCalls++;

            public Memory<byte> GetMemory(int sizeHint = 0)
            {
                Requests++;
                throw new InvalidOperationException("Simulated buffer writer failure.");
            }

            public Span<byte> GetSpan(int sizeHint = 0)
            {
                Requests++;
                throw new InvalidOperationException("Simulated buffer writer failure.");
            }
        }
    }
}
