//-----------------------------------------------------------------------------
// Filename: FFmpegVideoEncoderResizeUnitTest.cs
//
// Description: Unit tests for FFmpegVideoEncoder following a change of frame
// size part way through a stream, as a browser's video does when it starts at
// a low resolution and steps up once the bandwidth allows. Each test encodes
// frames at one size and then others, decodes what comes out and checks the
// decoded size follows the size of the frames given to the encoder.
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
using System.Linq;
using FFmpeg.AutoGen;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorceryMedia.FFmpeg.UnitTest
{
    public unsafe class FFmpegVideoEncoderResizeUnitTest
    {
        private const int FRAMES_PER_SIZE = 4;

        /// <summary>
        /// The sizes a stream steps through: up from the low resolution a browser starts at to 720p, as measured with
        /// a browser, then down to a size with another aspect ratio.
        /// </summary>
        private static readonly (int Width, int Height)[] _sizes = { (640, 360), (1280, 720), (320, 240) };

        public static IEnumerable<object[]> CodecsAndFormats()
        {
            foreach (var codec in new[] { VideoCodecsEnum.H264, VideoCodecsEnum.VP8 })
            {
                // I420 goes to the encoder as it is; Bgr goes through the encoder's pixel format converter, which
                // has to follow the size change as well.
                foreach (var format in new[] { VideoPixelFormatsEnum.I420, VideoPixelFormatsEnum.Bgr })
                {
                    yield return new object[] { codec, format };
                }
            }
        }

        public static IEnumerable<object[]> Codecs()
        {
            yield return new object[] { VideoCodecsEnum.H264 };
            yield return new object[] { VideoCodecsEnum.VP8 };
        }

        private static IEnumerable<(int Width, int Height, int Index, bool FirstAtSize)> Sequence()
        {
            int index = 0;
            foreach (var size in _sizes)
            {
                for (int i = 0; i < FRAMES_PER_SIZE; i++)
                {
                    yield return (size.Width, size.Height, index++, i == 0);
                }
            }
        }

        private static bool IsKeyFrame(VideoCodecsEnum codec, byte[] encoded) =>
            codec == VideoCodecsEnum.H264 ? EncodedFrames.H264HasIdr(encoded) : EncodedFrames.Vp8IsKeyFrame(encoded);

        /// <summary>
        /// Decodes one encoded frame and checks it comes out whole at the size it was given to the encoder at.
        /// </summary>
        private static void AssertDecodesAtSize(FFmpegVideoEncoder decoder, VideoCodecsEnum codec, byte[] encoded, int width, int height, int index)
        {
            Assert.True(encoded != null && encoded.Length > 0, $"Frame {index} ({width}x{height}) was not encoded.");

            var decoded = decoder.DecodeVideoFaster(encoded, VideoPixelFormatsEnum.Bgr, codec).ToList();

            Assert.True(decoded.Count == 1, $"Frame {index} ({width}x{height}) decoded to {decoded.Count} frames.");
            Assert.True(decoded[0].Width == width && decoded[0].Height == height,
                $"Frame {index} was encoded at {width}x{height} but decoded at {decoded[0].Width}x{decoded[0].Height}.");
        }

        /// <summary>
        /// Raw frames given to EncodeVideo at a new size are encoded at that size, the first of them as a key frame,
        /// whether they go to the encoder as they are (I420) or through its pixel format converter (Bgr).
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [MemberData(nameof(CodecsAndFormats))]
        public void EncodeVideo_FrameSizeChanges_DecodedSizeFollows(VideoCodecsEnum codec, VideoPixelFormatsEnum format)
        {
            using (var encoder = new FFmpegVideoEncoder())
            using (var decoder = new FFmpegVideoEncoder())
            {
                foreach (var (width, height, index, firstAtSize) in Sequence())
                {
                    byte[] encoded = encoder.EncodeVideo(width, height, TestFrames.Create(width, height, format, index), format, codec);

                    AssertDecodesAtSize(decoder, codec, encoded, width, height, index);

                    if (firstAtSize)
                    {
                        Assert.True(IsKeyFrame(codec, encoded), $"Frame {index}, the first at {width}x{height}, is not a key frame.");
                    }
                }
            }
        }

        /// <summary>
        /// The same for frames given to the encoder as an AVFrame, the path FFmpegVideoSource encodes through.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [MemberData(nameof(Codecs))]
        public void Encode_AVFrame_FrameSizeChanges_DecodedSizeFollows(VideoCodecsEnum codec)
        {
            AVCodecID codecID = FFmpegConvert.GetAVCodecID(codec).Value;

            using (var encoder = new FFmpegVideoEncoder())
            using (var decoder = new FFmpegVideoEncoder())
            {
                foreach (var (width, height, index, firstAtSize) in Sequence())
                {
                    byte[] sample = TestFrames.Create(width, height, VideoPixelFormatsEnum.I420, index);
                    byte[] encoded;

                    fixed (byte* pSample = sample)
                    {
                        AVFrame frame = encoder.MakeFrame(pSample, width, height);
                        encoded = encoder.Encode(codecID, &frame, 30);
                    }

                    AssertDecodesAtSize(decoder, codec, encoded, width, height, index);

                    if (firstAtSize)
                    {
                        Assert.True(IsKeyFrame(codec, encoded), $"Frame {index}, the first at {width}x{height}, is not a key frame.");
                    }
                }
            }
        }

        /// <summary>
        /// The encoder keeps its configured bitrate, frame rate and codec options when it is reopened at a new size:
        /// an encoder that is resized part way through produces, at the new size, exactly the frames of an encoder
        /// configured the same way that started at that size.
        /// </summary>
        [FFmpegTheory("libx264", "libvpx")]
        [MemberData(nameof(Codecs))]
        public void EncodeVideo_FrameSizeChanges_KeepsConfiguration(VideoCodecsEnum codec)
        {
            const int width = 640, height = 360;
            const int newWidth = 1280, newHeight = 720;

            FFmpegVideoEncoder CreateEncoder()
            {
                // A fixed speed for libvpx, so the two encoders make the same choices: a positive cpu-used in realtime
                // mode means libvpx picks the speed from how long frames take to encode. A constant quality is a
                // codec option that visibly changes the output, so losing it on the reopen would show.
                var options = codec == VideoCodecsEnum.VP8
                    ? new Dictionary<string, string> { ["cpu-used"] = "-5", ["crf"] = "10" }
                    : new Dictionary<string, string> { ["crf"] = "18" };

                var encoder = new FFmpegVideoEncoder(options);
                encoder.SetThreadCount(1);
                encoder.SetBitrate(600_000, null, null, null);
                return encoder;
            }

            var expected = new List<byte[]>();
            using (var reference = CreateEncoder())
            {
                for (int i = 0; i < FRAMES_PER_SIZE; i++)
                {
                    expected.Add(reference.EncodeVideo(newWidth, newHeight, TestFrames.Create(newWidth, newHeight, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, codec));
                }
            }

            using (var encoder = CreateEncoder())
            {
                for (int i = 0; i < FRAMES_PER_SIZE; i++)
                {
                    Assert.NotNull(encoder.EncodeVideo(width, height, TestFrames.Create(width, height, VideoPixelFormatsEnum.I420, 100 + i), VideoPixelFormatsEnum.I420, codec));
                }

                for (int i = 0; i < FRAMES_PER_SIZE; i++)
                {
                    byte[] encoded = encoder.EncodeVideo(newWidth, newHeight, TestFrames.Create(newWidth, newHeight, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, codec);
                    Assert.True(expected[i].SequenceEqual(encoded), $"Frame {i} after the resize differs from the frame an encoder started at {newWidth}x{newHeight} produced.");
                }
            }
        }

        /// <summary>
        /// One instance that decodes a stream and encodes another, as FFmpegVideoEndPoint uses it, keeps decoding when
        /// the encoder is reopened at a new size or for a new bitrate: reopening the encoder leaves the decoder's
        /// state alone.
        /// </summary>
        [FFmpegFact("libvpx")]
        public void EncodeVideo_FrameSizeChanges_SameInstanceKeepsDecoding()
        {
            const int incomingWidth = 320, incomingHeight = 240;
            const int frames = 12;

            var incoming = new List<byte[]>();
            using (var sender = new FFmpegVideoEncoder())
            {
                for (int i = 0; i < frames; i++)
                {
                    incoming.Add(sender.EncodeVideo(incomingWidth, incomingHeight, TestFrames.Create(incomingWidth, incomingHeight, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8));
                }
            }

            using (var transcoder = new FFmpegVideoEncoder())
            using (var decoder = new FFmpegVideoEncoder())
            {
                for (int i = 0; i < frames; i++)
                {
                    var decoded = transcoder.DecodeVideoFaster(incoming[i], VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.VP8).ToList();
                    Assert.True(decoded.Count == 1, $"Incoming frame {i} decoded to {decoded.Count} frames.");
                    Assert.Equal(incomingWidth, decoded[0].Width);
                    Assert.Equal(incomingHeight, decoded[0].Height);

                    if (i == 8)
                    {
                        transcoder.SetBitrate(500_000, null, null, null);
                    }

                    var (width, height) = i < 4 ? _sizes[0] : _sizes[1];
                    byte[] encoded = transcoder.EncodeVideo(width, height, TestFrames.Create(width, height, VideoPixelFormatsEnum.I420, i), VideoPixelFormatsEnum.I420, VideoCodecsEnum.VP8);

                    AssertDecodesAtSize(decoder, VideoCodecsEnum.VP8, encoded, width, height, i);
                }
            }
        }
    }
}
