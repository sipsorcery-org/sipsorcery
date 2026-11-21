//-----------------------------------------------------------------------------
// Filename: AV1VideoEncoderUnitTest.cs
//
// Description: Unit tests for the AV1 video encoder: its bitstream against
// av1rt's own encoder, colour signalling, frame timing, and the encoder in a
// SIPSorcery video source.
//
// Author(s):
// SIPSorcery contributors
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Av1rt;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.AV1.UnitTest;

[Trait("Category", "unit")]
public class AV1VideoEncoderUnitTest
{
    private static byte[] TestPicture(VideoPixelFormatsEnum format, int stride, int w, int h, int seed)
    {
        var src = new byte[I420Converter.SourceSize(format, stride, w, h)];
        for (int i = 0; i < src.Length; i++)
        {
            src[i] = (byte)((i * 7 + seed * 13) ^ (i >> 9));
        }
        return src;
    }

    // The encoder (convert into av1rt's buffer, encode in place) must produce what encoding the
    // reference I420 with av1rt's Av1Encoder produces.
    [Theory]
    [InlineData(VideoPixelFormatsEnum.I420)]
    [InlineData(VideoPixelFormatsEnum.Bgra)]
    [InlineData(VideoPixelFormatsEnum.NV12)]
    public void BitstreamMatchesAv1rt(VideoPixelFormatsEnum format)
    {
        const int w = 194, h = 130;
        int stride = I420Converter.PackedStride(format, w);
        // With colour signalling, which the encoder must pass on to the av1rt encoder it creates.
        using var encoder = new AV1VideoEncoder(new Av1EncoderOptions
        {
            Threads = 2, MatrixCoefficients = Av1MatrixCoefficients.Bt709, FullRange = true,
        });
        using var direct = new Av1Encoder(new Av1EncoderOptions
        {
            Width = w, Height = h, Threads = 2, MatrixCoefficients = Av1MatrixCoefficients.Bt709, FullRange = true,
        });
        RgbToYuvWeights weights = I420ConverterUnitTest.Weights(Av1MatrixCoefficients.Bt709, true);
        var output = new ArrayBufferWriter<byte>();
        for (int t = 0; t < 6; t++)
        {
            byte[] src = TestPicture(format, stride, w, h, t);
            output.ResetWrittenCount();
            Av1EncodeResult r = direct.Encode(I420ConverterUnitTest.Reference(src, stride, format, w, h, weights), output);
            byte[]? want = r.Dropped ? null : output.WrittenSpan.ToArray();
            // The IVideoEncoder overload times frames by their arrival; this one, without a
            // timestamp, at the nominal rate like `direct`.
            output.ResetWrittenCount();
            byte[]? got = encoder.EncodeVideo(w, h, src, 0, format, output).Dropped ? null : output.WrittenSpan.ToArray();
            Assert.Equal(want, got);
        }
    }

    // RGB input is converted with the matrix and range the stream signals, which is BT.709 for
    // whatever the options leave unspecified; I420 input is passed through and signalled as the
    // options say.
    [Theory]
    [InlineData(VideoPixelFormatsEnum.Bgra, Av1ColorPrimaries.Unspecified, Av1MatrixCoefficients.Unspecified, false,
                Av1ColorPrimaries.Bt709, Av1TransferCharacteristics.Bt709, Av1MatrixCoefficients.Bt709)]
    [InlineData(VideoPixelFormatsEnum.Bgr, Av1ColorPrimaries.Unspecified, Av1MatrixCoefficients.Unspecified, true,
                Av1ColorPrimaries.Bt709, Av1TransferCharacteristics.Bt709, Av1MatrixCoefficients.Bt709)]
    [InlineData(VideoPixelFormatsEnum.Bgra, Av1ColorPrimaries.Unspecified, Av1MatrixCoefficients.Bt601, false,
                Av1ColorPrimaries.Unspecified, Av1TransferCharacteristics.Unspecified, Av1MatrixCoefficients.Bt601)]
    [InlineData(VideoPixelFormatsEnum.Rgba, Av1ColorPrimaries.Bt2020, Av1MatrixCoefficients.Bt2020Ncl, true,
                Av1ColorPrimaries.Bt2020, Av1TransferCharacteristics.Unspecified, Av1MatrixCoefficients.Bt2020Ncl)]
    [InlineData(VideoPixelFormatsEnum.Bgra, Av1ColorPrimaries.Bt709, Av1MatrixCoefficients.Unspecified, false,
                Av1ColorPrimaries.Bt709, Av1TransferCharacteristics.Unspecified, Av1MatrixCoefficients.Bt709)]
    [InlineData(VideoPixelFormatsEnum.I420, Av1ColorPrimaries.Unspecified, Av1MatrixCoefficients.Unspecified, false,
                Av1ColorPrimaries.Unspecified, Av1TransferCharacteristics.Unspecified, Av1MatrixCoefficients.Unspecified)]
    public void ConvertsWithTheColourItSignals(VideoPixelFormatsEnum format, Av1ColorPrimaries primaries, Av1MatrixCoefficients matrix,
                                               bool fullRange, Av1ColorPrimaries signalledPrimaries,
                                               Av1TransferCharacteristics signalledTransfer, Av1MatrixCoefficients signalledMatrix)
    {
        const int w = 66, h = 40;
        int stride = I420Converter.PackedStride(format, w);
        using var encoder = new AV1VideoEncoder(new Av1EncoderOptions
        {
            Threads = 1, ColorPrimaries = primaries, MatrixCoefficients = matrix, FullRange = fullRange,
        });
        using var direct = new Av1Encoder(new Av1EncoderOptions
        {
            Width = w, Height = h, Threads = 1, ColorPrimaries = signalledPrimaries, TransferCharacteristics = signalledTransfer,
            MatrixCoefficients = signalledMatrix, FullRange = fullRange,
        });
        RgbToYuvWeights weights = I420Converter.IsRgb(format) ? I420ConverterUnitTest.Weights(signalledMatrix, fullRange) : default;
        byte[] src = TestPicture(format, stride, w, h, 0);
        var output = new ArrayBufferWriter<byte>();
        direct.Encode(I420ConverterUnitTest.Reference(src, stride, format, w, h, weights), output);
        byte[] want = output.WrittenSpan.ToArray();
        output.ResetWrittenCount();
        encoder.EncodeVideo(w, h, src, 0, format, output);
        Assert.Equal(want, output.WrittenSpan.ToArray());
    }

