//-----------------------------------------------------------------------------
// Filename: DecoderConformanceTests.cs
//
// Description: Decoder conformance against openh264's upstream SHA1 table: each
// Constrained Baseline vector is decoded through the ported CWelsDecoder API,
// driven as upstream's BaseDecoderTest drives it (DecoderHash.cs), and its
// frame count and output SHA1 must match the table.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
// 25 Nov 2026  Claude          Decode test enabled (D6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.IO;
using System.Linq;
using Xunit;

namespace SIPSorcery.H264.TestVectors;

public class DecoderConformanceTests
{
    [Fact]
    public void Table_HasEveryUpstreamVector()
    {
        // 51 entries in kFileParamArray at openh264 78783f19, 35 of them Constrained Baseline.
        Assert.Equal(51, DecoderConformanceVectors.All.Count);
        Assert.Equal(35, DecoderConformanceVectors.ConstrainedBaseline.Count());
        Assert.All(DecoderConformanceVectors.All, v => Assert.Matches("^[0-9a-f]{40}$", v.Sha1));
    }

    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_IsCommitted(DecoderVector vector)
    {
        Assert.True(File.Exists(DecoderConformanceVectors.PathOf(vector)), $"{vector.FileName} is missing from {DecoderConformanceVectors.VectorDirectory}.");
    }

    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_DecodesToUpstreamHash(DecoderVector vector)
    {
        byte[] stream = File.ReadAllBytes(DecoderConformanceVectors.PathOf(vector));
        DecodeResult r = DecoderHash.HashChunks(HeaderDump.SplitChunks(stream));
        Assert.True(r.ErrorChunks == 0, $"{vector.FileName}: {r.ErrorChunks} DecodeFrame2/FlushFrame calls failed, the first with 0x{r.FirstErrorState:x}.");
        Assert.Equal(vector.Frames, r.Frames);
        Assert.Equal(vector.Sha1, r.Sha1);
    }
}
