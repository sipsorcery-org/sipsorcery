//-----------------------------------------------------------------------------
// Filename: Vp8NetVideoEncoderEndPointUnitTest.cs
//
// Description: Unit tests for the Vp8NetVideoEncoderEndPoint event that raises
// each encoded frame as a span over a buffer the end point re-uses, next to the
// event that raises a new array per frame.
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
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace Vpx.Net.UnitTest
{
    public class Vp8NetVideoEncoderEndPointUnitTest
    {
        private static readonly Vp8GoldenScenario Scenario = Vp8GoldenScenario.Get("I420_176x144_Gop4_IntraFallback_Forced");

        private static Vp8NetVideoEncoderEndPoint CreateEndPoint() => new Vp8NetVideoEncoderEndPoint
        {
            KeyframeIntervalFrames = Scenario.KeyframeInterval,
            BaseQIndex = Scenario.BaseQIndex,
            EnableIntraFallback = Scenario.IntraFallback,
        };

        private static void Feed(Vp8NetVideoEncoderEndPoint endPoint, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                endPoint.ExternalVideoSourceRawSample(33, Scenario.Width, Scenario.Height, Scenario.CreateFrame(i), VideoPixelFormatsEnum.I420);
            }
        }

        /// <summary>
        /// The frames, with their durations, that the array event raises when it is the only one subscribed, which is
        /// what the end point raised before it had the span event.
        /// </summary>
        private static List<(uint Duration, byte[] Frame)> EncodeWithArrayEventOnly(int frames)
        {
            var encoded = new List<(uint Duration, byte[] Frame)>();
            var endPoint = CreateEndPoint();
            endPoint.OnVideoSourceEncodedSample += (duration, sample) => encoded.Add((duration, sample));
            Feed(endPoint, frames);
            return encoded;
        }

        private static void AssertSameFrames(List<(uint Duration, byte[] Frame)> expected, List<(uint Duration, byte[] Frame)> actual)
        {
            Assert.Equal(expected.ConvertAll(e => e.Duration), actual.ConvertAll(a => a.Duration));
            Assert.Equal(expected.ConvertAll(e => e.Frame), actual.ConvertAll(a => a.Frame));
        }

        /// <summary>
        /// The span event on its own raises exactly the frames, and durations, that the array event raises on its own.
        /// </summary>
        [Fact]
        public void OnVideoSourceEncodedSampleFaster_RaisesTheFramesTheArrayEventRaises()
        {
            var expected = EncodeWithArrayEventOnly(8);

            var actual = new List<(uint Duration, byte[] Frame)>();
            var endPoint = CreateEndPoint();
            endPoint.OnVideoSourceEncodedSampleFaster += (duration, sample) => actual.Add((duration, sample.ToArray()));
            Assert.True(endPoint.HasEncodedVideoSubscribers());
            Feed(endPoint, 8);

            Assert.Equal(8, expected.Count);
            AssertSameFrames(expected, actual);
        }

        /// <summary>
        /// With both events subscribed each frame is encoded once and raised on both, with the frames and durations
        /// the array event raises on its own, the array event getting a copy of its own.
        /// </summary>
        [Fact]
        public void BothEventsSubscribed_EachFrameRaisedOnBoth()
        {
            var expected = EncodeWithArrayEventOnly(6);

            var fromSpan = new List<(uint Duration, byte[] Frame)>();
            var fromArray = new List<(uint Duration, byte[] Frame)>();
            var endPoint = CreateEndPoint();
            endPoint.OnVideoSourceEncodedSampleFaster += (duration, sample) => fromSpan.Add((duration, sample.ToArray()));
            endPoint.OnVideoSourceEncodedSample += (duration, sample) => fromArray.Add((duration, sample));
            Feed(endPoint, 6);

            Assert.Equal(6, expected.Count);
            AssertSameFrames(expected, fromSpan);
            AssertSameFrames(expected, fromArray);
        }

        /// <summary>
        /// Raising frames on the span event does not allocate an array per frame.
        /// </summary>
        [Fact]
        public void OnVideoSourceEncodedSampleFaster_DoesNotAllocateTheFrame()
        {
            long encodedBytes = 0;
            var endPoint = CreateEndPoint();
            endPoint.OnVideoSourceEncodedSampleFaster += (duration, sample) => encodedBytes += sample.Length;

            // Two key frames (one every four frames) and their inter frames let the end point's buffer grow to the
            // largest frame size before measuring.
            var frames = new[] { Scenario.CreateFrame(0), Scenario.CreateFrame(1) };
            for (int i = 0; i < 6; i++)
            {
                endPoint.ExternalVideoSourceRawSample(33, Scenario.Width, Scenario.Height, frames[i & 1], VideoPixelFormatsEnum.I420);
            }

            encodedBytes = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10; i++)
            {
                endPoint.ExternalVideoSourceRawSample(33, Scenario.Width, Scenario.Height, frames[i & 1], VideoPixelFormatsEnum.I420);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // The encoder allocates a few dozen bytes per frame of its own, far less than the frame.
            Assert.True(encodedBytes > 10 * 100);
            Assert.True(allocated < encodedBytes / 10, $"Allocated {allocated} bytes for {encodedBytes} encoded bytes.");
        }
    }
}
