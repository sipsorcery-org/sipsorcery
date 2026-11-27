//-----------------------------------------------------------------------------
// Filename: H264CodecTests.cs
//
// Description: The SIPSorcery wire-up of the decoder (D7). H264Codec is fed
// the conformance vectors one access unit at a time, as an RTP receiver feeds
// it, and must still produce upstream's SHA1s; the same again through RTP
// packets and SIPSorcery's H264Depacketiser. Also the output formats, the key
// frame signalling and H264NetVideoEndPoint. The encoder is in H264EncodeTests.cs.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.H264.TestVectors;

public class H264CodecTests
{
    // SIPSorcery's RTP payload limit (RTPSession.RTP_MAX_PAYLOAD, not public).
    private const int RtpMaxPayload = 1200;

    private static readonly VideoFormat H264Format = H264NetVideoEndPoint.SupportedFormats[0];

    private static List<byte[]> AccessUnitsOf(DecoderVector vector) =>
        AccessUnits.Split(File.ReadAllBytes(DecoderConformanceVectors.PathOf(vector)));

    private static List<byte[]> AccessUnitsOf(string fileName) =>
        AccessUnitsOf(DecoderConformanceVectors.All.Single(v => v.FileName == fileName));

    private static string Hex(IncrementalHash sha1) => Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant();

    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_AccessUnitsDecodeToUpstreamHash(DecoderVector vector)
    {
        using var codec = new H264Codec();
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        int frames = 0, damaged = 0;

        foreach (byte[] au in AccessUnitsOf(vector))
        {
            foreach (VideoSample s in codec.DecodeVideo(au, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264))
            {
                Assert.Equal(VideoPixelFormatsEnum.I420, s.PixelFormat);
                Assert.Equal((int)(s.Width * s.Height + 2 * ((s.Width + 1) / 2) * ((s.Height + 1) / 2)), s.Sample.Length);
                sha1.AppendData(s.Sample);
                ++frames;
            }
            damaged += codec.KeyFrameRequired ? 1 : 0;
        }

        Assert.True(damaged == 0, $"{vector.FileName}: {damaged} access units did not decode cleanly.");
        Assert.Equal(vector.Frames, frames);
        Assert.Equal(vector.Sha1, Hex(sha1));
    }

    /// <summary>
    /// Through RTP: each access unit is packetised as a browser sends it (STAP-A for SPS and
    /// PPS, FU-A for slices over the payload limit), two packets of each unit are swapped, and
    /// the sequence numbers wrap, then SIPSorcery's H264Depacketiser puts the units back
    /// together for the codec.
    /// </summary>
    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_RtpRoundTripDecodesToUpstreamHash(DecoderVector vector)
    {
        using var codec = new H264Codec();
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var depacketiser = new H264Depacketiser();
        ushort seq = 65500;
        uint timestamp = 90000;
        int frames = 0, units = 0;

        foreach (byte[] au in AccessUnitsOf(vector))
        {
            var packets = AccessUnits.Packetise(au, RtpMaxPayload)
                .Select(p => (p.Payload, p.Marker, Seq: seq++)).ToList();
            if (packets.Count >= 3)
            {
                // Out of order on the wire; the marker packet still comes last, as the
                // depacketiser needs it to.
                (packets[0], packets[1]) = (packets[1], packets[0]);
            }

            MemoryStream assembled = null;
            foreach (var p in packets)
            {
                assembled = depacketiser.ProcessRTPPayload(p.Payload, p.Seq, timestamp, p.Marker ? 1 : 0, out _);
                Assert.True(p.Marker || assembled == null);
            }
            Assert.NotNull(assembled);
            ++units;
            timestamp += 3000;

            foreach (VideoSample s in codec.DecodeVideo(assembled.ToArray(), VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264))
            {
                sha1.AppendData(s.Sample);
                ++frames;
            }
            Assert.False(codec.KeyFrameRequired, $"{vector.FileName}: access unit {units} did not decode cleanly after the round trip.");
        }

        Assert.Equal(vector.Frames, frames);
        Assert.Equal(vector.Sha1, Hex(sha1));
    }

    [Fact]
    public void RtpRoundTrip_CoversFragmentedSlices()
    {
        // The 1280x720 vector's slices are mostly larger than one packet.
        var aus = AccessUnitsOf("Zhling_1280x720.264");
        int fragmented = aus.Count(au => AccessUnits.Packetise(au, RtpMaxPayload).Any(p => (p.Payload[0] & 0x1f) == 28));
        Assert.True(fragmented > aus.Count / 2, $"Only {fragmented} of {aus.Count} access units needed FU-A.");
        Assert.Contains(aus, au => AccessUnits.Packetise(au, RtpMaxPayload).Any(p => (p.Payload[0] & 0x1f) == 24));
    }

