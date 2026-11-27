//-----------------------------------------------------------------------------
// Filename: SliceThreadTests.cs
//
// Description: The slice threads (P5.7) beyond the api.tsv rows, which
// already compare threaded streams with the C build byte for byte.
//
// A threaded encode codes and deblocks each slice on its own, which upstream
// gets by turning iLoopFilterDisableIdc 0 into 2 (no filtering across slice
// edges). So with the same slices and the load balancing off, a threaded
// stream is the one-thread stream with iLoopFilterDisableIdc=2, as the C
// build also shows; the first test checks that over more content, slice
// layouts and rate control than the api rows. With the load balancing on,
// the slice sizes follow how long each slice took, so the stream can't be
// predicted: the second test checks that such a stream decodes to the
// encoder's own reconstruction frame by frame, and that the slices moved the
// right way: the slice at the noisy end, top or bottom, must shrink.
// The last two look for races: the same threaded encode run again and again,
// and on several encoders at once, must give the same bytes every time.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SIPSorcery.H264.Encoder;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

public class SliceThreadTests
{
    // The api rows' base: the extended API, no rate control and none of the preprocessing
    // that only decides what to code.
    private const string Base = "api=ext;iRCMode=-1;fMaxFrameRate=30;bEnableBackgroundDetection=0;bEnableAdaptiveQuant=0;"
                                + "bEnableSceneChangeDetect=0;";
    private const string Qcif = "iPicWidth=176;iPicHeight=144;";

    private readonly ITestOutputHelper _output;

    public SliceThreadTests(ITestOutputHelper output) => _output = output;

