//-----------------------------------------------------------------------------
// Filename: DecodeDamagedTests.cs
//
// Description: The full decoder API over damaged copies of each vector, against
// the reference build's decode-damaged table (reference/decode-damaged.tsv,
// `h264ref decode-damaged`). The copies are the same seeded DamageChunks
// variants the header and macroblock dumps use: chunks dropped, truncated or
// bit-flipped. They reach what no clean vector does: error concealment
// (ERROR_CON_SLICE_COPY, as the harness sets it), the DECODING_STATE error
// returns, and ResetDecoder after a DPB that runs out of pictures
// (dsOutOfMemory). Each variant must match the reference build in chunk,
// frame and error counts, the first failing state, the OR of all failing
// states, and the SHA1 of the output. The exception is a variant whose damage
// reaches a tool the port leaves out (KnownDivergences, with the reason).
//
// The 35 Constrained Baseline vectors are committed and always run. The other
// 16 use CABAC, B slices or High profile tools, which are not ported, so their
// damaged copies only have to decode without crashing; they run when
// OPENH264_RES points at an openh264 res/ directory.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (D6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

public sealed record DamagedDecodeReference(
    string FileName,
    int Variant,
    int Chunks,
    int Frames,
    int Errors,
    int FirstErrorState,
    int ErrorStates,
    string Sha1);

public static class DamagedDecodeReferences
{
    public const string TableFile = "decode-damaged.tsv";

    private static readonly Lazy<IReadOnlyList<DamagedDecodeReference>> _all = new(Load);

    public static IReadOnlyList<DamagedDecodeReference> All => _all.Value;

    private static int Hex(string s) => int.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static IReadOnlyList<DamagedDecodeReference> Load()
    {
        // Columns: file, variant, chunks, frames, errors, first, states, sha1. '#' lines are comments.
        var rows = new List<DamagedDecodeReference>();
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, TableFile)))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] c = line.Split('\t');
            if (c[7] == "MISSING" || c[7] == "INITFAIL")
            {
                throw new InvalidDataException($"{TableFile}: the reference build has no result for {c[0]}; regenerate it.");
            }
            rows.Add(new DamagedDecodeReference(c[0], int.Parse(c[1]), int.Parse(c[2]), int.Parse(c[3]), int.Parse(c[4]),
                Hex(c[5]), Hex(c[6]), c[7]));
        }
        return rows;
    }
}

public class DecodeDamagedTests
{
    private const string ResEnvironmentVariable = "OPENH264_RES";
    /// <summary>When set to a directory, a variant whose output differs is written there as I420 for comparison.</summary>
    private const string DumpEnvironmentVariable = "H264_DUMP_DIR";
    private const int dsOutOfMemory = 0x4000;

    /// <summary>
    /// Variants where the damage takes the stream into a tool the port leaves out, so the port
    /// cannot match upstream. Each one still has to decode without crashing, with the same chunk
    /// and frame counts, and must still differ: once the tool is ported, its entry has to go.
    /// </summary>
    private static readonly Dictionary<(string File, int Variant), string> KnownDivergences = new()
    {
        // `h264ref trace-damaged` vs DecoderHash.TraceChunks with H264REF_TRACE_LEVEL=8: the bit
        // flips in chunk 19 make its slice_type B. Upstream decodes it as a B slice even in this
        // Baseline stream; the port has no B slices, so it rejects the slice (dsRefLost) and
        // conceals that one non-reference picture. Every other output frame matches.
        { ("NRF_MW_E.264", 2), "a bit flip turns a P slice into a B slice, and B slices are not ported" },
    };

    private readonly ITestOutputHelper _output;

    public DecodeDamagedTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<object[]> OtherVectorData =>
        DecoderConformanceVectors.All.Where(v => !v.IsConstrainedBaseline).Select(v => new object[] { v });

    [Fact]
    public void Table_HasThreeVariantsOfEveryUpstreamVector()
    {
        var files = DecoderConformanceVectors.All.Select(v => v.FileName).OrderBy(f => f, StringComparer.Ordinal).ToList();
        Assert.Equal(files, DamagedDecodeReferences.All.Select(r => r.FileName).Distinct().OrderBy(f => f, StringComparer.Ordinal));
        Assert.All(files, f => Assert.Equal(new[] { 0, 1, 2 }, DamagedDecodeReferences.All.Where(r => r.FileName == f).Select(r => r.Variant)));
        Assert.All(DamagedDecodeReferences.All, r => Assert.Matches("^[0-9a-f]{40}$", r.Sha1));
    }

    [Fact]
    public void Table_ReachesResetDecoderOnConstrainedBaselineVectors()
    {
        // ResetDecoder (dsOutOfMemory) is the recovery the port has to carry over
        // from DecodeFrame2; at least one committed vector's variant must reach it,
        // or the check below would not cover it.
        var cb = DecoderConformanceVectors.ConstrainedBaseline.Select(v => v.FileName).ToHashSet();
        Assert.Contains(DamagedDecodeReferences.All, r => cb.Contains(r.FileName) && (r.ErrorStates & dsOutOfMemory) != 0);
    }