    /// <summary>
    /// BGR, RGB, I420 and DecodeVideoFaster describe the same pictures: the same sizes, BGR is
    /// RGB with R and B swapped, and the faster path's buffer holds the same bytes.
    /// </summary>
    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_OutputFormatsAgree(DecoderVector vector)
    {
        using var i420 = new H264Codec();
        using var bgr = new H264Codec();
        using var rgb = new H264Codec();
        using var faster = new H264Codec();
        int frames = 0;

        foreach (byte[] au in AccessUnitsOf(vector))
        {
            var y = i420.DecodeVideo(au, VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264).ToList();
            var b = bgr.DecodeVideo(au, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264).ToList();
            var r = rgb.DecodeVideo(au, VideoPixelFormatsEnum.Rgb, VideoCodecsEnum.H264).ToList();
            var f = faster.DecodeVideoFaster(au, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264).ToList();
            Assert.Equal(y.Count, b.Count);
            Assert.Equal(y.Count, r.Count);
            Assert.Equal(y.Count, f.Count);
            if (y.Count == 0)
            {
                continue;
            }
            ++frames;

            Assert.Equal(VideoPixelFormatsEnum.Bgr, b[0].PixelFormat);
            Assert.Equal(VideoPixelFormatsEnum.Rgb, r[0].PixelFormat);
            Assert.Equal(VideoPixelFormatsEnum.Bgr, f[0].PixelFormat);
            Assert.Equal((y[0].Width, y[0].Height), (b[0].Width, b[0].Height));
            Assert.Equal((y[0].Width, y[0].Height), (r[0].Width, r[0].Height));
            Assert.Equal(((int)y[0].Width, (int)y[0].Height, (int)y[0].Width * 3), (f[0].Width, f[0].Height, f[0].Stride));
            Assert.Equal((int)(b[0].Width * 3 * b[0].Height), b[0].Sample.Length);

            byte[] swapped = (byte[])r[0].Sample.Clone();
            for (int i = 0; i < swapped.Length; i += 3)
            {
                (swapped[i], swapped[i + 2]) = (swapped[i + 2], swapped[i]);
            }
            Assert.True(swapped.AsSpan().SequenceEqual(b[0].Sample), $"{vector.FileName} frame {frames}: RGB is not BGR swapped.");
            Assert.True(f[0].GetBuffer().AsSpan().SequenceEqual(b[0].Sample), $"{vector.FileName} frame {frames}: DecodeVideoFaster differs from DecodeVideo.");
        }

        Assert.Equal(vector.Frames, frames);
    }

