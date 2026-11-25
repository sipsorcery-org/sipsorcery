//-----------------------------------------------------------------------------
// Filename: HeaderDumpTests.cs
//
// Description: D1 acceptance: the ported NAL, parameter set, slice header and
// access unit parsing must produce, for every conformance vector, the same
// header dump as the C-only reference build (refharness `h264ref headers`,
// stored in reference/headers.tsv). See HeaderDump.cs.
//
// The clean vectors never reach the parsers' error paths, so each vector is
// also checked in three damaged forms (dropped, truncated and bit-flipped
// NALs, `h264ref headers-damaged`, reference/headers-damaged.tsv).
//
// The 35 Constrained Baseline vectors are committed and always run. The other
// 16 run when OPENH264_RES points at an openh264 res/ directory.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
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

/// <summary>One reference header dump. Variant is -1 for the clean stream, else the damage variant.</summary>
public sealed record HeaderReference(string FileName, int Variant, int Chunks, int Nals, int AccessUnits, int Bytes, string Sha1);

public static class HeaderReferences
{
    public const string CleanTableFile = "headers.tsv";
    public const string DamagedTableFile = "headers-damaged.tsv";

    private static readonly Lazy<IReadOnlyDictionary<string, HeaderReference>> _clean = new(LoadClean);
    private static readonly Lazy<IReadOnlyList<HeaderReference>> _damaged = new(LoadDamaged);

    /// <summary>Clean stream dumps, by file name.</summary>
    public static IReadOnlyDictionary<string, HeaderReference> Clean => _clean.Value;

    /// <summary>Damaged stream dumps, in table order.</summary>
    public static IReadOnlyList<HeaderReference> Damaged => _damaged.Value;

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
                throw new InvalidDataException($"The reference build has no header dump for {c[0]} ({c[^1]}); regenerate {table}.");
            }
            yield return c;
        }
    }

    private static IReadOnlyDictionary<string, HeaderReference> LoadClean() =>
        // Columns: file, nals, aus, bytes, sha1.
        Rows(CleanTableFile).ToDictionary(c => c[0],
            c => new HeaderReference(c[0], -1, -1, int.Parse(c[1]), int.Parse(c[2]), int.Parse(c[3]), c[4]));

    private static IReadOnlyList<HeaderReference> LoadDamaged() =>
        // Columns: file, variant, chunks, nals, aus, bytes, sha1.
        Rows(DamagedTableFile).Select(c => new HeaderReference(c[0], int.Parse(c[1]), int.Parse(c[2]), int.Parse(c[3]),
            int.Parse(c[4]), int.Parse(c[5]), c[6])).ToList();
}

public class HeaderDumpTests
{
    private const string ResEnvironmentVariable = "OPENH264_RES";

    private readonly ITestOutputHelper _output;

    public HeaderDumpTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> OtherVectorData =>
        DecoderConformanceVectors.All.Where(v => !v.IsConstrainedBaseline).Select(v => new object[] { v });

    [Fact]
    public void Tables_HaveEveryUpstreamVector()
    {
        var files = DecoderConformanceVectors.All.Select(v => v.FileName).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(files, HeaderReferences.Clean.Keys.OrderBy(f => f, StringComparer.Ordinal));
        Assert.Equal(files, HeaderReferences.Damaged.Select(r => r.FileName).Distinct().OrderBy(f => f, StringComparer.Ordinal));
        Assert.All(HeaderReferences.Clean.Values.Concat(HeaderReferences.Damaged), r => Assert.Matches("^[0-9a-f]{40}$", r.Sha1));
    }

    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_HeaderDumpMatchesNative(DecoderVector vector)
    {
        AssertCleanDumpMatches(vector, DecoderConformanceVectors.PathOf(vector));
    }

    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_DamagedHeaderDumpsMatchNative(DecoderVector vector)
    {
        AssertDamagedDumpsMatch(vector, DecoderConformanceVectors.PathOf(vector));
    }

    [Theory]
    [MemberData(nameof(OtherVectorData))]
    public void OtherVector_HeaderDumpsMatchNative(DecoderVector vector)
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
        AssertDumpMatches(HeaderReferences.Clean[vector.FileName], HeaderDump.SplitChunks(stream),
            $"h264ref headerdump {path}");
    }

    private void AssertDamagedDumpsMatch(DecoderVector vector, string path)
    {
        byte[] stream = File.ReadAllBytes(path);
        List<byte[]> clean = HeaderDump.SplitChunks(stream);
        var expected = HeaderReferences.Damaged.Where(r => r.FileName == vector.FileName).ToList();
        Assert.NotEmpty(expected);
        foreach (HeaderReference r in expected)
        {
            List<byte[]> damaged = HeaderDump.DamageChunks(clean, (uint)stream.Length, r.Variant);
            Assert.True(damaged.Count == r.Chunks,
                $"{vector.FileName} variant {r.Variant}: damaged to {damaged.Count} chunks, the reference build to {r.Chunks}.");
            AssertDumpMatches(r, damaged, $"h264ref headerdump-damaged {path} {r.Variant}");
        }
    }

    private void AssertDumpMatches(HeaderReference expected, List<byte[]> chunks, string nativeCommand)
    {
        Assert.True(HeaderDump.RunChunks(chunks, out string dump, out int nals, out int aus),
            "The decoder context failed to initialise.");
        string sha1 = HeaderDump.Sha1Hex(dump);
        if (sha1 == expected.Sha1 && nals == expected.Nals && aus == expected.AccessUnits && dump.Length == expected.Bytes)
        {
            return;
        }

        string label = expected.Variant < 0 ? expected.FileName : $"{expected.FileName}.damaged{expected.Variant}";
        string dir = Path.Combine(AppContext.BaseDirectory, "headerdumps");
        Directory.CreateDirectory(dir);
        string outFile = Path.Combine(dir, label + ".txt");
        File.WriteAllText(outFile, dump);
        _output.WriteLine($"C# dump written to {outFile}; compare with `{nativeCommand}`.");
        Assert.Fail($"{label}: header dump differs from the reference build. " +
            $"Expected nals={expected.Nals} aus={expected.AccessUnits} bytes={expected.Bytes} sha1={expected.Sha1}, " +
            $"got nals={nals} aus={aus} bytes={dump.Length} sha1={sha1}. C# dump: {outFile}");
    }
}
