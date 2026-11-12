//-----------------------------------------------------------------------------
// Filename: FFmpegVideoEndPointUnitTest.cs
//
// Description: Unit tests for the FFmpegVideoEndPoint event that raises each
// encoded frame as a span over a buffer the end point re-uses, next to the
// event that raises a new array per frame.
//
// The tests encode with the native FFmpeg libraries and are skipped when those
// cannot be loaded, see FFmpegAvailability.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 13 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorceryMedia.FFmpeg.UnitTest
{
    public class FFmpegVideoEndPointUnitTest
    {
        private const int WIDTH = 320;
        private const int HEIGHT = 240;
        private const uint DURATION_MILLISECONDS = 33;

        /// <summary>
        /// The two ways an application hands the end point a raw frame.
        /// </summary>
        public enum Input
        {
            /// <summary>ExternalVideoSourceRawSample with the frame in an array.</summary>
            Array,

            /// <summary>ExternalVideoSourceRawSampleFaster with a RawImage pointing at the frame.</summary>
            RawImage,
        }

        /// <summary>
        /// A raw frame in an array that never moves, so a RawImage can point at it for as long as the test runs.
        /// </summary>
        private sealed class PinnedFrame
        {
            public PinnedFrame(byte[] sample, VideoPixelFormatsEnum pixelFormat)
            {
                Sample = GC.AllocateArray<byte>(sample.Length, pinned: true);
                sample.CopyTo(Sample, 0);
                Image = new RawImage
                {
                    Width = WIDTH,
                    Height = HEIGHT,
                    Stride = pixelFormat == VideoPixelFormatsEnum.I420 ? WIDTH : sample.Length / HEIGHT,
                    Sample = Marshal.UnsafeAddrOfPinnedArrayElement(Sample, 0),
                    PixelFormat = pixelFormat,
                };
            }

            public byte[] Sample { get; }

            public RawImage Image { get; }
        }

        private static PinnedFrame[] CreateFrames(int count, VideoPixelFormatsEnum pixelFormat = VideoPixelFormatsEnum.I420) =>
            Enumerable.Range(0, count).Select(i => new PinnedFrame(TestFrames.Create(WIDTH, HEIGHT, pixelFormat, i), pixelFormat)).ToArray();

        private static FFmpegVideoEndPoint CreateEndPoint(VideoCodecsEnum codec, Dictionary<string, string> encoderOptions = null)
        {
            var endPoint = new FFmpegVideoEndPoint(encoderOptions);
            endPoint.SetVideoSourceFormat(endPoint.GetVideoSourceFormats().First(f => f.Codec == codec));
            return endPoint;
        }

        private static void Push(FFmpegVideoEndPoint endPoint, PinnedFrame frame, Input input)
        {
            if (input == Input.Array)
            {
                endPoint.ExternalVideoSourceRawSample(DURATION_MILLISECONDS, WIDTH, HEIGHT, frame.Sample, frame.Image.PixelFormat);
            }
            else
            {
                endPoint.ExternalVideoSourceRawSampleFaster(DURATION_MILLISECONDS, frame.Image);
            }
        }

        /// <summary>
        /// The frames, with their durations, that the array event raises when it is the only one subscribed and the
        /// frames are passed as arrays, which is what the end point raised before it had the span event.
        /// </summary>
        private static List<(uint Duration, byte[] Frame)> EncodeWithArrayEventOnly(PinnedFrame[] frames)
        {
            var encoded = new List<(uint Duration, byte[] Frame)>();
            using (var endPoint = CreateEndPoint(VideoCodecsEnum.H264))
            {
                endPoint.OnVideoSourceEncodedSample += (duration, sample) => encoded.Add((duration, sample));
                foreach (var frame in frames)
                {
                    Push(endPoint, frame, Input.Array);
                }
            }
            return encoded;
        }

        private static void AssertSameFrames(List<(uint Duration, byte[] Frame)> expected, List<(uint Duration, byte[] Frame)> actual)
        {
            Assert.Equal(expected.Select(e => e.Duration), actual.Select(a => a.Duration));
            Assert.Equal(expected.Select(e => e.Frame), actual.Select(a => a.Frame));
        }

        /// <summary>
        /// With both events subscribed each frame is encoded once and raised on both, with the same bytes and the same
        /// duration, the array event getting a copy of its own.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [InlineData(VideoCodecsEnum.H264, Input.Array)]
        [InlineData(VideoCodecsEnum.H264, Input.RawImage)]
        [InlineData(VideoCodecsEnum.VP8, Input.Array)]
        [InlineData(VideoCodecsEnum.VP8, Input.RawImage)]
        public void BothEventsSubscribed_EachFrameRaisedOnBothWithTheSameBytes(VideoCodecsEnum codec, Input input)
        {
            var frames = CreateFrames(8);
            using (var endPoint = CreateEndPoint(codec))
            {
                var fromSpan = new List<(uint Duration, byte[] Frame)>();
                var fromArray = new List<(uint Duration, byte[] Frame)>();
                endPoint.OnVideoSourceEncodedSampleFaster += (duration, sample) => fromSpan.Add((duration, sample.ToArray()));
                endPoint.OnVideoSourceEncodedSample += (duration, sample) => fromArray.Add((duration, sample));

                foreach (var frame in frames)
                {
                    Push(endPoint, frame, input);
                }

                Assert.Equal(frames.Length, fromArray.Count);
                AssertSameFrames(fromArray, fromSpan);
                Assert.All(fromArray, f => Assert.Equal(90000u * DURATION_MILLISECONDS / 1000, f.Duration));
            }
        }

        /// <summary>
        /// The span event on its own raises exactly the frames, and durations, that the array event raises on its own.
        /// libx264 produces the same frames for the same input and options, so two end points can be compared.
        /// </summary>
        [FFmpegTheory("libx264")]
        [InlineData(Input.Array)]
        [InlineData(Input.RawImage)]
        public void OnlySpanEventSubscribed_RaisesTheFramesTheArrayEventRaises(Input input)
        {
            var frames = CreateFrames(8);
            var expected = EncodeWithArrayEventOnly(frames);

            var actual = new List<(uint Duration, byte[] Frame)>();
            using (var endPoint = CreateEndPoint(VideoCodecsEnum.H264))
            {
                endPoint.OnVideoSourceEncodedSampleFaster += (duration, sample) => actual.Add((duration, sample.ToArray()));
                Assert.True(endPoint.HasEncodedVideoSubscribers());

                foreach (var frame in frames)
                {
                    Push(endPoint, frame, input);
                }
            }

            Assert.Equal(frames.Length, expected.Count);
            AssertSameFrames(expected, actual);
        }

        /// <summary>
        /// An encoder that holds frames back (libx264 with rate control look-ahead) raises nothing for the frames it
        /// keeps, and raises them once it produces them.
        /// </summary>
        [FFmpegTheory("libx264")]
        [InlineData(Input.Array)]
        [InlineData(Input.RawImage)]
        public void EncoderHoldsFramesBack_RaisesNothingUntilItProducesAFrame(Input input)
        {
            var frames = CreateFrames(25);
            var lookAhead = new Dictionary<string, string> { ["tune"] = "film", ["x264-params"] = "rc-lookahead=4" };
            using (var endPoint = CreateEndPoint(VideoCodecsEnum.H264, lookAhead))
            {
                var raised = new List<int>();
                endPoint.OnVideoSourceEncodedSampleFaster += (duration, sample) => raised.Add(sample.Length);

                Push(endPoint, frames[0], input);
                Assert.Empty(raised);

                foreach (var frame in frames.Skip(1))
                {
                    Push(endPoint, frame, input);
                }

                Assert.NotEmpty(raised);
                Assert.All(raised, length => Assert.True(length > 0));
            }
        }

        /// <summary>
        /// A failed encode is reported on OnVideoSourceError, as it is when only the array event is subscribed, and
        /// nothing is raised. The encoder has no FFmpeg pixel format for Rgba.
        /// </summary>
        [FFmpegTheory("libx264")]
        [InlineData(Input.Array)]
        [InlineData(Input.RawImage)]
        public void EncodeFails_ReportedOnVideoSourceError(Input input)
        {
            var frame = new PinnedFrame(new byte[WIDTH * HEIGHT * 4], VideoPixelFormatsEnum.Rgba);
            using (var endPoint = CreateEndPoint(VideoCodecsEnum.H264))
            {
                string error = null;
                int raised = 0;
                endPoint.OnVideoSourceError += message => error = message;
                endPoint.OnVideoSourceEncodedSampleFaster += (duration, sample) => raised++;

                Push(endPoint, frame, input);

                Assert.NotNull(error);
                Assert.Contains("Rgba", error);
                Assert.Equal(0, raised);
            }
        }

        /// <summary>
        /// With only the span event subscribed, encoding and raising a frame allocates nothing per frame, whether the
        /// encoder reads the frame directly (I420) or converts it first (BGR).
        /// </summary>
        [FFmpegTheory("libx264")]
        [InlineData(VideoPixelFormatsEnum.I420, Input.Array)]
        [InlineData(VideoPixelFormatsEnum.I420, Input.RawImage)]
        [InlineData(VideoPixelFormatsEnum.Bgr, Input.Array)]
        [InlineData(VideoPixelFormatsEnum.Bgr, Input.RawImage)]
        public void OnlySpanEventSubscribed_DoesNotAllocatePerFrame(VideoPixelFormatsEnum pixelFormat, Input input)
        {
            var frames = CreateFrames(2, pixelFormat);
            using (var endPoint = CreateEndPoint(VideoCodecsEnum.H264))
            {
                long encodedBytes = 0;
                endPoint.OnVideoSourceEncodedSampleFaster += (duration, sample) => encodedBytes += sample.Length;

                // The encoder makes a key frame every 30 frames. Running past the second one lets the end point's
                // buffer grow to the key frame size, so none of the inter frames measured below need it to grow.
                for (int i = 0; i < 32; i++)
                {
                    Push(endPoint, frames[i & 1], input);
                }

                const int count = 24;
                encodedBytes = 0;
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < count; i++)
                {
                    Push(endPoint, frames[i & 1], input);
                }
                long allocatedPerFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / count;

                Assert.True(encodedBytes > 0);
                Assert.True(allocatedPerFrame < 64, $"Allocated {allocatedPerFrame} bytes per frame.");
            }
        }
    }
}
