//-----------------------------------------------------------------------------
// Filename: MbDumpTests.cs
//
// Description: D2 acceptance: the ported CAVLC macroblock syntax parsing
// (WelsDecodeSlice and the I and P slice macroblock parsers) must produce, for
// every conformance vector, the same macroblock dump as the C-only reference
// build (refharness `h264ref mbs`, stored in reference/mbs.tsv). The dump is
// the D1 header dump plus, for each parsed slice, the parser's end state and
// the SHA-1 of every macroblock's parse results. See HeaderDump.cs.
//
// D3 added intra reconstruction to the dump, and D4 the decoder's reference
// handling (picture buffer, reference lists, marking) and the reconstruction
// of every macroblock, so these tests are also the D3 and D4 acceptance.
//
// As in D1, each vector is also checked in the three damaged forms
// (`h264ref mbs-damaged`, reference/mbs-damaged.tsv), which drive the
// macroblock parsers' error and concealment paths.
//
// CABAC and B slices are not ported yet and are skipped on both sides, so the
// CABAC vectors check only that the skip happens in the same places.
//
// The 35 Constrained Baseline vectors are committed and always run. The other
// 16 run when OPENH264_RES points at an openh264 res/ directory.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D2).
// 25 Nov 2026  Claude          D3 and D4 coverage noted.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

/// <summary>One reference macroblock dump. Variant is -1 for the clean stream, else the damage variant.</summary>
public sealed record MbReference(string FileName, int Variant, int Chunks, int Slices, int Mbs, int Bytes, string Sha1);

public static class MbReferences
{
    public const string CleanTableFile = "mbs.tsv";
    public const string DamagedTableFile = "mbs-damaged.tsv";

    private static readonly Lazy<IReadOnlyDictionary<string, MbReference>> _clean = new(LoadClean);
    private static readonly Lazy<IReadOnlyList<MbReference>> _damaged = new(LoadDamaged);

    /// <summary>Clean stream dumps, by file name.</summary>
    public static IReadOnlyDictionary<string, MbReference> Clean => _clean.Value;

    /// <summary>Damaged stream dumps, in table order.</summary>
    public static IReadOnlyList<MbReference> Damaged => _damaged.Value;

    private static IEnumerable<string[]> Rows(string table)
    {
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, table)))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] c = line.Split('\t');
            if (c[^1] == "MISSING" || c[^1] == "INITFAIL")
            {
                throw new InvalidDataException($"The reference build has no macroblock dump for {c[0]} ({c[^1]}); regenerate {table}.");
            }
            yield return c;
        }
    }

    private static IReadOnlyDictionary<string, MbReference> LoadClean() =>
        // Columns: file, slices, mbs, bytes, sha1.
        Rows(CleanTableFile).ToDictionary(c => c[0],
            c => new MbReference(c[0], -1, -1, int.Parse(c[1]), int.Parse(c[2]), int.Parse(c[3]), c[4]));

    private static IReadOnlyList<MbReference> LoadDamaged() =>
        // Columns: file, variant, chunks, slices, mbs, bytes, sha1.
        Rows(DamagedTableFile).Select(c => new MbReference(c[0], int.Parse(c[1]), int.Parse(c[2]), int.Parse(c[3]),
            int.Parse(c[4]), int.Parse(c[5]), c[6])).ToList();
}

public class MbDumpTests
{
    private const string ResEnvironmentVariable = "OPENH264_RES";

    private readonly ITestOutputHelper _output;

    public MbDumpTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> OtherVectorData =>
        DecoderConformanceVectors.All.Where(v => !v.IsConstrainedBaseline).Select(v => new object[] { v });

