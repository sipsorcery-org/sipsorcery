//-----------------------------------------------------------------------------
// Filename: DecoderConformanceTests.cs
//
// Description: Decoder conformance against openh264's upstream SHA1 table. The
// decode test is skipped until the decoder orchestration lands (D6 in the port
// plan); the table checks run now.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 24 Nov 2026  Claude          Created.
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

    [Theory(Skip = "The decoder is not ported yet (D6 in the port plan).")]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_DecodesToUpstreamHash(DecoderVector vector)
    {
        Assert.Fail($"Not implemented: decode {vector.FileName} and compare with {vector.Sha1}.");
    }
}
