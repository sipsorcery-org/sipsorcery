//-----------------------------------------------------------------------------
// Filename: mc_simd.cs
//
// Description: SIMD versions of the motion compensation kernels in mc.cs
// (P5.3), for the slots upstream's X86_ASM and HAVE_NEON blocks fill in
// InitMcFunc. Not a port: upstream's are assembly. Each gives what its C kernel
// gives, for any input (mc_simd_unittest.cs), and reads only the samples the C
// kernel reads. As for the C kernels, the destination must not overlap the
// source.
//
// The 6-tap filter p0 - 5 p1 + 20 p2 + 20 p3 - 5 p4 + p5 of 8-bit samples lies
// in -2550..10710, so it is exact in 16-bit lanes, and (x + 16) >> 5 with a
// saturating pack to bytes is WelsClip1's. The centre position filters the
// columns first, as McHorVer22_c does, keeping the 16-bit results, then filters
// those along the rows in 32 bits; (x + 512) >> 10 lies in -210..464, so the
// packs that follow saturate exactly as WelsClip1 does. Chroma weights add up to
// 64, so its sums fit 16 bits.
//
// Blocks are done in strips of 16, 8 or 4 columns. When the width isn't made
// of those, as the encoder's 17 and 9 aren't, the last strip overlaps the one
// before it and writes the same values again. Narrower blocks, and the centre
// position past the 17 columns McHorVer22_c's buffer holds, go to the C kernels,
// as do 2-wide chroma blocks. The quarter-pel positions average two half-pel or
// full-pel predictions just as the C kernels do.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.3).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using static SIPSorcery.H264.Simd;