    /// <summary>A receiver that joins after the IDR picture gets nothing, and is told to ask for a key frame, until one comes.</summary>
    [Fact]
    public void JoiningAfterTheKeyFrame_RequiresAKeyFrameUntilOneArrives()
    {
        var aus = AccessUnitsOf("BA_MW_D.264");
        using var codec = new H264Codec();

        for (int i = 1; i <= 5; i++)
        {
            Assert.Empty(codec.DecodeVideo(aus[i], VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
            Assert.True(codec.KeyFrameRequired);
        }

        Assert.Single(codec.DecodeVideo(aus[0], VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        Assert.False(codec.KeyFrameRequired);
        Assert.Single(codec.DecodeVideo(aus[1], VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        Assert.False(codec.KeyFrameRequired);
    }

    /// <summary>
    /// A lost reference picture: the pictures after it are still output, concealed, and the key
    /// frame request stands for each of them, because each predicts from a concealed one. An IDR
    /// picture clears it, and decodes exactly as it did the first time.
    /// </summary>
    [Fact]
    public void LostReferencePicture_RequiresAKeyFrameUntilTheNextIdr()
    {
        var aus = AccessUnitsOf("BA_MW_D.264");
        // The lost picture must be a reference one (nal_ref_idc != 0) for anything to predict from it.
        var lost = AccessUnits.FindNals(aus[2]).Single(n => (aus[2][n.Offset] & 0x1f) == 1);
        Assert.NotEqual(0, aus[2][lost.Offset] & 0x60);

        using var codec = new H264Codec();
        byte[] first = codec.DecodeVideo(aus[0], VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264).Single().Sample;
        Assert.Single(codec.DecodeVideo(aus[1], VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
        Assert.False(codec.KeyFrameRequired);

        for (int i = 3; i < 10; i++)
        {
            Assert.Single(codec.DecodeVideo(aus[i], VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264));
            Assert.True(codec.KeyFrameRequired, $"Access unit {i} after the loss decoded as clean.");
        }

        byte[] again = codec.DecodeVideo(aus[0], VideoPixelFormatsEnum.I420, VideoCodecsEnum.H264).Single().Sample;
        Assert.False(codec.KeyFrameRequired);
        Assert.True(first.AsSpan().SequenceEqual(again));
    }

    [Fact]
    public void Codec_RejectsBadDecodeArguments()
    {
        var codec = new H264Codec();
        Assert.Throws<ArgumentNullException>(() => codec.DecodeVideo(null, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264));
        Assert.Throws<NotSupportedException>(() => codec.DecodeVideo(new byte[] { 0, 0, 0, 1, 0x67 }, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.VP8));
        Assert.Empty(codec.DecodeVideo(Array.Empty<byte>(), VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264));
        codec.ForceKeyFrame();

        var format = Assert.Single(codec.SupportedFormats);
        Assert.Equal(VideoCodecsEnum.H264, format.Codec);
        Assert.Contains("profile-level-id=42e01f", format.Parameters);
        Assert.Contains("packetization-mode=1", format.Parameters);

        codec.Dispose();
        codec.Dispose();
        Assert.Throws<ObjectDisposedException>(() => codec.DecodeVideo(AccessUnitsOf("BA_MW_D.264")[0], VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.H264));
    }

    [Fact]
    public void EndPoint_RaisesEachPictureAsPackedBgr()
    {
        var vector = DecoderConformanceVectors.All.Single(v => v.FileName == "CVFC1_Sony_C.jsv");
        using var endPoint = new H264NetVideoEndPoint();
        using var reference = new H264Codec();
        int frames = 0;
        endPoint.OnVideoSinkDecodedSample += (sample, width, height, stride, format) =>
        {
            Assert.Equal(VideoPixelFormatsEnum.Bgr, format);
            Assert.Equal((int)width * 3, stride);
            Assert.Equal(stride * (int)height, sample.Length);
            ++frames;
        };

        foreach (byte[] au in AccessUnitsOf(vector))
        {
            endPoint.GotVideoFrame(null, 0, au, endPoint.GetVideoSinkFormats()[0]);
        }

        Assert.Equal(vector.Frames, frames);
    }

    [Fact]
    public void EndPoint_FasterHandlerGetsTheSamePictures()
    {
        var aus = AccessUnitsOf("BA_MW_D.264");
        using var arrays = new H264NetVideoEndPoint();
        using var raw = new H264NetVideoEndPoint();
        var fromArrays = new List<byte[]>();
        var fromRaw = new List<byte[]>();
        arrays.OnVideoSinkDecodedSample += (sample, width, height, stride, format) => fromArrays.Add(sample);
        raw.OnVideoSinkDecodedSampleFaster += image => fromRaw.Add(image.GetBuffer());

        foreach (byte[] au in aus)
        {
            arrays.GotVideoFrame(null, 0, au, H264Format);
            raw.GotVideoFrame(null, 0, au, H264Format);
        }

        Assert.Equal(fromArrays.Count, fromRaw.Count);
        Assert.NotEmpty(fromArrays);
        Assert.All(fromArrays.Zip(fromRaw), pair => Assert.True(pair.First.AsSpan().SequenceEqual(pair.Second)));
    }

    [Fact]
    public void EndPoint_AsksForAKeyFrameAtMostOncePerInterval()
    {
        var aus = AccessUnitsOf("BA_MW_D.264");
        using var endPoint = new H264NetVideoEndPoint();
        int requests = 0, frames = 0;
        endPoint.OnKeyFrameRequired += () => requests++;
        endPoint.OnVideoSinkDecodedSample += (sample, width, height, stride, format) => frames++;

        // Joined late: a burst of undecodable access units asks once, not once each.
        for (int i = 1; i < 20; i++)
        {
            endPoint.GotVideoFrame(null, 0, aus[i], H264Format);
        }
        Assert.True(endPoint.KeyFrameRequired);
        Assert.Equal(1, requests);
        Assert.Equal(0, frames);

        // The key frame arrives and the stream decodes cleanly from there, with no more requests.
        foreach (byte[] au in aus)
        {
            endPoint.GotVideoFrame(null, 0, au, H264Format);
        }
        Assert.False(endPoint.KeyFrameRequired);
        Assert.Equal(1, requests);
        Assert.Equal(aus.Count, frames);
    }

    [Fact]
    public void EndPoint_OffersConstrainedBaselineOnly()
    {
        using var endPoint = new H264NetVideoEndPoint();
        var format = Assert.Single(endPoint.GetVideoSinkFormats());
        Assert.Equal(VideoCodecsEnum.H264, format.Codec);
        Assert.Equal(H264Codec.H264_FORMAT_PARAMETERS, format.Parameters);
        Assert.Throws<NotSupportedException>(() => endPoint.GotVideoRtp(null, 0, 0, 0, 0, false, new byte[1]));
    }
}
