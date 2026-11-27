//-----------------------------------------------------------------------------
// Filename: DecoderConformanceVectors.cs
//
// Description: The decoder conformance table: openh264's test/api/decoder_test.cpp
// vectors with their upstream SHA1s, as reproduced by the C-only reference
// build and classified by the coding tools each stream uses. The table is the
// reference harness's own output (thirdparty/openh264-dotnet-port/reference/
// decoder-conformance.tsv), so it never drifts from what the oracle produced.
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SIPSorcery.H264.TestVectors;

public sealed record DecoderVector(
    string FileName,
    string Sha1,
    int Frames,
    bool IsConstrainedBaseline,
    string Features)
{
    public override string ToString() => FileName;
}

public static class DecoderConformanceVectors
{
    public const string TableFile = "decoder-conformance.tsv";
    public const string VectorDirectory = "TestVectors";

    private static readonly Lazy<IReadOnlyList<DecoderVector>> _all = new(Load);

    /// <summary>Every vector in upstream's decoder_test.cpp table.</summary>
    public static IReadOnlyList<DecoderVector> All => _all.Value;

    /// <summary>The vectors that use only Constrained Baseline tools: the port's scope.</summary>
    public static IEnumerable<DecoderVector> ConstrainedBaseline => All.Where(v => v.IsConstrainedBaseline);

    public static string PathOf(DecoderVector v) => Path.Combine(AppContext.BaseDirectory, VectorDirectory, v.FileName);

    /// <summary>xunit MemberData for the Constrained Baseline vectors.</summary>
    public static IEnumerable<object[]> ConstrainedBaselineData => ConstrainedBaseline.Select(v => new object[] { v });

    private static IReadOnlyList<DecoderVector> Load()
    {
        // Columns: result, CB, frames, errors, file, sha1, features. '#' lines are comments.
        var rows = new List<DecoderVector>();
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, TableFile)))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] c = line.Split('\t');
            if (c[0] != "PASS")
            {
                throw new InvalidDataException($"The reference build did not reproduce upstream's hash for {c[4]}; regenerate {TableFile}.");
            }
            rows.Add(new DecoderVector(c[4], c[5], int.Parse(c[2]), c[1] == "yes", c[6]));
        }
        return rows;
    }
}
