//-----------------------------------------------------------------------------
// Filename: PreprocessFixtures.cs
//
// Description: The pieces the E7 preprocessing fixtures share, mirroring the
// helpers of refharness/src/kernels_processing.cpp: planes with a margin
// before and after, I420 pictures, the reshaping of a plane's content, the
// pair of a current picture and a reference built macroblock by macroblock
// from content classes, the VAA statistics of such a pair, and the scene
// change result's noise and hash. Every buffer is filled from the generator
// in the C harness's order.
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

namespace SIPSorcery.H264.UnitTest;

/// <summary>YuvSize: the luma size and the two strides.</summary>
internal readonly record struct FixtureYuvSize(int W, int H, int StrideY, int StrideUV);

/// <summary>Plane: a picture plane with kVaaMargin before and after it, filled with noise.</summary>
internal sealed unsafe class FixturePlane : IDisposable
{
    public const int Margin = 512;

    private readonly NativeBuffer _buf;

    public FixturePlane(ref FixtureRng r, int w, int h, int stride)
    {
        W = w;
        H = h;
        Stride = stride;
        _buf = new NativeBuffer(stride * h + 2 * Margin);
        r.Fill(_buf.Span);
    }

    public int W { get; }
    public int H { get; }
    public int Stride { get; }
    public byte* P => _buf.Ptr + Margin;
    public Span<byte> Span => _buf.Span;

    public void HashInto(FixtureHasher h) => h.Add(_buf.Span);

    public void Dispose() => _buf.Dispose();
}

/// <summary>Yuv: an I420 picture, U and V of half the width and height.</summary>
internal sealed unsafe class FixtureYuv : IDisposable
{
    public readonly FixturePlane[] Pl = new FixturePlane[3];

    public FixtureYuv(ref FixtureRng r, FixtureYuvSize s)
    {
        Pl[0] = new FixturePlane(ref r, s.W, s.H, s.StrideY);
        Pl[1] = new FixturePlane(ref r, s.W >> 1, s.H >> 1, s.StrideUV);
        Pl[2] = new FixturePlane(ref r, s.W >> 1, s.H >> 1, s.StrideUV);
    }

    public void HashInto(FixtureHasher h)
    {
        foreach (FixturePlane q in Pl)
        {
            q.HashInto(h);
        }
    }

    public SPixMap Map()
    {
        SPixMap m = default;
        for (int c = 0; c < 3; c++)
        {
            m.pPixel[c] = Pl[c].P;
            m.iStride[c] = Pl[c].Stride;
        }
        m.iSizeInBits = 8;
        m.sRect.iRectWidth = Pl[0].W;
        m.sRect.iRectHeight = Pl[0].H;
        m.eFormat = EVideoFormat.VIDEO_FORMAT_I420;
        return m;
    }

    public void Dispose()
    {
        foreach (FixturePlane q in Pl)
        {
            q.Dispose();
        }
    }
}

internal static unsafe class PreprocessFixtures
{
    // kMixes: Next() % 16 to a macroblock's content class.
    private static readonly int[,] Mixes =
    {
        { 4, 7, 10, 13, 16 }, { 9, 13, 14, 15, 16 }, { 16, 16, 16, 16, 16 }, { 0, 0, 0, 0, 16 },
    };