    public static IEnumerable<object[]> SameSliceRows => new[]
    {
        // content, frames, settings without the threads, threads
        new object[] { "subpel", 12, Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=2", 2 },
        new object[] { "block", 12, Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=3", 3 },
        new object[] { "mixed", 40, Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4", 4 },
        new object[] { "mixed", 40, Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4", 2 },
        new object[] { "noise", 8, Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4;iComplexityMode=2", 3 },
        new object[] { "cuts", 20, Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=7", 4 },
        new object[] { "pan", 12, Base + Qcif + "L0.uiSliceMode=2", 4 },
        new object[] { "camera", 12, Base + "iPicWidth=320;iPicHeight=192;L0.uiSliceMode=2;iComplexityMode=2", 2 },
        new object[] { "subpel", 12, Base + "iPicWidth=200;iPicHeight=120;L0.uiSliceMode=1;L0.uiSliceNum=4;uiIntraPeriod=5", 4 },
        new object[] { "mixed", 30, Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4;iTemporalLayerNum=3", 4 },
        new object[] { "subpel", 12, Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4;iLoopFilterAlphaC0Offset=4;iLoopFilterBetaOffset=-2", 4 },
        new object[] { "mixed", 30, "api=ext;iRCMode=0;iTargetBitrate=300000;fMaxFrameRate=30;" + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4", 4 },
        new object[] { "mixed", 30, "api=ext;iRCMode=1;iTargetBitrate=200000;fMaxFrameRate=30;" + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=2", 2 },
        new object[] { "camera", 20, "api=ext;iRCMode=0;iTargetBitrate=500000;fMaxFrameRate=30;bEnableDenoise=1;" + Qcif + "L0.uiSliceMode=2", 3 },
    };

    [Theory]
    [MemberData(nameof(SameSliceRows))]
    public void Threaded_IsOneThreadWithoutSliceEdgeFiltering(string content, int frames, string settings, int threads)
    {
        // More threads than slices would turn the load balancing on; off keeps the slices fixed.
        string threaded = settings + $";iMultipleThreadIdc={threads};bUseLoadBalancing=0";
        string single = settings + ";iMultipleThreadIdc=1;iLoopFilterDisableIdc=2";
        List<byte[]> a = Encode(threaded, content, frames, expectThreads: true);
        List<byte[]> b = Encode(single, content, frames, expectThreads: false);
        Assert.Equal(b.Count, a.Count);
        for (int f = 0; f < a.Count; f++)
        {
            Assert.True(a[f].AsSpan().SequenceEqual(b[f]), $"frame {f}: {a[f].Length} bytes threaded, {b[f].Length} on one thread.");
        }
        _output.WriteLine($"{a.Count} frames, {a.Sum(x => x.Length)} bytes the same");
    }

    private static readonly string[] BalancingSettings =
    {
        Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4;iMultipleThreadIdc=4",
        Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=2;iMultipleThreadIdc=2",
        // Rate control: the slices move in whole GOMs (a macroblock row here).
        "api=ext;iRCMode=0;iTargetBitrate=400000;fMaxFrameRate=30;" + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4;iMultipleThreadIdc=4",
    };

    // Each setting with the noise at the top of the picture and at the bottom.
    public static IEnumerable<object[]> BalancingRows =>
        BalancingSettings.SelectMany(s => new[] { new object[] { s, s.Contains("uiSliceNum=4") ? 4 : 2, false },
                                                    new object[] { s, s.Contains("uiSliceNum=4") ? 4 : 2, true } });

    [Theory]
    [MemberData(nameof(BalancingRows))]
    public void LoadBalancing_DecodesToTheReconstruction_AndMovesTheSlices(string settings, int slices, bool noiseAtBottom)
    {
        const int Frames = 40;
        using var enc = new EncoderDriver();
        Assert.Equal(0, enc.Initialize(settings));
        Assert.Equal(slices, ThreadCount(enc));
        int w = enc.PicWidth, h = enc.PicHeight;
        int rw = (w + 15) & ~15, rh = (h + 15) & ~15;
        int mbs = (rw / 16) * (rh / 16);

        var chunks = new List<byte[]>();
        var recons = new List<byte[][]>();
        var layouts = new HashSet<string>();
        bool noisyEndShrank = false;
        for (int f = 0; f < Frames; f++)
        {
            EncodedFrame got = enc.EncodeFrame(Lopsided(w, h, f, noiseAtBottom), f * 1000L / 30);
            Assert.Equal(0, got.ReturnCode);
            int[] counts = enc.SliceMbCounts();
            Assert.Equal(slices, counts.Length);
            Assert.Equal(mbs, counts.Sum());
            Assert.All(counts, c => Assert.True(c > 0));
            layouts.Add(string.Join(',', counts));
            noisyEndShrank |= noiseAtBottom ? counts[^1] < counts[0] : counts[0] < counts[^1];
            if (got.Bytes.Length != 0)
            {
                chunks.Add(got.Bytes);
                recons.Add(got.Comparable ? new[] { enc.Reconstruction(0, rw, rh), enc.Reconstruction(1, rw / 2, rh / 2),
                                                     enc.Reconstruction(2, rw / 2, rh / 2) } : null);
            }
        }
        _output.WriteLine($"{layouts.Count} slice layouts: {string.Join(" | ", layouts)}");

        var decoded = new List<byte[][]>();
        DecodeResult d = DecoderHash.DecodeChunks(chunks, (y, u, v) => decoded.Add(new[] { Copy(y), Copy(u), Copy(v) }));
        Assert.Equal(0, d.ErrorChunks);
        Assert.Equal(chunks.Count, decoded.Count);
        int compared = 0;
        for (int i = 0; i < decoded.Count; i++)
        {
            if (recons[i] == null)
            {
                continue;
            }
            for (int p = 0; p < 3; p++)
            {
                int pw = p == 0 ? w : w / 2, ph = p == 0 ? h : h / 2, stride = p == 0 ? rw : rw / 2;
                for (int r = 0; r < ph; r++)
                {
                    Assert.True(recons[i][p].AsSpan(r * stride, pw).SequenceEqual(decoded[i][p].AsSpan(r * pw, pw)),
                                $"frame {i} plane {p} row {r}: the reconstruction differs from the decoder's.");
                }
            }
            compared++;
        }
        _output.WriteLine($"{compared} of {decoded.Count} reconstructions compared with the decoder's");
        // The rate control skips some of the noisy frames.
        Assert.True(compared >= Frames / 4, $"Only {compared} reconstructions to compare.");

        // A quarter of the picture is new noise every frame and the rest barely changes, so the
        // slice at the noisy end takes far longer and the load balancing must shrink it, which
        // it can only do from the slice times. Without them (the ratios left at 0, say) every
        // slice but the last shrinks to its least, which looks right for noise at the top.
        Assert.True(layouts.Count > 1, "The load balancing never moved the slices.");
        Assert.True(noisyEndShrank, $"The slice at the noisy end never became the smaller: {string.Join(" | ", layouts)}.");
        Assert.Equal(0u, enc.Release());
    }

    [Fact]
    public void Threaded_IsTheSameEveryRun()
    {
        string settings = Base + "iPicWidth=320;iPicHeight=192;L0.uiSliceMode=1;L0.uiSliceNum=4;iMultipleThreadIdc=4;bUseLoadBalancing=0";
        string first = Hash(Encode(settings, "mixed", 30, expectThreads: true));
        for (int run = 0; run < 8; run++)
        {
            Assert.Equal(first, Hash(Encode(settings, "mixed", 30, expectThreads: true)));
        }
    }

    [Fact]
    public void ThreadedEncoders_RunTogether_GiveTheirOwnStreams()
    {
        string[] settings =
        {
            Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=4;iMultipleThreadIdc=4;bUseLoadBalancing=0",
            Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=3;iMultipleThreadIdc=3;bUseLoadBalancing=0",
            Base + "iPicWidth=320;iPicHeight=192;L0.uiSliceMode=2;iMultipleThreadIdc=4",
            Base + Qcif + "L0.uiSliceMode=1;L0.uiSliceNum=2;iMultipleThreadIdc=2;bUseLoadBalancing=0",
            Base + Qcif,
            Base + "iPicWidth=320;iPicHeight=192;L0.uiSliceMode=1;L0.uiSliceNum=7;iMultipleThreadIdc=4",
        };
        string[] alone = settings.Select(s => Hash(Encode(s, "mixed", 30, expectThreads: null))).ToArray();
        for (int round = 0; round < 3; round++)
        {
            var together = new string[settings.Length * 2];
            Parallel.For(0, together.Length, new ParallelOptions { MaxDegreeOfParallelism = together.Length },
                         i => together[i] = Hash(Encode(settings[i % settings.Length], "mixed", 30, expectThreads: null)));
            for (int i = 0; i < together.Length; i++)
            {
                Assert.Equal(alone[i % settings.Length], together[i]);
            }
        }
    }

    /// <summary>
    /// Each frame's bytes. expectThreads, when given, says whether the encoder must have
    /// settled on more than one thread.
    /// </summary>
    private static List<byte[]> Encode(string settings, string content, int frames, bool? expectThreads)
    {
        using var enc = new EncoderDriver();
        Assert.Equal(0, enc.Initialize(settings));
        if (expectThreads.HasValue)
        {
            Assert.Equal(expectThreads.Value, ThreadCount(enc) > 1);
        }
        var output = new List<byte[]>();
        for (int f = 0; f < frames; f++)
        {
            EncodedFrame got = enc.EncodeFrame(StreamContent.Fill(enc.PicWidth, enc.PicHeight, content, f), f * 1000L / 30);
            Assert.Equal(0, got.ReturnCode);
            output.Add(got.Bytes);
        }
        Assert.Equal(0u, enc.Release());
        return output;
    }

    /// <summary>The iMultipleThreadIdc the encoder settled on.</summary>
    private static int ThreadCount(EncoderDriver enc)
    {
        SEncParamExt e = default;
        Assert.Equal(0, enc.GetOption(ENCODER_OPTION.ENCODER_OPTION_SVC_ENCODE_PARAM_EXT, ref e));
        return e.iMultipleThreadIdc;
    }

    private static string Hash(List<byte[]> frames)
    {
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        foreach (byte[] f in frames)
        {
            sha1.AppendData(f);
        }
        return Convert.ToHexStringLower(sha1.GetHashAndReset());
    }

    /// <summary>New noise over the top or bottom quarter of every frame, and a still scene elsewhere.</summary>
    private static byte[] Lopsided(int w, int h, int f, bool atBottom)
    {
        byte[] buf = StreamContent.Fill(w, h, "static", f);
        int cw = w >> 1, ch = h >> 1;
        int[] offset = { 0, w * h, w * h + cw * ch };
        int[] pw = { w, cw, cw }, ph = { h, ch, ch };
        for (int plane = 0; plane < 3; plane++)
        {
            for (int r = atBottom ? ph[plane] - ph[plane] / 4 : 0; r < (atBottom ? ph[plane] : ph[plane] / 4); r++)
            {
                for (int c = 0; c < pw[plane]; c++)
                {
                    buf[offset[plane] + r * pw[plane] + c] = StreamContent.Sample("random", plane, r, c, f);
                }
            }
        }
        return buf;
    }

    private static unsafe byte[] Copy(DecoderHash.Plane p)
    {
        var a = new byte[p.Width * p.Height];
        for (int r = 0; r < p.Height; r++)
        {
            new ReadOnlySpan<byte>(p.Data + r * p.Stride, p.Width).CopyTo(a.AsSpan(r * p.Width));
        }
        return a;
    }
}