// A strip of a block: source, its stride, destination, its stride, height.
using unsafe PWelsMcStripFunc = delegate*<byte*, int, byte*, int, int, void>;
using unsafe PWelsMcWidthHeightSimdFunc = delegate*<byte*, int, byte*, int, int, int, void>;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    //--------------------the filters, lane by lane------------------//

    /// <summary>a - 5b + 20c + 20d - 5e + f, exact in 16 bits for 8-bit samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Tap6(Vector128<short> a, Vector128<short> b, Vector128<short> c,
                                         Vector128<short> d, Vector128<short> e, Vector128<short> f) =>
        (a + f) + ((((c + d) << 2) - (b + e)) * (short)5);

    /// <summary>(x + 16) >> 5.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Round5(Vector128<short> x) =>
        Vector128.ShiftRightArithmetic(x + Vector128.Create((short)16), 5);

    /// <summary>
    /// The 6-tap filter of the 16-bit results of the column pass, then (x + 512) >> 10, in 32
    /// bits, as HorFilterInput16bit_c and McHorVer22_c do.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> Tap6Round10(Vector128<short> t0, Vector128<short> t1, Vector128<short> t2,
                                                Vector128<short> t3, Vector128<short> t4, Vector128<short> t5)
    {
        // Each pair sum lies in -5100..21420.
        Vector128<short> a05 = t0 + t5, a14 = t1 + t4, a23 = t2 + t3;
        if (Sse2.IsSupported)
        {
            // pmaddwd: a05 - 5 a14, and 20 a23 + 512, in 32 bits.
            Vector128<short> k1m5 = Vector128.Create(1, -5, 1, -5, 1, -5, 1, -5);
            Vector128<short> k20p1 = Vector128.Create(20, 1, 20, 1, 20, 1, 20, 1);
            Vector128<short> k512 = Vector128.Create((short)512);
            Vector128<int> lo = Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a05, a14), k1m5) +
                                Sse2.MultiplyAddAdjacent(Sse2.UnpackLow(a23, k512), k20p1);
            Vector128<int> hi = Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a05, a14), k1m5) +
                                Sse2.MultiplyAddAdjacent(Sse2.UnpackHigh(a23, k512), k20p1);
            return Sse2.PackSignedSaturate(Sse2.ShiftRightArithmetic(lo, 10), Sse2.ShiftRightArithmetic(hi, 10));
        }
        Vector128<int> l = Vector128.WidenLower(a05) - Vector128.WidenLower(a14) * 5 + Vector128.WidenLower(a23) * 20 +
                           Vector128.Create(512);
        Vector128<int> h = Vector128.WidenUpper(a05) - Vector128.WidenUpper(a14) * 5 + Vector128.WidenUpper(a23) * 20 +
                           Vector128.Create(512);
        return PackSs(Vector128.ShiftRightArithmetic(l, 10), Vector128.ShiftRightArithmetic(h, 10));
    }

    /// <summary>
    /// Runs a kernel's strips across a block at least 4 columns wide: 16 columns at a time where
    /// there is a 16-column strip, then 8, then 4, and for what is left, one more strip ending
    /// at the last column.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void McByStrips(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                   int32_t iWidth, int32_t iHeight,
                                   PWelsMcStripFunc pfStrip16, PWelsMcStripFunc pfStrip8, PWelsMcStripFunc pfStrip4)
    {
        int32_t x = 0;
        if (pfStrip16 != null)
        {
            for (; x + 16 <= iWidth; x += 16)
            {
                pfStrip16(pSrc + x, iSrcStride, pDst + x, iDstStride, iHeight);
            }
        }
        for (; x + 8 <= iWidth; x += 8)
        {
            pfStrip8(pSrc + x, iSrcStride, pDst + x, iDstStride, iHeight);
        }
        if (x + 4 <= iWidth)
        {
            pfStrip4(pSrc + x, iSrcStride, pDst + x, iDstStride, iHeight);
            x += 4;
        }
        if (x < iWidth)
        {
            if (iWidth >= 8)
            {
                pfStrip8(pSrc + iWidth - 8, iSrcStride, pDst + iWidth - 8, iDstStride, iHeight);
            }
            else
            {
                pfStrip4(pSrc + iWidth - 4, iSrcStride, pDst + iWidth - 4, iDstStride, iHeight);
            }
        }
    }

    //--------------------(2, 0): the row filter------------------//

    private static void McHorVer20Strip16_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                               int32_t iHeight)
    {
        for (int32_t i = 0; i < iHeight; i++)
        {
            Vector128<byte> a = Load16(pSrc - 2), b = Load16(pSrc - 1), c = Load16(pSrc);
            Vector128<byte> d = Load16(pSrc + 1), e = Load16(pSrc + 2), f = Load16(pSrc + 3);
            Vector128<short> lo = Tap6(Lo16(a), Lo16(b), Lo16(c), Lo16(d), Lo16(e), Lo16(f));
            Vector128<short> hi = Tap6(Hi16(a), Hi16(b), Hi16(c), Hi16(d), Hi16(e), Hi16(f));
            PackUs(Round5(lo), Round5(hi)).Store(pDst);
            pSrc += iSrcStride;
            pDst += iDstStride;
        }
    }

    private static void McHorVer20Strip8_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                              int32_t iHeight)
    {
        for (int32_t i = 0; i < iHeight; i++)
        {
            Vector128<short> v = Round5(Tap6(Load8W(pSrc - 2), Load8W(pSrc - 1), Load8W(pSrc), Load8W(pSrc + 1),
                                             Load8W(pSrc + 2), Load8W(pSrc + 3)));
            Store8(pDst, PackUs(v, v));
            pSrc += iSrcStride;
            pDst += iDstStride;
        }
    }

    /// <summary>Two rows at a time, one in each half of the lanes.</summary>
    private static void McHorVer20Strip4_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                              int32_t iHeight)
    {
        int32_t i = 0;
        for (; i + 2 <= iHeight; i += 2)
        {
            uint8_t* pNext = pSrc + iSrcStride;
            Vector128<short> v = Round5(Tap6(
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc - 2), Unsafe.ReadUnaligned<uint>(pNext - 2)),
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc - 1), Unsafe.ReadUnaligned<uint>(pNext - 1)),
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc), Unsafe.ReadUnaligned<uint>(pNext)),
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc + 1), Unsafe.ReadUnaligned<uint>(pNext + 1)),
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc + 2), Unsafe.ReadUnaligned<uint>(pNext + 2)),
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc + 3), Unsafe.ReadUnaligned<uint>(pNext + 3))));
            Vector128<byte> o = PackUs(v, v);
            Store4(pDst, o);
            Store4High(pDst + iDstStride, o);
            pSrc += 2 * iSrcStride;
            pDst += 2 * iDstStride;
        }
        if (i < iHeight)
        {
            Vector128<short> v = Round5(Tap6(
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc - 2), 0), Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc - 1), 0),
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc), 0), Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc + 1), 0),
                Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc + 2), 0), Rows4x2W(Unsafe.ReadUnaligned<uint>(pSrc + 3), 0)));
            Store4(pDst, PackUs(v, v));
        }
    }

    private static void McHorVer20_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        if (iWidth < 4)
        {
            McHorVer20_c(pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight);
            return;
        }
        McByStrips(pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight,
                   &McHorVer20Strip16_simd, &McHorVer20Strip8_simd, &McHorVer20Strip4_simd);
    }

    //--------------------(0, 2): the column filter------------------//

    /// <summary>Down the strip with the six rows the filter needs in registers.</summary>
    private static void McHorVer02Strip8_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                              int32_t iHeight)
    {
        uint8_t* p = pSrc - 2 * iSrcStride;
        Vector128<short> r0 = Load8W(p), r1 = Load8W(p + iSrcStride), r2 = Load8W(p + 2 * iSrcStride);
        Vector128<short> r3 = Load8W(p + 3 * iSrcStride), r4 = Load8W(p + 4 * iSrcStride);
        p += 5 * iSrcStride;
        for (int32_t i = 0; i < iHeight; i++)
        {
            Vector128<short> r5 = Load8W(p);
            Vector128<short> v = Round5(Tap6(r0, r1, r2, r3, r4, r5));
            Store8(pDst, PackUs(v, v));
            r0 = r1;
            r1 = r2;
            r2 = r3;
            r3 = r4;
            r4 = r5;
            p += iSrcStride;
            pDst += iDstStride;
        }
    }

    /// <summary>Two rows at a time, one in each half of the lanes.</summary>
    private static void McHorVer02Strip4_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                              int32_t iHeight)
    {
        uint8_t* p = pSrc - 2 * iSrcStride;
        uint u0 = Unsafe.ReadUnaligned<uint>(p), u1 = Unsafe.ReadUnaligned<uint>(p + iSrcStride);
        uint u2 = Unsafe.ReadUnaligned<uint>(p + 2 * iSrcStride), u3 = Unsafe.ReadUnaligned<uint>(p + 3 * iSrcStride);
        uint u4 = Unsafe.ReadUnaligned<uint>(p + 4 * iSrcStride);
        p += 5 * iSrcStride;
        int32_t i = 0;
        for (; i + 2 <= iHeight; i += 2)
        {
            uint u5 = Unsafe.ReadUnaligned<uint>(p), u6 = Unsafe.ReadUnaligned<uint>(p + iSrcStride);
            Vector128<short> v = Round5(Tap6(Rows4x2W(u0, u1), Rows4x2W(u1, u2), Rows4x2W(u2, u3),
                                             Rows4x2W(u3, u4), Rows4x2W(u4, u5), Rows4x2W(u5, u6)));
            Vector128<byte> o = PackUs(v, v);
            Store4(pDst, o);
            Store4High(pDst + iDstStride, o);
            u0 = u2;
            u1 = u3;
            u2 = u4;
            u3 = u5;
            u4 = u6;
            p += 2 * iSrcStride;
            pDst += 2 * iDstStride;
        }
        if (i < iHeight)
        {
            uint u5 = Unsafe.ReadUnaligned<uint>(p);
            Vector128<short> v = Round5(Tap6(Rows4x2W(u0, 0), Rows4x2W(u1, 0), Rows4x2W(u2, 0),
                                             Rows4x2W(u3, 0), Rows4x2W(u4, 0), Rows4x2W(u5, 0)));
            Store4(pDst, PackUs(v, v));
        }
    }

    private static void McHorVer02_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        if (iWidth < 4)
        {
            McHorVer02_c(pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight);
            return;
        }
        // Two 8-column strips keep fewer rows in registers than one of 16.
        McByStrips(pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight,
                   null, &McHorVer02Strip8_simd, &McHorVer02Strip4_simd);
    }

    //--------------------(2, 2): columns, then rows------------------//

    /// <summary>The column filter down an 8-column strip, kept at 16 bits as McHorVer22_c's iTmp is.</summary>
    private static void McHorVer22ColumnStrip8_simd(uint8_t* pSrc, int32_t iSrcStride, int16_t* pTmp, int32_t iTmpStride,
                                                    int32_t iHeight)
    {
        uint8_t* p = pSrc - 2 * iSrcStride;
        Vector128<short> r0 = Load8W(p), r1 = Load8W(p + iSrcStride), r2 = Load8W(p + 2 * iSrcStride);
        Vector128<short> r3 = Load8W(p + 3 * iSrcStride), r4 = Load8W(p + 4 * iSrcStride);
        p += 5 * iSrcStride;
        for (int32_t i = 0; i < iHeight; i++)
        {
            Vector128<short> r5 = Load8W(p);
            Tap6(r0, r1, r2, r3, r4, r5).Store(pTmp);
            r0 = r1;
            r1 = r2;
            r2 = r3;
            r3 = r4;
            r4 = r5;
            p += iSrcStride;
            pTmp += iTmpStride;
        }
    }

    /// <summary>8 results of the row filter from the column results at pTmp[0..12].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> McHorVer22Row8(int16_t* pTmp)
    {
        Vector128<short> v = Tap6Round10(Vector128.Load(pTmp), Vector128.Load(pTmp + 1), Vector128.Load(pTmp + 2),
                                         Vector128.Load(pTmp + 3), Vector128.Load(pTmp + 4), Vector128.Load(pTmp + 5));
        return PackUs(v, v);
    }

    [SkipLocalsInit]
    private static void McHorVer22_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        const int32_t kiMaxWidth = 17, kiMaxHeight = 17, kiTmpStride = 24;
        if (iWidth < 4 || iWidth > kiMaxWidth || iHeight > kiMaxHeight)
        {
            McHorVer22_c(pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight);
            return;
        }

        // The whole block's column results first, at columns -2 to iWidth + 2. The strips cover
        // them 8 at a time, the last one ending at the last column.
        int16_t* pTmp = stackalloc int16_t[kiTmpStride * kiMaxHeight];
        int32_t iColumns = iWidth + 5;
        for (int32_t x = 0; x < iColumns; x += 8)
        {
            int32_t c = Math.Min(x, iColumns - 8);
            McHorVer22ColumnStrip8_simd(pSrc - 2 + c, iSrcStride, pTmp + c, kiTmpStride, iHeight);
        }

        // Then each row. Output k reads pTmp[k..k+5]. Under 8 wide, the lanes past the width read
        // past the column results, and are not stored.
        for (int32_t i = 0; i < iHeight; i++)
        {
            int16_t* t = pTmp + i * kiTmpStride;
            if (iWidth >= 8)
            {
                for (int32_t x = 0; x < iWidth; x += 8)
                {
                    int32_t c = Math.Min(x, iWidth - 8);
                    Store8(pDst + c, McHorVer22Row8(t + c));
                }
            }
            else
            {
                Store4(pDst, McHorVer22Row8(t));
                Store4(pDst + iWidth - 4, McHorVer22Row8(t + iWidth - 4));
            }
            pDst += iDstStride;
        }
    }

    //--------------------copies and averages------------------//

    private static void McCopy_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride, int32_t iWidth,
                                    int32_t iHeight)
    {
        if (iWidth != 16)
        {
            // 8, 4 and 2 wide, the C kernel's word copies are as good.
            McCopy_c(pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight);
            return;
        }
        for (int32_t i = 0; i < iHeight; i++)
        {
            Load16(pSrc).Store(pDst);
            pSrc += iSrcStride;
            pDst += iDstStride;
        }
    }

    private static void PixelAvg_simd(uint8_t* pDst, int32_t iDstStride, uint8_t* pSrcA, int32_t iSrcAStride,
                                      uint8_t* pSrcB, int32_t iSrcBStride, int32_t iWidth, int32_t iHeight)
    {
        int32_t i;
        if (iWidth == 16)
        {
            for (i = 0; i < iHeight; i++)
            {
                Avg(Load16(pSrcA), Load16(pSrcB)).Store(pDst);
                pDst += iDstStride;
                pSrcA += iSrcAStride;
                pSrcB += iSrcBStride;
            }
            return;
        }
        if (iWidth == 8)
        {
            for (i = 0; i < iHeight; i++)
            {
                Store8(pDst, Avg(Load8(pSrcA), Load8(pSrcB)));
                pDst += iDstStride;
                pSrcA += iSrcAStride;
                pSrcB += iSrcBStride;
            }
            return;
        }
        // Any other width, column by column past the 16, 8 and 4 wide pieces. Not overlapped:
        // averaging in place, as C allows, would average twice.
        for (i = 0; i < iHeight; i++)
        {
            int32_t j = 0;
            for (; j + 16 <= iWidth; j += 16)
            {
                Avg(Load16(pSrcA + j), Load16(pSrcB + j)).Store(pDst + j);
            }
            if (j + 8 <= iWidth)
            {
                Store8(pDst + j, Avg(Load8(pSrcA + j), Load8(pSrcB + j)));
                j += 8;
            }
            if (j + 4 <= iWidth)
            {
                Store4(pDst + j, Avg(Load4(pSrcA + j), Load4(pSrcB + j)));
                j += 4;
            }
            for (; j < iWidth; j++)
            {
                pDst[j] = (uint8_t)((pSrcA[j] + pSrcB[j] + 1) >> 1);
            }
            pDst += iDstStride;
            pSrcA += iSrcAStride;
            pSrcB += iSrcBStride;
        }
    }

    /////////////////////luma MC//////////////////////////
    // As the C kernels: a half-pel or full-pel prediction averaged with another.

    [SkipLocalsInit]
    private static void McHorVer01_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiTmp = stackalloc uint8_t[256];
        McHorVer02_simd(pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer03_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiTmp = stackalloc uint8_t[256];
        McHorVer02_simd(pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, pSrc + iSrcStride, iSrcStride, uiTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer10_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiTmp = stackalloc uint8_t[256];
        McHorVer20_simd(pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, pSrc, iSrcStride, uiTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer11_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        McHorVer20_simd(pSrc, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer02_simd(pSrc, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, uiHorTmp, 16, uiVerTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer12_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        uint8_t* uiCtrTmp = stackalloc uint8_t[256];
        McHorVer02_simd(pSrc, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        McHorVer22_simd(pSrc, iSrcStride, uiCtrTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, uiVerTmp, 16, uiCtrTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer13_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        McHorVer20_simd(pSrc + iSrcStride, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer02_simd(pSrc, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, uiHorTmp, 16, uiVerTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer21_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiCtrTmp = stackalloc uint8_t[256];
        McHorVer20_simd(pSrc, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer22_simd(pSrc, iSrcStride, uiCtrTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, uiHorTmp, 16, uiCtrTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer23_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiCtrTmp = stackalloc uint8_t[256];
        McHorVer20_simd(pSrc + iSrcStride, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer22_simd(pSrc, iSrcStride, uiCtrTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, uiHorTmp, 16, uiCtrTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer30_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        McHorVer20_simd(pSrc, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, pSrc + 1, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer31_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        McHorVer20_simd(pSrc, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer02_simd(pSrc + 1, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, uiHorTmp, 16, uiVerTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer32_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        uint8_t* uiCtrTmp = stackalloc uint8_t[256];
        McHorVer02_simd(pSrc + 1, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        McHorVer22_simd(pSrc, iSrcStride, uiCtrTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, uiVerTmp, 16, uiCtrTmp, 16, iWidth, iHeight);
    }

    [SkipLocalsInit]
    private static void McHorVer33_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                        int32_t iWidth, int32_t iHeight)
    {
        uint8_t* uiHorTmp = stackalloc uint8_t[256];
        uint8_t* uiVerTmp = stackalloc uint8_t[256];
        McHorVer20_simd(pSrc + iSrcStride, iSrcStride, uiHorTmp, 16, iWidth, iHeight);
        McHorVer02_simd(pSrc + 1, iSrcStride, uiVerTmp, 16, iWidth, iHeight);
        PixelAvg_simd(pDst, iDstStride, uiHorTmp, 16, uiVerTmp, 16, iWidth, iHeight);
    }

    // pWelsMcFunc's layout, [x * 4 + y].
    private static readonly PWelsMcWidthHeightSimdFunc[] pWelsMcFunc_simd =
    [
        &McCopy_simd, &McHorVer01_simd, &McHorVer02_simd, &McHorVer03_simd,
        &McHorVer10_simd, &McHorVer11_simd, &McHorVer12_simd, &McHorVer13_simd,
        &McHorVer20_simd, &McHorVer21_simd, &McHorVer22_simd, &McHorVer23_simd,
        &McHorVer30_simd, &McHorVer31_simd, &McHorVer32_simd, &McHorVer33_simd,
    ];

    private static void McLuma_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                    int16_t iMvX, int16_t iMvY, int32_t iWidth, int32_t iHeight)
    {
        pWelsMcFunc_simd[((iMvX & 0x03) << 2) + (iMvY & 0x03)](pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight);
    }

    //--------------------chroma MC------------------//

    private static void McChromaWithFragMv_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                                int16_t iMvX, int16_t iMvY, int32_t iWidth, int32_t iHeight)
    {
        if (iWidth != 8 && iWidth != 4)
        {
            McChromaWithFragMv_c(pSrc, iSrcStride, pDst, iDstStride, iMvX, iMvY, iWidth, iHeight);
            return;
        }
        uint8_t* pABCD = (uint8_t*)&g_kuiABCD[iMvY & 0x07][iMvX & 0x07];
        Vector128<short> vA = Vector128.Create((short)pABCD[0]), vB = Vector128.Create((short)pABCD[1]);
        Vector128<short> vC = Vector128.Create((short)pABCD[2]), vD = Vector128.Create((short)pABCD[3]);
        Vector128<short> k32 = Vector128.Create((short)32);
        int32_t i = 0;

        if (iWidth == 8)
        {
            // Each row's samples are the next row's upper neighbours.
            Vector128<short> p0 = Load8W(pSrc), p1 = Load8W(pSrc + 1);
            for (; i < iHeight; i++)
            {
                pSrc += iSrcStride;
                Vector128<short> n0 = Load8W(pSrc), n1 = Load8W(pSrc + 1);
                Vector128<short> v = Vector128.ShiftRightLogical(vA * p0 + vB * p1 + vC * n0 + vD * n1 + k32, 6);
                Store8(pDst, PackUs(v, v));
                p0 = n0;
                p1 = n1;
                pDst += iDstStride;
            }
            return;
        }

        // 4 wide: two rows at a time, one in each half of the lanes.
        uint a0 = Unsafe.ReadUnaligned<uint>(pSrc), a1 = Unsafe.ReadUnaligned<uint>(pSrc + 1);
        for (; i + 2 <= iHeight; i += 2)
        {
            uint8_t* pRow1 = pSrc + iSrcStride, pRow2 = pRow1 + iSrcStride;
            uint b0 = Unsafe.ReadUnaligned<uint>(pRow1), b1 = Unsafe.ReadUnaligned<uint>(pRow1 + 1);
            uint c0 = Unsafe.ReadUnaligned<uint>(pRow2), c1 = Unsafe.ReadUnaligned<uint>(pRow2 + 1);
            Vector128<short> v = Vector128.ShiftRightLogical(
                vA * Rows4x2W(a0, b0) + vB * Rows4x2W(a1, b1) + vC * Rows4x2W(b0, c0) + vD * Rows4x2W(b1, c1) + k32, 6);
            Vector128<byte> o = PackUs(v, v);
            Store4(pDst, o);
            Store4High(pDst + iDstStride, o);
            a0 = c0;
            a1 = c1;
            pSrc = pRow2;
            pDst += 2 * iDstStride;
        }
        if (i < iHeight)
        {
            uint8_t* pRow1 = pSrc + iSrcStride;
            uint b0 = Unsafe.ReadUnaligned<uint>(pRow1), b1 = Unsafe.ReadUnaligned<uint>(pRow1 + 1);
            Vector128<short> v = Vector128.ShiftRightLogical(
                vA * Rows4x2W(a0, 0) + vB * Rows4x2W(a1, 0) + vC * Rows4x2W(b0, 0) + vD * Rows4x2W(b1, 0) + k32, 6);
            Store4(pDst, PackUs(v, v));
        }
    }

    private static void McChroma_simd(uint8_t* pSrc, int32_t iSrcStride, uint8_t* pDst, int32_t iDstStride,
                                      int16_t iMvX, int16_t iMvY, int32_t iWidth, int32_t iHeight)
    {
        if (0 == (iMvX & 0x07) && 0 == (iMvY & 0x07))
        {
            McCopy_simd(pSrc, iSrcStride, pDst, iDstStride, iWidth, iHeight);
        }
        else
        {
            McChromaWithFragMv_simd(pSrc, iSrcStride, pDst, iDstStride, iMvX, iMvY, iWidth, iHeight);
        }
    }
}
