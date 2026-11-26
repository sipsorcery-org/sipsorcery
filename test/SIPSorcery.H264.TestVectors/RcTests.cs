//-----------------------------------------------------------------------------
// Filename: RcTests.cs
//
// Description: E6 acceptance: rate controlled streams the ported encoder
// writes, frame by frame, for a table of configurations must match the C-only
// reference build's byte for byte (refharness `h264ref rc`, reference/rc.tsv):
// the quality, bit rate, buffer based and timestamp modes with their frame
// skips, max bit rate windows and padding, over timestamp schedules with
// pauses, steps back and jitter. Preprocessing stays off (background
// detection, adaptive quantisation and scene change detection come with E7),
// and every reconstruction a decoder can see must equal what the ported
// decoder decodes from those bytes, as in StreamTests.
//
// Set H264_DUMP_DIR to have a failing row write its stream there, to compare
// with `h264ref rc-dump <name> <out>`.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E6).
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

public static class RcReferences
{
    public const string TableFile = "rc.tsv";

    private static readonly Lazy<IReadOnlyList<StreamReference>> _rows = new(() => StreamTable.Load(TableFile));

    public static IReadOnlyList<StreamReference> Rows => _rows.Value;

    public static IEnumerable<object[]> Names => Rows.Select(r => new object[] { r.Name });
}

public class RcTests
{
    private readonly ITestOutputHelper _output;

    public RcTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void TableIsComplete()
    {
        Assert.NotEmpty(RcReferences.Rows);
        Assert.All(RcReferences.Rows, r =>
        {
            Assert.Equal(0, r.InitReturnCode);
            Assert.Equal(r.FrameCount, r.Frames.Count);
        });

        // Every mode, plenty of coded and skipped frames, padding in some, and no two
        // configurations writing the same stream but the two pan pauses either side of the
        // timestamp mode's 1 s limit, which take different paths to the same bytes.
        foreach (string mode in new[] { "iRCMode=0;", "iRCMode=1;", "iRCMode=2;", "iRCMode=3;" })
        {
            Assert.Contains(RcReferences.Rows, r => r.Settings.Contains(mode, StringComparison.Ordinal));
        }
        IEnumerable<string[]> frames = RcReferences.Rows.SelectMany(r => r.Frames).Select(f => f.Split('\t'));
        Assert.True(frames.Count(f => f[2] == "P") >= 1500);
        Assert.True(frames.Count(f => f[2] == "skip") >= 500);
        Assert.True(frames.Count(f => f[2] == "P" && f[4].Contains("N=", StringComparison.Ordinal)) >= 50);
        List<StreamReference> distinct = RcReferences.Rows.Where(r => r.Name != "timestamp-pause-1000").ToList();
        Assert.Equal(distinct.Count, distinct.Select(r => r.Sha1).Distinct().Count());
        Assert.Equal(RcReferences.Rows.Single(r => r.Name == "timestamp-pause-950").Sha1,
                     RcReferences.Rows.Single(r => r.Name == "timestamp-pause-1000").Sha1);
    }

    [Theory]
    [MemberData(nameof(RcReferences.Names), MemberType = typeof(RcReferences))]
    public void RcStreamMatchesReference(string name) =>
        StreamCheck.Run(RcReferences.Rows.Single(r => r.Name == name), _output);
}
