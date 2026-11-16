//-----------------------------------------------------------------------------
// Filename: EncodedVideoSampleEventsTest.cs
//
// Description: Unit tests for EncodedVideoSampleEvents, which raises a video
// source's encoded frames on its array and span events.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 16 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SIPSorceryMedia.Abstractions.UnitTest
{
    public class EncodedVideoSampleEventsTest
    {
        private static byte[] CreateFrame(int length, int seed)
        {
            var frame = new byte[length];
            new Random(seed).NextBytes(frame);
            return frame;
        }

        // Writes the frame in one piece, as the encoders do.
        private static bool WriteFrame(byte[] frame, IBufferWriter<byte> output)
        {
            frame.CopyTo(output.GetSpan(frame.Length));
            output.Advance(frame.Length);
            return true;
        }

        /// <summary>
        /// A frame is raised on the span event, then as an array with the same bytes and duration on the array event.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_RaisesTheFrameOnBothEvents()
        {
            var events = new EncodedVideoSampleEvents();
            var order = new List<string>();
            byte[] fromSpan = null;
            byte[] fromArray = null;
            uint spanDuration = 0;
            uint arrayDuration = 0;
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => { order.Add("span"); spanDuration = duration; fromSpan = sample.ToArray(); };
            events.OnVideoSourceEncodedSample += (duration, sample) => { order.Add("array"); arrayDuration = duration; fromArray = sample; };

            var frame = CreateFrame(1000, 1);
            int length = events.EncodeAndRaise(3000, frame, (f, output) => WriteFrame(f, output));

            Assert.Equal(1000, length);
            Assert.Equal(new[] { "span", "array" }, order);
            Assert.Equal(frame, fromSpan);
            Assert.Equal(frame, fromArray);
            Assert.Equal(3000u, spanDuration);
            Assert.Equal(3000u, arrayDuration);
        }

        /// <summary>
        /// The array event's subscribers get an array of their own, which later frames do not change.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_ArraySubscribersOwnTheirArrays()
        {
            var events = new EncodedVideoSampleEvents();
            var arrays = new List<byte[]>();
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => { };
            events.OnVideoSourceEncodedSample += (duration, sample) => arrays.Add(sample);

            var first = CreateFrame(500, 1);
            var second = CreateFrame(500, 2);
            events.EncodeAndRaise(3000, first, (f, output) => WriteFrame(f, output));
            events.EncodeAndRaise(3000, second, (f, output) => WriteFrame(f, output));

            Assert.Equal(2, arrays.Count);
            Assert.NotSame(arrays[0], arrays[1]);
            Assert.Equal(first, arrays[0]);
            Assert.Equal(second, arrays[1]);
        }

        /// <summary>
        /// When the encode step reports no frame, for example because the encoder holds it back, nothing is raised, even
        /// if something was written.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_NoFrame_RaisesNothing()
        {
            var events = new EncodedVideoSampleEvents();
            int raised = 0;
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => raised++;
            events.OnVideoSourceEncodedSample += (duration, sample) => raised++;

            int length = events.EncodeAndRaise(3000, CreateFrame(100, 1), (f, output) => WriteFrame(f, output) && false);

            Assert.Equal(0, length);
            Assert.Equal(0, raised);
        }

        /// <summary>
        /// An exception from the encode step is passed on before anything is raised, and the next frame is raised as
        /// usual.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_EncodeThrows_PassedOnBeforeAnythingIsRaised()
        {
            var events = new EncodedVideoSampleEvents();
            var raised = new List<byte[]>();
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => raised.Add(sample.ToArray());

            Assert.Throws<InvalidOperationException>(() => events.EncodeAndRaise(3000, CreateFrame(100, 1), (f, output) =>
            {
                WriteFrame(f, output);
                throw new InvalidOperationException("The encode failed.");
            }));
            Assert.Empty(raised);

            var frame = CreateFrame(50, 2);
            events.EncodeAndRaise(3000, frame, (f, output) => WriteFrame(f, output));
            Assert.Equal(new[] { frame }, raised);
        }

        /// <summary>
        /// A handler that pushes frames while a frame is being raised, as a relay might, gets them raised in full first,
        /// and the handlers after it still get the frame they were raised with.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_HandlerPushesFrames_LaterHandlersStillGetTheirFrame()
        {
            var events = new EncodedVideoSampleEvents();
            var frames = new[] { CreateFrame(1000, 1), CreateFrame(600, 2), CreateFrame(400, 3) };
            bool pushed = false;
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) =>
            {
                if (!pushed)
                {
                    pushed = true;
                    events.EncodeAndRaise(3000, frames[1], (f, output) => WriteFrame(f, output));
                    events.EncodeAndRaise(3000, frames[2], (f, output) => WriteFrame(f, output));
                }
            };
            var fromSpan = new List<byte[]>();
            var fromArray = new List<byte[]>();
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => fromSpan.Add(sample.ToArray());
            events.OnVideoSourceEncodedSample += (duration, sample) => fromArray.Add(sample);

            events.EncodeAndRaise(3000, frames[0], (f, output) => WriteFrame(f, output));

            var raisedOrder = new[] { frames[1], frames[2], frames[0] };
            Assert.Equal(raisedOrder, fromSpan);
            Assert.Equal(raisedOrder, fromArray);
        }

        /// <summary>
        /// A frame written in pieces of growing size, through GetSpan and GetMemory in turn, is raised whole.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_FrameWrittenInPieces_RaisedWhole()
        {
            var events = new EncodedVideoSampleEvents();
            byte[] fromSpan = null;
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => fromSpan = sample.ToArray();

            var frame = CreateFrame(20000, 1);
            events.EncodeAndRaise(3000, frame, (f, output) =>
            {
                int offset = 0;
                int piece = 1;
                bool useMemory = false;
                while (offset < f.Length)
                {
                    int length = Math.Min(piece, f.Length - offset);
                    Span<byte> destination = useMemory ? output.GetMemory(length).Span : output.GetSpan(length);
                    Assert.True(destination.Length >= length);
                    f.AsSpan(offset, length).CopyTo(destination);
                    output.Advance(length);
                    offset += length;
                    piece *= 3;
                    useMemory = !useMemory;
                }

                // Without a size hint there is still room for at least one byte.
                Assert.True(output.GetSpan().Length >= 1);
                return true;
            });

            Assert.Equal(frame, fromSpan);
        }

        /// <summary>
        /// Frames of very different sizes in turn are each raised whole, at their own length: the buffer grows for a
        /// frame larger than any before it, and a smaller frame after it is not padded to the buffer's size.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_FramesOfDifferentSizes_EachRaisedWhole()
        {
            var events = new EncodedVideoSampleEvents();
            var fromSpan = new List<byte[]>();
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => fromSpan.Add(sample.ToArray());

            var frames = new[] { CreateFrame(10, 1), CreateFrame(100000, 2), CreateFrame(50, 3), CreateFrame(250000, 4), CreateFrame(1, 5) };
            foreach (var frame in frames)
            {
                events.EncodeAndRaise(3000, frame, (f, output) => WriteFrame(f, output));
            }

            Assert.Equal(frames, fromSpan);
        }

        /// <summary>
        /// The writer the frame is encoded into refuses a negative size or advance, an advance past the memory it handed
        /// out, and a size larger than any array can be, which it refuses without trying to allocate it.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_WriterRefusesInvalidRequests()
        {
            var events = new EncodedVideoSampleEvents();
            events.EncodeAndRaise(3000, 0, (unused, output) =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => output.GetSpan(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => output.GetMemory(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => output.Advance(-1));
                int available = output.GetSpan(100).Length;
                Assert.Throws<ArgumentOutOfRangeException>(() => output.Advance(available + 1));
                Assert.Throws<OutOfMemoryException>(() => output.GetSpan(int.MaxValue));
                return false;
            });
        }

        /// <summary>
        /// Frames pushed from several threads at once are raised one at a time, each whole, without one overwriting
        /// another in the re-used buffer.
        /// </summary>
        [Fact]
        public async Task EncodeAndRaise_FromSeveralThreads_EachFrameRaisedWhole()
        {
            const int threads = 4;
            const int framesPerThread = 200;
            var events = new EncodedVideoSampleEvents();
            int raised = 0;
            int corrupted = 0;

            // Every byte of a frame is its id, and its length is 1000 plus the id.
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) =>
            {
                byte id = sample[0];
                bool whole = sample.Length == 1000 + id;
                for (int i = 0; whole && i < sample.Length; i++)
                {
                    whole = sample[i] == id;
                }

                if (!whole)
                {
                    Interlocked.Increment(ref corrupted);
                }
                Interlocked.Increment(ref raised);
            };

            var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
            {
                for (int n = 0; n < framesPerThread; n++)
                {
                    byte id = (byte)((t * framesPerThread + n) % 200);
                    events.EncodeAndRaise(3000, id, (frameId, output) =>
                    {
                        // Written in two halves with a yield in between, so frames from other threads get the chance to
                        // interleave if they could.
                        int length = 1000 + frameId;
                        int half = length / 2;
                        output.GetSpan(half).Slice(0, half).Fill(frameId);
                        output.Advance(half);
                        Thread.Yield();
                        output.GetSpan(length - half).Slice(0, length - half).Fill(frameId);
                        output.Advance(length - half);
                        return true;
                    });
                }
            })).ToArray();
            await Task.WhenAll(tasks);

            Assert.Equal(threads * framesPerThread, raised);
            Assert.Equal(0, corrupted);
        }

        /// <summary>
        /// With only the span event subscribed, raising a frame allocates nothing once the buffer has grown to the frame
        /// size.
        /// </summary>
        [Fact]
        public void EncodeAndRaise_OnlySpanSubscribers_DoesNotAllocatePerFrame()
        {
            var events = new EncodedVideoSampleEvents();
            long raisedBytes = 0;
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => raisedBytes += sample.Length;
            var frame = CreateFrame(20000, 1);

            for (int i = 0; i < 10; i++)
            {
                events.EncodeAndRaise(3000, frame, (f, output) => WriteFrame(f, output));
            }

            const int count = 1000;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < count; i++)
            {
                events.EncodeAndRaise(3000, frame, (f, output) => WriteFrame(f, output));
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal((10L + count) * frame.Length, raisedBytes);
            Assert.True(allocated < count, $"Allocated {allocated} bytes for {count} frames.");
        }

        /// <summary>
        /// A frame the caller encoded into an array is raised on the span event over the array, then on the array event
        /// with the array itself.
        /// </summary>
        [Fact]
        public void Raise_RaisesTheArrayOnBothEvents()
        {
            var events = new EncodedVideoSampleEvents();
            var order = new List<string>();
            byte[] fromSpan = null;
            byte[] fromArray = null;
            events.OnVideoSourceEncodedSampleFaster += (duration, sample) => { order.Add("span"); fromSpan = sample.ToArray(); };
            events.OnVideoSourceEncodedSample += (duration, sample) => { order.Add("array"); fromArray = sample; };

            var frame = CreateFrame(300, 1);
            events.Raise(1500, frame);

            Assert.Equal(new[] { "span", "array" }, order);
            Assert.Equal(frame, fromSpan);
            Assert.Same(frame, fromArray);
        }

        [Fact]
        public void Raise_NullSample_Refused()
        {
            var events = new EncodedVideoSampleEvents();
            var excp = Assert.Throws<ArgumentNullException>(() => events.Raise(1500, null));
            Assert.Equal("sample", excp.ParamName);
        }

        [Fact]
        public void EncodeAndRaise_NullEncode_Refused()
        {
            var events = new EncodedVideoSampleEvents();
            var excp = Assert.Throws<ArgumentNullException>(() => events.EncodeAndRaise<int>(1500, 0, null));
            Assert.Equal("encode", excp.ParamName);
        }

        /// <summary>
        /// HasSubscribers and HasSpanSubscribers follow what is subscribed to each event.
        /// </summary>
        [Fact]
        public void HasSubscribers_FollowTheSubscriptions()
        {
            var events = new EncodedVideoSampleEvents();
            EncodedSampleFasterDelegate spanHandler = (duration, sample) => { };
            EncodedSampleDelegate arrayHandler = (duration, sample) => { };

            Assert.False(events.HasSubscribers);
            Assert.False(events.HasSpanSubscribers);

            events.OnVideoSourceEncodedSampleFaster += spanHandler;
            Assert.True(events.HasSubscribers);
            Assert.True(events.HasSpanSubscribers);

            events.OnVideoSourceEncodedSample += arrayHandler;
            events.OnVideoSourceEncodedSampleFaster -= spanHandler;
            Assert.True(events.HasSubscribers);
            Assert.False(events.HasSpanSubscribers);

            events.OnVideoSourceEncodedSample -= arrayHandler;
            Assert.False(events.HasSubscribers);
        }
    }
}