    [Fact]
    public void Tables_HaveEveryUpstreamVector()
    {
        var files = DecoderConformanceVectors.All.Select(v => v.FileName).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(files, MbReferences.Clean.Keys.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(files, MbReferences.Damaged.Select(r => r.FileName).Distinct().OrderBy(f => f, StringComparer.Ordinal));
        Assert.All(MbReferences.Clean.Values.Concat(MbReferences.Damaged), r => Assert.Matches("^[0-9a-f]{40}$", r.Sha1));
    }

    [Fact]
    public void Tables_ParseEveryConstrainedBaselineVector()
    {
        // Every Constrained Baseline vector must reach the macroblock parsers, or the
        // check below would pass vacuously.
        Assert.All(DecoderConformanceVectors.All.Where(v => v.IsConstrainedBaseline),
            v => Assert.True(MbReferences.Clean[v.FileName].Slices > 0, $"{v.FileName}: no slice parsed by the reference build."));
    }

    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_MbDumpMatchesNative(DecoderVector vector)
    {
        AssertCleanDumpMatches(vector, DecoderConformanceVectors.PathOf(vector));
    }

    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_DamagedMbDumpsMatchNative(DecoderVector vector)
    {
        AssertDamagedDumpsMatch(vector, DecoderConformanceVectors.PathOf(vector));
    }

    [Theory]
    [MemberData(nameof(OtherVectorData))]
    public void OtherVector_MbDumpsMatchNative(DecoderVector vector)
    {
        string res = Environment.GetEnvironmentVariable(ResEnvironmentVariable);
        if (string.IsNullOrEmpty(res))
        {
            _output.WriteLine($"{ResEnvironmentVariable} is not set; {vector.FileName} is not committed, nothing to check.");
            return;
        }
        string path = Path.Combine(res, vector.FileName);
        Assert.True(File.Exists(path), $"{path} not found ({ResEnvironmentVariable}={res}).");
        AssertCleanDumpMatches(vector, path);
        AssertDamagedDumpsMatch(vector, path);
    }

    private void AssertCleanDumpMatches(DecoderVector vector, string path)
    {
        byte[] stream = File.ReadAllBytes(path);
        AssertDumpMatches(MbReferences.Clean[vector.FileName], HeaderDump.SplitChunks(stream), $"h264ref mbdump {path}");
    }

    private void AssertDamagedDumpsMatch(DecoderVector vector, string path)
    {
        byte[] stream = File.ReadAllBytes(path);
        List<byte[]> clean = HeaderDump.SplitChunks(stream);
        var expected = MbReferences.Damaged.Where(r => r.FileName == vector.FileName).ToList();
        Assert.NotEmpty(expected);
        foreach (MbReference r in expected)
        {
            List<byte[]> damaged = HeaderDump.DamageChunks(clean, (uint)stream.Length, r.Variant);
            Assert.True(damaged.Count == r.Chunks,
                $"{vector.FileName} variant {r.Variant}: damaged to {damaged.Count} chunks, the reference build to {r.Chunks}.");
            AssertDumpMatches(r, damaged, $"h264ref mbdump-damaged {path} {r.Variant}");
        }
    }

    private void AssertDumpMatches(MbReference expected, List<byte[]> chunks, string nativeCommand)
    {
        Assert.True(HeaderDump.RunMbChunks(chunks, out string dump, out int slices, out int mbs),
            "The decoder context failed to initialise.");
        string sha1 = HeaderDump.Sha1Hex(dump);
        if (sha1 == expected.Sha1 && slices == expected.Slices && mbs == expected.Mbs && dump.Length == expected.Bytes)
        {
            return;
        }

        string label = expected.Variant < 0 ? expected.FileName : $"{expected.FileName}.damaged{expected.Variant}";
        string dir = Path.Combine(AppContext.BaseDirectory, "mbdumps");
        Directory.CreateDirectory(dir);
        string outFile = Path.Combine(dir, label + ".txt");
        File.WriteAllText(outFile, dump);
        _output.WriteLine($"C# dump written to {outFile}; compare with `{nativeCommand}`.");
        Assert.Fail($"{label}: macroblock dump differs from the reference build. " +
            $"Expected slices={expected.Slices} mbs={expected.Mbs} bytes={expected.Bytes} sha1={expected.Sha1}, " +
            $"got slices={slices} mbs={mbs} bytes={dump.Length} sha1={sha1}. C# dump: {outFile}");
    }
}