    // YCgCo is not a weighting of R, G and B, so RGB can't be converted for it; I420 input can still
    // be signalled as YCgCo.
    [Fact]
    public void RgbInputIsNotSupportedForYCgCo()
    {
        const int w = 32, h = 32;
        using var encoder = new AV1VideoEncoder(new Av1EncoderOptions { Threads = 1, MatrixCoefficients = Av1MatrixCoefficients.YCgCo });
        var bgra = new byte[I420Converter.SourceSize(VideoPixelFormatsEnum.Bgra, w * 4, w, h)];
        Assert.Throws<NotSupportedException>(() => encoder.EncodeVideo(w, h, bgra, VideoPixelFormatsEnum.Bgra, VideoCodecsEnum.AV1));
        var i420 = new byte[I420Converter.SourceSize(VideoPixelFormatsEnum.I420, w, w, h)];
        Assert.NotNull(encoder.EncodeVideo(w, h, i420, VideoPixelFormatsEnum.I420, VideoCodecsEnum.AV1));
    }

    // SIPSorcery passes no timestamps; the encoder stamps frames with their arrival time.
    [Fact]
    public void IVideoEncoderFramesAreTimedByArrival()
    {
        const int w = 64, h = 64;
        using var encoder = new AV1VideoEncoder(new Av1EncoderOptions { Threads = 1 });
        var stamps = new List<long>();
        encoder.FrameEncoded += r => stamps.Add(r.Timestamp);
        var frame = new byte[I420Converter.SourceSize(VideoPixelFormatsEnum.I420, w, w, h)];
        for (int t = 0; t < 3; t++)
        {
            if (t > 0)
            {
                Thread.Sleep(100);
            }
            encoder.EncodeVideo(w, h, frame, VideoPixelFormatsEnum.I420, VideoCodecsEnum.AV1);
        }
        Assert.Equal(3, stamps.Count);
        for (int t = 1; t < 3; t++)
        {
            Assert.InRange(stamps[t] - stamps[t - 1], 100 * 90, 2000 * 90);
        }
    }

    // The encoder plugged into a SIPSorcery video source, as in the package README.
    [Fact]
    public async Task TestPatternSourceProducesAv1Frames()
    {
        using var encoder = new AV1VideoEncoder(new Av1EncoderOptions { Threads = 2 });
        var video = new VideoTestPatternSource(encoder);
        VideoFormat av1 = video.GetVideoSourceFormats().Single(f => f.Codec == VideoCodecsEnum.AV1);
        video.SetVideoSourceFormat(av1);

        int frames = 0;
        var gotFrames = new TaskCompletionSource();
        video.OnVideoSourceEncodedSample += (duration, sample) =>
        {
            Assert.NotEmpty(sample);
            if (Interlocked.Increment(ref frames) == 5)
            {
                gotFrames.TrySetResult();
            }
        };
        await video.StartVideo();
        try
        {
            await gotFrames.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await video.CloseVideo();
        }
    }
}
