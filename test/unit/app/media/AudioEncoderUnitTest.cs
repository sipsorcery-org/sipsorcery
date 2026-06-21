//-----------------------------------------------------------------------------
// Filename: AudioEncoderUnitTest.cs
//
// Description: Unit tests for the AudioEncoder class, in particular the OPUS
// encode path. The OPUS SDP format is always "opus/48000/2" (RFC 7587) but
// that channel count is a fixed wire-format declaration and does not describe
// the encoded stream, which is signalled in-band per packet. The library's
// PCM convention is mono, so the encoder must produce mono packets: a stereo
// configured encoder fed mono PCM consumes every two samples as one stereo
// frame, halving the duration and doubling the pitch of the audio.
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
using Concentus;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.UnitTests;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Media.UnitTests
{
    [Trait("Category", "unit")]
    public class AudioEncoderUnitTest
    {
        private const int OPUS_CLOCK_RATE = 48000;
        private const int FRAME_DURATION_MILLISECONDS = 20;
        private const int SAMPLES_PER_FRAME = OPUS_CLOCK_RATE / 1000 * FRAME_DURATION_MILLISECONDS; // 960 mono samples.

        private Microsoft.Extensions.Logging.ILogger logger = null;

        public AudioEncoderUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static short[] CreateSineWavePcm(int sampleCount, int sampleRate, double frequencyHz)
        {
            var pcm = new short[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                pcm[i] = (short)(Math.Sin(2.0 * Math.PI * frequencyHz * i / sampleRate) * short.MaxValue * 0.5);
            }
            return pcm;
        }

        /// <summary>
        /// Encodes 20ms of mono PCM and decodes it with an INDEPENDENT decoder, which is what a
        /// remote party (browser, SFU, SIP bridge) does. The decoded duration must match the
        /// input duration. A stereo configured encoder fed mono PCM produces a 10ms packet from
        /// 20ms of input, which a remote decoder plays back time-compressed and pitch-doubled;
        /// a round trip through the same matching mis-configured encoder/decoder pair masks the
        /// fault, which is why this test must NOT reuse the AudioEncoder for the decode.
        /// </summary>
        [Fact]
        public void OpusEncodePreservesDurationUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var encoder = new AudioEncoder(includeOpus: true);
            var pcm = CreateSineWavePcm(SAMPLES_PER_FRAME, OPUS_CLOCK_RATE, 440);

            byte[] encoded = encoder.EncodeAudio(pcm, AudioCommonlyUsedFormats.OpusWebRTC);

            Assert.NotNull(encoded);
            Assert.True(encoded.Length > 0, "The OPUS encode produced an empty packet.");

            // Decode with a fresh, correctly configured mono decoder, equivalent to what the
            // remote party does.
            var independentDecoder = OpusCodecFactory.CreateDecoder(OPUS_CLOCK_RATE, 1);
            var decodedPcm = new float[SAMPLES_PER_FRAME * 2];
            int samplesPerChannel = independentDecoder.Decode(encoded, decodedPcm, decodedPcm.Length, false);

            Assert.Equal(SAMPLES_PER_FRAME, samplesPerChannel);
        }

        /// <summary>
        /// The decode path must honour the library's mono PCM convention: decoding a mono OPUS
        /// packet must return one sample per source sample, not stereo upmixed interleaved PCM
        /// which would double the apparent duration for downstream mono consumers (resampler,
        /// audio sinks).
        /// </summary>
        [Fact]
        public void OpusDecodeReturnsMonoPcmUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var encoder = new AudioEncoder(includeOpus: true);
            var decoder = new AudioEncoder(includeOpus: true);
            var pcm = CreateSineWavePcm(SAMPLES_PER_FRAME, OPUS_CLOCK_RATE, 440);

            byte[] encoded = encoder.EncodeAudio(pcm, AudioCommonlyUsedFormats.OpusWebRTC);
            short[] decoded = decoder.DecodeAudio(encoded, AudioCommonlyUsedFormats.OpusWebRTC);

            Assert.Equal(SAMPLES_PER_FRAME, decoded.Length);
        }

        /// <summary>
        /// Sanity check that the encoded audio is a faithful rendition and not time-compressed:
        /// a 440Hz tone must come back with approximately the same number of zero crossings as
        /// went in. The stereo mis-configuration doubled the apparent frequency.
        /// </summary>
        [Fact]
        public void OpusRoundTripPreservesPitchUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            var encoder = new AudioEncoder(includeOpus: true);
            var pcm = CreateSineWavePcm(SAMPLES_PER_FRAME, OPUS_CLOCK_RATE, 440);

            byte[] encoded = encoder.EncodeAudio(pcm, AudioCommonlyUsedFormats.OpusWebRTC);

            var independentDecoder = OpusCodecFactory.CreateDecoder(OPUS_CLOCK_RATE, 1);
            var decodedPcm = new float[SAMPLES_PER_FRAME * 2];
            int samplesPerChannel = independentDecoder.Decode(encoded, decodedPcm, decodedPcm.Length, false);

            int ZeroCrossings(Func<int, float> sample, int count)
            {
                int crossings = 0;
                for (int i = 1; i < count; i++)
                {
                    if ((sample(i - 1) < 0 && sample(i) >= 0) || (sample(i - 1) >= 0 && sample(i) < 0))
                    {
                        crossings++;
                    }
                }
                return crossings;
            }

            int inputCrossings = ZeroCrossings(i => pcm[i], pcm.Length);
            int outputCrossings = ZeroCrossings(i => decodedPcm[i], samplesPerChannel);

            logger.LogDebug("Zero crossings input {Input}, output {Output}.", inputCrossings, outputCrossings);

            // The codec start-up transient means the first frame is not bit-faithful, so allow a
            // generous tolerance. The mis-configuration error mode was a factor of 2.
            Assert.InRange(outputCrossings, inputCrossings / 2 + 1, inputCrossings * 3 / 2);
        }

        /// <summary>
        /// A remote peer is free to send genuinely stereo OPUS packets (the SDP is always
        /// "opus/48000/2" and the stream channel count is signalled in-band, RFC 7587). The
        /// decoder must downmix such packets to mono to honour the library's PCM convention,
        /// which the OPUS decoder API supports natively (RFC 6716 requires decoding any packet
        /// to the configured channel count). Pins the receive path against peers such as the
        /// OpenAI realtime endpoint.
        /// </summary>
        [Fact]
        public void OpusDecodeOfStereoStreamDownmixesToMonoUnitTest()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());
            logger.BeginScope(TestHelper.GetCurrentMethodName());

            // Build a genuine stereo packet with an independent stereo encoder.
            var stereoEncoder = OpusCodecFactory.CreateEncoder(OPUS_CLOCK_RATE, 2, Concentus.Enums.OpusApplication.OPUS_APPLICATION_VOIP);
            var monoPcm = CreateSineWavePcm(SAMPLES_PER_FRAME, OPUS_CLOCK_RATE, 440);
            var stereoPcm = new short[SAMPLES_PER_FRAME * 2];
            for (int i = 0; i < SAMPLES_PER_FRAME; i++)
            {
                stereoPcm[i * 2] = monoPcm[i];
                stereoPcm[i * 2 + 1] = monoPcm[i];
            }

            var packet = new byte[1275];
            int encodedLength = stereoEncoder.Encode(stereoPcm, SAMPLES_PER_FRAME, packet, packet.Length);
            Assert.True(encodedLength > 0, "The stereo OPUS encode produced an empty packet.");

            var decoder = new AudioEncoder(includeOpus: true);
            short[] decoded = decoder.DecodeAudio(packet.AsSpan(0, encodedLength).ToArray(), AudioCommonlyUsedFormats.OpusWebRTC);

            // The stereo packet must come back as 20ms of MONO PCM.
            Assert.Equal(SAMPLES_PER_FRAME, decoded.Length);
        }
    }
}