    public static byte Clip255(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    /// <summary>Shape: kind 0 noise, 1 near 128, 2 flat, 3 only 0 and 255, margins included.</summary>
    public static void Shape(ref FixtureRng r, FixturePlane q, int kind)
    {
        Span<byte> s = q.Span;
        for (int i = 0; i < s.Length; i++)
        {
            switch (kind)
            {
                case 1:
                    s[i] = (byte)(128 + (int)(r.Next() % 17) - 8);
                    break;
                case 2:
                    s[i] = 77;
                    break;
                case 3:
                    s[i] = (byte)((s[i] & 1) != 0 ? 255 : 0);
                    break;
            }
        }
    }

    private static void ApplyClass(ref FixtureRng r, int cls, FixturePlane cur, FixturePlane refPic, int x0, int y0, int n)
    {
        for (int y = y0; y < y0 + n && y < cur.H; y++)
        {
            for (int x = x0; x < x0 + n && x < cur.W; x++)
            {
                int c = cur.P[y * cur.Stride + x];
                byte* d = refPic.P + y * refPic.Stride + x;
                switch (cls)
                {
                    case 0:
                        *d = (byte)c;
                        break;
                    case 1:
                        *d = Clip255(c + (int)(r.Next() % 5) - 2);
                        break;
                    case 2:
                        *d = Clip255(c + 3 + (int)(r.Next() % 3) - 1);
                        break;
                    case 3:
                        *d = Clip255(c + (int)(r.Next() % 25) - 12);
                        break;
                }
            }
        }
    }

    /// <summary>MakeYuvPair: the current picture, the reference, then each macroblock's class.</summary>
    public static void MakeYuvPair(ref FixtureRng r, FixtureYuvSize s, int mix, out FixtureYuv cur, out FixtureYuv refPic)
    {
        cur = new FixtureYuv(ref r, s);
        refPic = new FixtureYuv(ref r, s);
        for (int my = 0; my < (s.H + 15) >> 4; my++)
        {
            for (int mx = 0; mx < (s.W + 15) >> 4; mx++)
            {
                int v = (int)(r.Next() % 16);
                int cls = 0;
                while (v >= Mixes[mix, cls])
                {
                    cls++;
                }
                ApplyClass(ref r, cls, cur.Pl[0], refPic.Pl[0], mx * 16, my * 16, 16);
                ApplyClass(ref r, cls, cur.Pl[1], refPic.Pl[1], mx * 8, my * 8, 8);
                ApplyClass(ref r, cls, cur.Pl[2], refPic.Pl[2], mx * 8, my * 8, 8);
            }
        }
    }

    /// <summary>RunVaa: VAACalcSadSsdBgd_c on the luma of the pair.</summary>
    public static void RunVaa(FixtureYuv cur, FixtureYuv refPic, FixtureYuvSize s, vaacalc_unittest.Outputs o)
    {
        int frameSad = o.FrameSad;
        WelsVP.VAACalcSadSsdBgd_c(cur.Pl[0].P, refPic.Pl[0].P, s.W, s.H, s.StrideY, &frameSad, o.Sad8x8, o.Sum16x16,
            o.SqSum16x16, o.Ssd16x16, o.Sd8x8, o.Mad8x8);
        o.FrameSad = frameSad;
    }

    public static int MbCount(int w, int h) => ((w + 15) >> 4) * ((h + 15) >> 4);

    public static long Noise64(ref FixtureRng r)
    {
        uint hi = r.Next();
        return (long)(((ulong)hi << 32) | r.Next());
    }

    public static SSceneChangeResult NoiseScdResult(ref FixtureRng r, byte* pStatic)
    {
        SSceneChangeResult s = default;
        s.eSceneChangeIdc = (ESceneChangeIdc)(r.Next() % 3);
        s.iMotionBlockNum = (int)r.Next();
        s.iFrameComplexity = Noise64(ref r);
        s.pStaticBlockIdc = pStatic;
        s.sScrollResult.sMaskRect.iRectTop = (int)r.Next();
        s.sScrollResult.sMaskRect.iRectLeft = (int)r.Next();
        s.sScrollResult.sMaskRect.iRectWidth = (int)r.Next();
        s.sScrollResult.sMaskRect.iRectHeight = (int)r.Next();
        s.sScrollResult.bMaskInfoAvailable = (r.Next() & 1) != 0;
        s.sScrollResult.iScrollMvX = (int)r.Next();
        s.sScrollResult.iScrollMvY = (int)r.Next();
        s.sScrollResult.bScrollDetectFlag = (r.Next() & 1) != 0;
        return s;
    }

    public static void HashScdResult(FixtureHasher h, in SSceneChangeResult s, byte* pStatic)
    {
        h.AddI32((int)s.eSceneChangeIdc);
        h.AddI32(s.iMotionBlockNum);
        h.AddI64(s.iFrameComplexity);
        h.AddI32(s.pStaticBlockIdc == pStatic ? 1 : 0);
        h.AddI32(s.sScrollResult.sMaskRect.iRectTop);
        h.AddI32(s.sScrollResult.sMaskRect.iRectLeft);
        h.AddI32(s.sScrollResult.sMaskRect.iRectWidth);
        h.AddI32(s.sScrollResult.sMaskRect.iRectHeight);
        h.AddI32(s.sScrollResult.bMaskInfoAvailable ? 1 : 0);
        h.AddI32(s.sScrollResult.iScrollMvX);
        h.AddI32(s.sScrollResult.iScrollMvY);
        h.AddI32(s.sScrollResult.bScrollDetectFlag ? 1 : 0);
    }
}
