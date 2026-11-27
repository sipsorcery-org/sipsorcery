//-----------------------------------------------------------------------------
// Filename: EncoderFixtures.cs
//
// Description: The input layouts the encoder kernel fixtures share with the C
// harness (thirdparty/openh264-dotnet-port/refharness/src/kernels_encoder.cpp):
// coefficient buffers with guard entries, random planes, the packed-prediction
// predictor fixture and the combined kernels' source blocks. Every draw must
// come in the same order as on the C side.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E2).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.H264.UnitTest;

internal static unsafe class EncoderFixtures
{
    /// <summary>The fixture's real quantiser rows (kQps in kernels_encoder.cpp).</summary>
    public static readonly int[] Qps = { 0, 12, 26, 39, 51 };

    /// <summary>
    /// A coefficient: uniform in [-range, range], or any short when range is
    /// 32768 (fixture Coeff).
    /// </summary>
    public static short Coeff(ref FixtureRng r, int range) =>
        range >= 32768 ? (short)(r.Next() >> 16) : (short)((int)(r.Next() % (uint)(2 * range + 1)) - range);

    /// <summary>A random byte buffer (fixture Plane).</summary>
    public static NativeBuffer Plane(ref FixtureRng r, int length)
    {
        var b = new NativeBuffer(length);
        r.Fill(b.Span);
        return b;
    }

    /// <summary>
    /// fixture PredHash: a random plane of n + 2 rows, the neighbours' block at
    /// row 1, column 4; the packed n x n prediction with 16 guard bytes either
    /// side. The hash covers the plane and the prediction.
    /// </summary>
    public static string PredHash(delegate*<byte*, byte*, int, void> f, int n, int stride, int seed)
    {
        var r = new FixtureRng((uint)seed);
        using var plane = Plane(ref r, stride * (n + 2));
        using var pred = Plane(ref r, n * n + 32);

        f(pred.Ptr + 16, plane.Ptr + stride + 4, stride);

        using var h = new FixtureHasher();
        h.Add(plane.Span);
        h.Add(pred.Span);
        return h.Hex();
    }

    /// <summary>
    /// fixture FlattenDec, the combined kernels' target 4: the neighbours made
    /// flat (the row above, from the corner, and the column to the left all set
    /// to the sample above), so every mode predicts the same block and the
    /// costs tie.
    /// </summary>
    public static void FlattenDec(byte* pDec, int decStride, int n)
    {
        byte c = pDec[-decStride];
        for (int x = -1; x < n; x++)
        {
            pDec[-decStride + x] = c;
        }
        for (int y = 0; y < n; y++)
        {
            pDec[y * decStride - 1] = c;
        }
    }

    /// <summary>
    /// fixture FillEnc: the combined kernels' source block from the neighbours
    /// of the reconstructed block at pDec. Target 0 copies the row above down,
    /// 1 the column to the left across, 2 (and 4, after FlattenDec) fills with
    /// the sample above, 3 is random; then noise in [-noise, noise] (clipped).
    /// </summary>
    public static void FillEnc(ref FixtureRng r, byte* pEnc, int encStride, byte* pDec, int decStride, int n, int target, int noise)
    {
        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                int v = target switch
                {
                    0 => pDec[-decStride + x],
                    1 => pDec[y * decStride - 1],
                    2 or 4 => pDec[-decStride],
                    _ => r.NextByte(),
                };
                if (noise != 0)
                {
                    v += (int)(r.Next() % (uint)(2 * noise + 1)) - noise;
                }
                pEnc[y * encStride + x] = (byte)Math.Clamp(v, 0, 255);
            }
        }
    }
}

/// <summary>
/// fixture CoeffBuf: n coefficients with Guard entries either side, all drawn
/// with Coeff. The hash covers the whole buffer.
/// </summary>
internal sealed unsafe class CoeffBuffer : IDisposable
{
    public const int Guard = 4;

    private readonly NativeBuffer _buffer;

    public CoeffBuffer(ref FixtureRng r, int n, int range)
    {
        _buffer = new NativeBuffer((n + 2 * Guard) * sizeof(short));
        short* p = (short*)_buffer.Ptr;
        for (int i = 0; i < n + 2 * Guard; i++)
        {
            p[i] = EncoderFixtures.Coeff(ref r, range);
        }
    }

    /// <summary>The first coefficient, after the leading guard.</summary>
    public short* Data => (short*)_buffer.Ptr + Guard;

    public ReadOnlySpan<byte> Bytes => _buffer.Span;

    public void Dispose() => _buffer.Dispose();
}
