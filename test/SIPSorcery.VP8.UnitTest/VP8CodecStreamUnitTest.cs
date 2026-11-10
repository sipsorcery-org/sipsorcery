//-----------------------------------------------------------------------------
// Filename: VP8CodecStreamUnitTest.cs
//
// Description: Unit tests for the VP8Codec EncodeVideo overload that writes
// each encoded frame to a caller supplied stream instead of returning a new
// array.
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
using System.Runtime.InteropServices;
using System.Threading;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class VP8CodecStreamUnitTest
    {
        public static IEnumerable<object[]> GoldenScenarios() => VP8CodecGoldenUnitTest.GoldenScenarios();

        /// <summary>
        /// Writing to a re-used stream gives exactly the frames the array returning method gives.
        /// </summary>
        [Theory]
        [MemberData(nameof(GoldenScenarios))]
        public void EncodeVideoToStream_MatchesGoldenOutput(string scenarioName, string expectedDigest)
        {
            var scenario = Vp8GoldenScenario.Get(scenarioName);
            var stream = new MemoryStream();

            var frames = scenario.Run((codec, width, height, sample, pixelFormat) =>
            {
                stream.SetLength(0);

                int bytesWritten = codec.EncodeVideo(width, height, sample, pixelFormat, VideoCodecsEnum.VP8, stream);
                Assert.Equal(stream.Length, bytesWritten);

                return stream.ToArray();
            });

            Assert.Equal(expectedDigest, Vp8GoldenScenario.Digest(frames));
        }

        /// <summary>
        /// Frames written one after another to the same stream without resetting it are laid out back to back, each
        /// call reporting the length it added, so the stream can be split back into the original frames.
        /// </summary>
        [Fact]
        public void EncodeVideoToStream_ReusedStream_AppendsWholeFrames()
        {
            var scenario = Vp8GoldenScenario.Get("I420_64x48_Default");
            var expected = scenario.Run((codec, w, h, sample, fmt) => codec.EncodeVideo(w, h, sample, fmt, VideoCodecsEnum.VP8));

            var stream = new MemoryStream();
            var lengths = new List<int>();

            scenario.Run((codec, w, h, sample, fmt) =>
            {
                long before = stream.Position;
                int bytesWritten = codec.EncodeVideo(w, h, sample, fmt, VideoCodecsEnum.VP8, stream);
                Assert.Equal(stream.Position - before, bytesWritten);
                lengths.Add(bytesWritten);
                return null;
            });

            byte[] all = stream.ToArray();
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
        /// A stream that can only be written to, with no position, length, seeking, reading or buffer access, is
        /// enough.
        /// </summary>
        [Fact]
        public void EncodeVideoToStream_WriteOnlyNonSeekableStream_ReceivesTheFrames()
        {
            var scenario = Vp8GoldenScenario.Get("I420_176x144_Gop4_IntraFallback_Forced");
            var stream = new WriteOnlyStream();

            var frames = scenario.Run((codec, w, h, sample, fmt) =>
            {
                int before = stream.Written.Count;
                int bytesWritten = codec.EncodeVideo(w, h, sample, fmt, VideoCodecsEnum.VP8, stream);
                Assert.Equal(stream.Written.Count - before, bytesWritten);
                return stream.Written.GetRange(before, bytesWritten).ToArray();
            });

            Assert.Equal(scenario.ExpectedDigest, Vp8GoldenScenario.Digest(frames));
            Assert.Equal(scenario.FrameCount, stream.WriteCalls);
        }

        /// <summary>
        /// The key frame cadence and forced key frames come through unchanged: the frame tag of every frame written
        /// carries the key frame flag exactly where the encoder decided to make one.
        /// </summary>
        [Fact]
        public void EncodeVideoToStream_KeyFramesAndInterFrames_AreFlaggedAsTheyWereEncoded()
        {
            var codec = new VP8Codec { KeyframeIntervalFrames = 4 };
            var scenario = Vp8GoldenScenario.Get("I420_64x48_Default");
            var stream = new MemoryStream();
            var isKeyFrame = new List<bool>();

            for (int i = 0; i < 10; i++)
            {
                if (i == 6)
                {
                    codec.ForceKeyFrame();
                }

                stream.SetLength(0);
                codec.EncodeVideo(64, 48, scenario.CreateFrame(i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, stream);

                // RFC 6386 section 9.1: bit 0 of the frame tag is 0 for a key frame.
                isKeyFrame.Add((stream.GetBuffer()[0] & 0x01) == 0);
            }

            Assert.Equal(new[] { true, false, false, false, true, false, true, false, false, false }, isKeyFrame);
        }

        /// <summary>
        /// A read-only stream is rejected before anything is encoded, so the frame is not consumed by the encoder
        /// (advancing its key frame counter and reference frame) and then lost.
        /// </summary>
        [Fact]
        public void EncodeVideoToStream_ReadOnlyStream_ThrowsWithoutEncoding()
        {
            var codec = new VP8Codec();
            var frame = Vp8GoldenScenario.Get("I420_64x48_Default").CreateFrame(0);

            using (var readOnly = new MemoryStream(new byte[16], writable: false))
            {
                Assert.Throws<ArgumentException>(() => codec.EncodeVideo(64, 48, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, readOnly));
            }

            Assert.Equal(0, codec.FramesSinceLastKeyframe);
        }

        [Fact]
        public void EncodeVideoToStream_NullOutput_Throws()
        {
            var codec = new VP8Codec();
            var frame = Vp8GoldenScenario.Get("I420_64x48_Default").CreateFrame(0);

            Assert.Throws<ArgumentNullException>(() => codec.EncodeVideo(64, 48, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, (Stream)null));
        }

        [Fact]
        public void EncodeVideoToStream_CodecOtherThanVp8_Throws()
        {
            var codec = new VP8Codec();
            var frame = Vp8GoldenScenario.Get("I420_64x48_Default").CreateFrame(0);

            Assert.Throws<NotSupportedException>(() => codec.EncodeVideo(64, 48, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264, new MemoryStream()));
        }

        /// <summary>
        /// The array returning method checks the codec the same way, rather than returning VP8 for another codec.
        /// </summary>
        [Fact]
        public void EncodeVideo_CodecOtherThanVp8_Throws()
        {
            var codec = new VP8Codec();
            var frame = Vp8GoldenScenario.Get("I420_64x48_Default").CreateFrame(0);

            Assert.Throws<NotSupportedException>(() => codec.EncodeVideo(64, 48, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        }

        /// <summary>
        /// A null sample is reported as such, not as a sample of the wrong length.
        /// </summary>
        [Fact]
        public void EncodeVideo_NullSample_Throws()
        {
            var codec = new VP8Codec();

            Assert.Throws<ArgumentNullException>(() => codec.EncodeVideo(64, 48, null, VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8));
        }

        /// <summary>
        /// A sample of the wrong size is rejected with the same exception the array method throws.
        /// </summary>
        [Fact]
        public void EncodeVideoToStream_SampleWrongSize_Throws()
        {
            var codec = new VP8Codec();

            Assert.Throws<ArgumentException>(() => codec.EncodeVideo(64, 48, new byte[64 * 48], VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, new MemoryStream()));
        }

        /// <summary>
        /// When the destination fails the exception reaches the caller, and the next frame is a key frame without the
        /// caller having to force one, so a receiver that missed the lost frame is not left predicting from it.
        /// </summary>
        [Fact]
        public void EncodeVideoToStream_StreamThrows_ExceptionPropagatesAndNextFrameIsKeyFrame()
        {
            var codec = new VP8Codec();
            var scenario = Vp8GoldenScenario.Get("I420_64x48_Default");

            codec.EncodeVideo(64, 48, scenario.CreateFrame(0), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, new MemoryStream());

            var failing = new FailingStream();
            Assert.Throws<IOException>(() => codec.EncodeVideo(64, 48, scenario.CreateFrame(1), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, failing));
            Assert.Equal(1, failing.WriteAttempts);

            var stream = new MemoryStream();
            int bytesWritten = codec.EncodeVideo(64, 48, scenario.CreateFrame(2), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8, stream);
            Assert.True(bytesWritten > 0);

            // RFC 6386 section 9.1: bit 0 of the frame tag is 0 for a key frame.
            Assert.Equal(0, stream.GetBuffer()[0] & 0x01);
        }

        /// <summary>
        /// The sample does not have to live in a managed array.
        /// </summary>
        [Fact]
        public unsafe void EncodeVideoToStream_SampleInUnmanagedMemory_MatchesArraySample()
        {
            var scenario = Vp8GoldenScenario.Get("Bgr_160x96");
            var expected = scenario.Run((codec, w, h, sample, fmt) => codec.EncodeVideo(w, h, sample, fmt, VideoCodecsEnum.VP8));

            var stream = new MemoryStream();
            var actual = scenario.Run((codec, w, h, sample, fmt) =>
            {
                IntPtr native = Marshal.AllocHGlobal(sample.Length);
                try
                {
                    var nativeSample = new Span<byte>((void*)native, sample.Length);
                    sample.CopyTo(nativeSample);

                    stream.SetLength(0);
                    codec.EncodeVideo(w, h, nativeSample, fmt, VideoCodecsEnum.VP8, stream);
                    return stream.ToArray();
                }
                finally
                {
                    Marshal.FreeHGlobal(native);
                }
            });

            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// Once the codec's buffers are sized for the resolution, encoding to a re-used stream allocates next to
        /// nothing per frame, including for input that has to be converted to I420 first. The array returning method
        /// allocates at least the frame, and for converted input a whole I420 frame as well.
        /// </summary>
        [Theory]
        [InlineData(VideoPixelFormatsEnum.I420)]
        [InlineData(VideoPixelFormatsEnum.Bgr)]
        public void EncodeVideoToStream_ReusedDestination_DoesNotAllocatePerFrame(VideoPixelFormatsEnum pixelFormat)
        {
            const int width = 320, height = 240, frames = 16;

            var codec = new VP8Codec();
            var scenario = Vp8GoldenScenario.Create("AdHoc", width, height, pixelFormat, frameCount: 2);
            var inputs = new[] { scenario.CreateFrame(0), scenario.CreateFrame(1) };
            var stream = new MemoryStream();

            // Size every buffer, including the stream, and get the code jitted.
            for (int i = 0; i < 8; i++)
            {
                stream.SetLength(0);
                codec.EncodeVideo(width, height, inputs[i & 1], pixelFormat, VideoCodecsEnum.VP8, stream);
            }

            long encodedBytes = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < frames; i++)
            {
                stream.SetLength(0);
                int streamBytes = codec.EncodeVideo(width, height, inputs[i & 1], pixelFormat, VideoCodecsEnum.VP8, stream);
                encodedBytes += streamBytes;
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
        /// The boolean coders write through raw pointers into the codec's output buffer, including between the fixed
        /// statements that set them up. Encoding while the GC compacts the heap on another thread checks the buffer
        /// cannot move underneath them, which would corrupt memory and the output, with small frames whose output
        /// buffer is on the small object heap that the GC compacts.
        /// </summary>
        [Fact]
        public void EncodeVideo_WhileGcCompacts_OutputIsUnchanged()
        {
            var scenario = Vp8GoldenScenario.Get("I420_64x48_Default");
            using (var stop = new CancellationTokenSource())
            {
                var churn = new Thread(() =>
                {
                    var keep = new List<byte[]>();
                    while (!stop.IsCancellationRequested)
                    {
                        for (int i = 0; i < 64; i++)
                        {
                            keep.Add(new byte[(i * 97) % 2048 + 16]);
                        }
                        keep.RemoveRange(0, keep.Count / 2);
                        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

                        // A pause between collections, so the tests xUnit runs alongside this one are not stalled.
                        Thread.Sleep(1);
                    }
                })
                { IsBackground = true };
                churn.Start();

                try
                {
                    for (int run = 0; run < 5; run++)
                    {
                        var frames = scenario.Run((codec, w, h, sample, fmt) =>
                        {
                            // Interleave garbage with the encode so objects around the buffers are moved.
                            var garbage = new byte[(run * 31) % 512 + 1];
                            GC.KeepAlive(garbage);
                            return codec.EncodeVideo(w, h, sample, fmt, VideoCodecsEnum.VP8);
                        });

                        Assert.Equal(scenario.ExpectedDigest, Vp8GoldenScenario.Digest(frames));
                    }
                }
                finally
                {
                    stop.Cancel();
                    churn.Join();
                }
            }
        }

        /// <summary>
        /// A stream that supports nothing but writing.
        /// </summary>
        private sealed class WriteOnlyStream : Stream
        {
            public List<byte> Written { get; } = new List<byte>();
            public int WriteCalls { get; private set; }

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
                for (int i = 0; i < count; i++)
                {
                    Written.Add(buffer[offset + i]);
                }
            }
        }

        /// <summary>
        /// A writable stream whose writes always fail.
        /// </summary>
        private sealed class FailingStream : Stream
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
}
