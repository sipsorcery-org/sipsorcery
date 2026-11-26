//-----------------------------------------------------------------------------
// Filename: FFmpegVideoEncoderLatencyUnitTest.cs
//
// Description: Unit tests for the real-time defaults FFmpegVideoEncoder gives
// the libvpx encoders: each frame given to the encoder comes straight back out
// encoded, rather than being held back for look-ahead, which at 30 fps would
// add the better part of a second of latency to every frame.
//
// The tests encode with the native FFmpeg libraries and are skipped when those
// cannot be loaded, see FFmpegAvailability.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 26 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorceryMedia.FFmpeg.UnitTest
{
    public class FFmpegVideoEncoderLatencyUnitTest
    {
        private const int WIDTH = 320;
        private const int HEIGHT = 240;
        private const int FRAMES = 30;

        private static FFmpegVideoEncoder CreateEncoder(VideoCodecsEnum codec, string encoderName, Dictionary<string, string> options = null)
        {
            var encoder = new FFmpegVideoEncoder(options);
            Assert.True(encoder.SetCodec(FFmpegConvert.GetAVCodecID(codec).Value, encoderName));
            return encoder;
        }

        private static List<byte[]> Encode(FFmpegVideoEncoder encoder, VideoCodecsEnum codec, int frames = FRAMES)
        {
            var encoded = new List<byte[]>();
            for (int i = 0; i < frames; i++)
            {
                encoded.Add(encoder.EncodeVideo(WIDTH, HEIGHT, TestFrames.Create(WIDTH, HEIGHT, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, codec));
            }
            return encoded;
        }

        /// <summary>
        /// With the default options every frame is returned encoded by the call that gave it to the encoder, from the
        /// first frame on. libvpx-vp9 holds 25 frames back for look-ahead unless lag-in-frames is set, even with the
        /// realtime deadline, so without the default the first 24 calls return nothing.
        /// </summary>
        [FFmpegTheory("libvpx", "libvpx-vp9")]
        [InlineData(VideoCodecsEnum.VP8, "libvpx")]
        [InlineData(VideoCodecsEnum.VP9, "libvpx-vp9")]
        public void EncodeVideo_RealtimeDefaults_EveryCallReturnsItsFrame(VideoCodecsEnum codec, string encoderName)
        {
            using (var encoder = CreateEncoder(codec, encoderName))
            {
                var encoded = Encode(encoder, codec);

                for (int i = 0; i < encoded.Count; i++)
                {
                    Assert.True(encoded[i] != null && encoded[i].Length > 0, $"{encoderName} returned no frame for call {i}.");
                }
            }
        }

        /// <summary>
        /// The caller's encoder options are applied after the real-time defaults, so a caller that wants libvpx-vp9's
        /// look-ahead can still ask for it: the first frames are then held back and come out later.
        /// </summary>
        [FFmpegFact("libvpx-vp9")]
        public void EncodeVideo_Vp9CallerSetsLagInFrames_OverridesDefault()
        {
            const int lag = 5;
            var options = new Dictionary<string, string> { ["lag-in-frames"] = lag.ToString() };

            using (var encoder = CreateEncoder(VideoCodecsEnum.VP9, "libvpx-vp9", options))
            {
                var encoded = Encode(encoder, VideoCodecsEnum.VP9);

                Assert.Null(encoded[0]);
                Assert.NotNull(encoded[FRAMES - 1]);
            }
        }
    }
}
