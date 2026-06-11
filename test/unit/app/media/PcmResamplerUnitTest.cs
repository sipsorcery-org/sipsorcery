//-----------------------------------------------------------------------------
// Filename: PcmResamplerUnitTest.cs
//
// Description: Unit tests for the PcmResampler. Accessed via the public
// AudioEncoder.Resample wrapper.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 11 Jun 2026  Aaron Clauson   Created, Dublin, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.UnitTests;
using Xunit;

#pragma warning disable CS0618 // AudioEncoder.Resample is the public access point for the internal resampler.

namespace SIPSorcery.Media.UnitTests
{
    [Trait("Category", "unit")]
    public class PcmResamplerUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public PcmResamplerUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        [Theory]
        [InlineData(8000, 16000)]
        [InlineData(8000, 48000)]
        [InlineData(16000, 8000)]
        [InlineData(16000, 48000)]
        [InlineData(48000, 8000)]
        [InlineData(48000, 16000)]
        [InlineData(44100, 48000)]
        public void ResampleProducesCorrectDuration(int inRate, int outRate)
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var encoder = new AudioEncoder();
            short[] pcm = new short[inRate / 50]; // 20ms.

            var resampled = encoder.Resample(pcm, inRate, outRate);

            Assert.Equal(outRate / 50, resampled.Length);
        }

        /// <summary>
        /// Upsampling a linear ramp must interpolate intermediate values rather than repeat
        /// samples. Sample repetition mirrors the source spectrum at multiples of the input
        /// rate, which wideband codecs (e.g. OPUS at 48KHz) transmit faithfully and which is
        /// audible as harsh, metallic artifacts.
        /// </summary>
        [Fact]
        public void UpsampleInterpolatesBetweenSamples()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var encoder = new AudioEncoder();
            short[] ramp = Enumerable.Range(0, 160).Select(x => (short)(x * 60)).ToArray(); // 20ms of 8KHz ramp.

            var resampled = encoder.Resample(ramp, 8000, 48000);

            Assert.Equal(960, resampled.Length);

            // With linear interpolation each output step on a ramp is one sixth of the input
            // step. Sample repetition produces runs of six identical values instead.
            for (int i = 1; i < 12; i++)
            {
                Assert.Equal(10, resampled[i] - resampled[i - 1]);
            }
        }

        [Fact]
        public void ResampleSameRateReturnsInput()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var encoder = new AudioEncoder();
            short[] pcm = { 1, 2, 3, 4 };

            var resampled = encoder.Resample(pcm, 8000, 8000);

            Assert.Same(pcm, resampled);
        }
    }
}
