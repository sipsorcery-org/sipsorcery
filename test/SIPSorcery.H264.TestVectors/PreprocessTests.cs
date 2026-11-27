//-----------------------------------------------------------------------------
// Filename: PreprocessTests.cs
//
// Description: E7 acceptance: preprocessed streams the ported encoder writes,
// frame by frame, for a table of configurations must match the C-only
// reference build's byte for byte (refharness `h264ref preprocess`,
// reference/preprocess.tsv): scene change detection's IDRs and its medium
// changes, background detection, the denoiser, downsampling a larger source to
// the coded size (half, quarter, general and odd sizes), each on its own and
// together, in fixed QP and every rate control mode, with temporal layers,
// intra periods, long term references, several slices and prefix NAL units.
// Adaptive quantisation stays off whatever the configuration says, as upstream
// ParamValidation forces it off; the kernel rows test it on its own. Every
// reconstruction a decoder can see must equal what the ported decoder decodes
// from those bytes, as in StreamTests.
//
// Set H264_DUMP_DIR to have a failing row write its stream there, to compare
// with `h264ref preprocess-dump <name> <out>`.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (E7).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

public static class PreprocessReferences
{
    public const string TableFile = "preprocess.tsv";

    private static readonly Lazy<IReadOnlyList<StreamReference>> _rows = new(() => StreamTable.Load(TableFile));

    public static IReadOnlyList<StreamReference> Rows => _rows.Value;

    public static IEnumerable<object[]> Names => Rows.Select(r => new object[] { r.Name });
}

public class PreprocessTests
{
    private readonly ITestOutputHelper _output;

    public PreprocessTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TableIsComplete()
    {
        Assert.NotEmpty(PreprocessReferences.Rows);
        Assert.All(PreprocessReferences.Rows, r =>
        {
            Assert.Equal(0, r.InitReturnCode);
            Assert.Equal(r.FrameCount, r.Frames.Count);
        });

        // Scene change detection's IDRs mid stream, without an intra period asking for them,
        // downsampled sources, the base API, every rate control mode, and no two
        // configurations writing the same stream.
        IEnumerable<string[]> frames = PreprocessReferences.Rows.SelectMany(r => r.Frames).Select(f => f.Split('\t'));
        Assert.True(frames.Count(f => f[2] == "P") >= 800);
        Assert.True(frames.Count(f => f[2] == "skip") >= 300);
        Assert.Contains(PreprocessReferences.Rows, r => r.Name == "scd-cuts" && r.Frames.Any(f => f.Split('\t') is [not "0", _, "IDR", ..]));
        Assert.Contains(PreprocessReferences.Rows, r => r.Settings.Contains("api=base;", StringComparison.Ordinal));
        foreach (string mode in new[] { "iRCMode=-1", "iRCMode=0;", "iRCMode=1;", "iRCMode=2;", "iRCMode=3;" })
        {
            Assert.Contains(PreprocessReferences.Rows, r => r.Settings.Contains(mode, StringComparison.Ordinal));
        }
        Assert.Equal(PreprocessReferences.Rows.Count, PreprocessReferences.Rows.Select(r => r.Sha1).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(PreprocessReferences.Names), MemberType = typeof(PreprocessReferences))]
    public void PreprocessedStreamMatchesReference(string name) =>
        StreamCheck.Run(PreprocessReferences.Rows.Single(r => r.Name == name), _output);
}