    [Theory]
    [MemberData(nameof(DecoderConformanceVectors.ConstrainedBaselineData), MemberType = typeof(DecoderConformanceVectors))]
    public void ConstrainedBaselineVector_DamagedDecodesMatchNative(DecoderVector vector)
    {
        string path = DecoderConformanceVectors.PathOf(vector);
        byte[] stream = File.ReadAllBytes(path);
        List<byte[]> clean = HeaderDump.SplitChunks(stream);
        var expected = DamagedDecodeReferences.All.Where(r => r.FileName == vector.FileName).ToList();
        Assert.NotEmpty(expected);
        foreach (DamagedDecodeReference e in expected)
        {
            List<byte[]> damaged = HeaderDump.DamageChunks(clean, (uint)stream.Length, e.Variant);
            DecodeResult r = DecoderHash.HashChunks(damaged);
            string where = $"{vector.FileName} variant {e.Variant} (h264ref hash-damaged {path} {e.Variant})";
            if (e.Sha1 != r.Sha1 && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(DumpEnvironmentVariable)))
            {
                // Compare with h264ref yuv-damaged <file> <variant> <out.yuv>.
                string yuv = Path.Combine(Environment.GetEnvironmentVariable(DumpEnvironmentVariable), $"{vector.FileName}.{e.Variant}.cs.yuv");
                DecoderHash.DecodeToYuv(damaged, yuv);
                // Compare with h264ref trace-damaged <file> <variant> (H264REF_TRACE_LEVEL=8 adds the logs on both sides).
                File.WriteAllText(Path.ChangeExtension(yuv, ".trace.tsv"), DecoderHash.TraceChunks(damaged));
                _output.WriteLine($"Wrote the port's output to {yuv} and its call trace next to it.");
            }
            Assert.True(e.Chunks == r.Chunks, $"{where}: {r.Chunks} chunks, the reference build {e.Chunks}.");
            Assert.True(e.Frames == r.Frames, $"{where}: {r.Frames} frames, the reference build {e.Frames}.");
            if (KnownDivergences.TryGetValue((vector.FileName, e.Variant), out string reason))
            {
                _output.WriteLine($"{where}: known divergence, {reason}.");
                Assert.True(e.Sha1 != r.Sha1, $"{where}: now matches the reference build; remove it from KnownDivergences ({reason}).");
                continue;
            }
            Assert.True(e.Errors == r.ErrorChunks, $"{where}: {r.ErrorChunks} failed calls, the reference build {e.Errors}.");
            Assert.True(e.FirstErrorState == r.FirstErrorState, $"{where}: first failure 0x{r.FirstErrorState:x}, the reference build 0x{e.FirstErrorState:x}.");
            Assert.True(e.ErrorStates == r.ErrorStates, $"{where}: failure states 0x{r.ErrorStates:x}, the reference build 0x{e.ErrorStates:x}.");
            Assert.True(e.Sha1 == r.Sha1, $"{where}: output SHA1 {r.Sha1}, the reference build {e.Sha1}.");
        }
    }

    [Theory]
    [MemberData(nameof(OtherVectorData))]
    public void OtherVector_CleanAndDamagedDecodesDoNotCrash(DecoderVector vector)
    {
        string res = Environment.GetEnvironmentVariable(ResEnvironmentVariable);
        if (string.IsNullOrEmpty(res))
        {
            _output.WriteLine($"{ResEnvironmentVariable} is not set; {vector.FileName} is not committed, nothing to check.");
            return;
        }
        string path = Path.Combine(res, vector.FileName);
        Assert.True(File.Exists(path), $"{path} not found ({ResEnvironmentVariable}={res}).");
        byte[] stream = File.ReadAllBytes(path);
        List<byte[]> clean = HeaderDump.SplitChunks(stream);

        DecodeResult c = DecoderHash.HashChunks(clean);
        _output.WriteLine($"clean: {c.Frames} frames (upstream {vector.Frames}), {c.ErrorChunks} failed calls, states 0x{c.ErrorStates:x}, " +
            $"SHA1 {(c.Sha1 == vector.Sha1 ? "matches upstream" : "differs from upstream")}.");
        foreach (DamagedDecodeReference e in DamagedDecodeReferences.All.Where(r => r.FileName == vector.FileName))
        {
            DecodeResult r = DecoderHash.HashChunks(HeaderDump.DamageChunks(clean, (uint)stream.Length, e.Variant));
            _output.WriteLine($"variant {e.Variant}: {r.Frames} frames (reference {e.Frames}), {r.ErrorChunks} failed calls (reference {e.Errors}), " +
                $"SHA1 {(r.Sha1 == e.Sha1 ? "matches" : "differs")}.");
        }
    }
}
