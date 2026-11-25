//-----------------------------------------------------------------------------
// Filename: KernelFixtures.cs
//
// Description: Loads reference/kernels.tsv, the kernel and table fixtures that
// the C-only reference build writes (`h264ref kernels`, see
// thirdparty/openh264-dotnet-port/refharness/src/fixture.h), and provides the
// pieces both sides must share to regenerate each case's inputs: the xorshift32
// generator, the SHA1 hasher and the padded-plane layout.
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
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SIPSorcery.H264.UnitTest;

/// <summary>One row of kernels.tsv: group, kernel, case, sha1.</summary>
public sealed record KernelFixture(string Group, string Kernel, string Case, string Sha1)
{
    /// <summary>The case's key=value pairs, e.g. "w=16,h=16,seed=0".</summary>
    public int Param(string key)
    {
        foreach (string kv in Case.Split(','))
        {
            int eq = kv.IndexOf('=');
            if (eq > 0 && kv[..eq] == key)
            {
                return int.Parse(kv[(eq + 1)..]);
            }
        }
        throw new KeyNotFoundException($"No '{key}' in case '{Case}'.");
    }
}

public static class KernelFixtures
{
    public static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "kernels.tsv");

    public static IReadOnlyList<KernelFixture> All { get; } = Load();

    private static List<KernelFixture> Load()
    {
        var rows = new List<KernelFixture>();
        foreach (string line in File.ReadAllLines(FilePath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] f = line.Split('\t');
            if (f.Length != 4)
            {
                throw new InvalidDataException($"Bad row in {FilePath}: '{line}'.");
            }
            rows.Add(new KernelFixture(f[0], f[1], f[2], f[3]));
        }
        return rows;
    }

    /// <summary>Theory data for one group, as (kernel, case, sha1) strings so each row is its own test case.</summary>
    public static IEnumerable<object[]> Group(string group)
    {
        var rows = All.Where(r => r.Group == group).ToList();
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"No '{group}' rows in {FilePath}.");
        }
        return rows.Select(r => new object[] { r.Kernel, r.Case, r.Sha1 });
    }

    public static KernelFixture Row(string group, string kernel, string kase) =>
        All.Single(r => r.Group == group && r.Kernel == kernel && r.Case == kase);
}

/// <summary>xorshift32, identical to fixture::Rng in the C harness.</summary>
internal struct FixtureRng
{
    private uint _s;

    public FixtureRng(uint seed)
    {
        _s = seed ^ 0xA5A5A5A5u;
        if (_s == 0)
        {
            _s = 1;
        }
    }

    public uint Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 17;
        _s ^= _s << 5;
        return _s;
    }

    public byte NextByte() => (byte)(Next() >> 24);

    public void Fill(Span<byte> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = NextByte();
        }
    }
}

/// <summary>SHA1 over the bytes a kernel produced, identical to fixture::Hasher.</summary>
internal sealed class FixtureHasher : IDisposable
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

    public void Add(ReadOnlySpan<byte> data) => _hash.AppendData(data);

    public void AddI32(int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        _hash.AppendData(b);
    }

    public void AddI64(long value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, value);
        _hash.AppendData(b);
    }

    public string Hex() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public void Dispose() => _hash.Dispose();

    public static string Of(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA1.HashData(data));
}

/// <summary>A zeroed native buffer, so kernels can be handed stable pointers.</summary>
internal sealed unsafe class NativeBuffer : IDisposable
{
    public byte* Ptr { get; private set; }
    public int Length { get; }

    public NativeBuffer(int length)
    {
        Length = length;
        Ptr = (byte*)NativeMemory.AllocZeroed((nuint)Math.Max(length, 1));
    }

    public Span<byte> Span => new(Ptr, Length);

    public void Dispose()
    {
        NativeMemory.Free(Ptr);
        Ptr = null;
    }
}

/// <summary>
/// fixture.h's PaddedPlane: a w x h plane with `pad` bytes of border on every
/// side, plus 4 guard columns left and right and one guard row above and below,
/// filled from the generator.
/// </summary>
internal sealed unsafe class PaddedPlane : IDisposable
{
    public const int GuardCols = 4;

    public int W { get; }
    public int H { get; }
    public int Pad { get; }
    public int Stride { get; }
    public NativeBuffer Buffer { get; }

    public PaddedPlane(int w, int h, int pad, int extraStride, ref FixtureRng rng)
    {
        W = w;
        H = h;
        Pad = pad;
        Stride = w + 2 * pad + 2 * GuardCols + extraStride;
        Buffer = new NativeBuffer(Stride * (h + 2 * pad + 2));
        rng.Fill(Buffer.Span);
    }

    public byte* Origin => Buffer.Ptr + (1 + Pad) * Stride + GuardCols + Pad;

    public void Dispose() => Buffer.Dispose();
}
