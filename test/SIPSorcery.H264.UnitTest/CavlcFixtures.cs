//-----------------------------------------------------------------------------
// Filename: CavlcFixtures.cs
//
// Description: The inputs the CAVLC writer fixtures share with the C harness
// (thirdparty/openh264-dotnet-port/refharness/src/cavlc_encoder.cpp): the
// coefficient and motion vector draws, the bitstream buffers, the macroblock
// fixture and the writer context. Every draw must come in the same order as on
// the C side.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E3).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Runtime.InteropServices;
using SIPSorcery.H264.Encoder;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

internal static unsafe class CavlcFixtures
{
    public static readonly uint[] SubTypes = { SUB_MB_TYPE_8x8, SUB_MB_TYPE_8x4, SUB_MB_TYPE_4x8, SUB_MB_TYPE_4x4 };

    /// <summary>The P slice macroblock types the slice rows draw from.</summary>
    public static readonly uint[] PTypes =
    {
        MB_TYPE_SKIP, MB_TYPE_INTRA4x4, MB_TYPE_INTRA16x16, MB_TYPE_16x16, MB_TYPE_16x8, MB_TYPE_8x16, MB_TYPE_8x8,
    };

    /// <summary>The types WelsSpatialWriteMbPred writes.</summary>
    public static readonly uint[] PredTypes = { MB_TYPE_INTRA4x4, MB_TYPE_INTRA16x16, MB_TYPE_16x16, MB_TYPE_16x8, MB_TYPE_8x16 };

    /// <summary>By EWelsSliceType value, as the rows' slice parameter.</summary>
    public static readonly EWelsSliceType[] SliceTypes = { EWelsSliceType.P_SLICE, EWelsSliceType.B_SLICE, EWelsSliceType.I_SLICE };

    /// <summary>A symmetric draw in [-r, r] (fixture Sym).</summary>
    public static int Sym(ref FixtureRng g, int r) => (int)(g.Next() % (uint)(2 * r + 1)) - r;

    /// <summary>
    /// fixture Coef: non-zero with probability density/16, of magnitude 1..range.
    /// </summary>
    public static short Coef(ref FixtureRng g, int density, int range)
    {
        if ((int)(g.Next() % 16) >= density)
        {
            return 0;
        }
        int v = 1 + (int)(g.Next() % (uint)range);
        if ((g.Next() & 1) != 0)
        {
            v = -v;
        }
        return (short)v;
    }

    /// <summary>
    /// fixture FillMb: one macroblock of the given type into f and the slice's
    /// cache, with motion vectors mvp + d stored as int16_t.
    /// </summary>
    public static void FillMb(ref FixtureRng g, MbFixture f, SMbCache* pCache, uint uiMbType, int iRefs, int iMvRange,
                              int iDensity, int iRange)
    {
        SMB* mb = f.Mb;
        *mb = default;
        mb->uiMbType = uiMbType;
        mb->sMv = f.Mv;
        mb->pRefIndex = f.Ref;

        uint a = g.Next(), b = g.Next();
        int iCbp = IS_INTRA16x16(uiMbType) ? (int)(((b % 3) << 4) | ((a & 1) != 0 ? 15u : 0u))
                                           : (int)(((b % 3) << 4) | (a % 16));
        if (IS_SKIP(uiMbType))
        {
            iCbp = 0;
        }
        mb->uiCbp = (byte)iCbp;
        mb->uiLumaQp = (byte)(g.Next() % 52);
        pCache->uiLumaI16x16Mode = (byte)(g.Next() % 7);
        pCache->uiChmaI8x8Mode = (byte)(g.Next() % 7);
        for (int i = 0; i < 16; i++)
        {
            f.PrevFlag[i] = (g.Next() & 1) != 0;
            f.RemMode[i] = (sbyte)(g.Next() % 8);
        }
        for (int i = 0; i < 4; i++)
        {
            mb->uiSubMbType[i] = (byte)SubTypes[g.Next() % 4];
        }
        bool bRef0 = (g.Next() % 3) == 0;
        for (int i = 0; i < 4; i++)
        {
            f.Ref[i] = bRef0 ? (sbyte)0 : (sbyte)(g.Next() % (uint)iRefs);
        }

        bool b8x8 = IS_Inter_8x8(uiMbType);
        int kiRp = b8x8 ? Math.Min(iMvRange, 8191) : iMvRange;
        int kiRd = b8x8 ? Math.Min(iMvRange, 8191) : Math.Min(iMvRange, 16383);
        for (int i = 0; i < 16; i++)
        {
            ref SMVUnitXY p = ref pCache->sMbMvp[i];
            p.iMvX = (short)Sym(ref g, kiRp);
            f.Mv[i].iMvX = (short)(p.iMvX + Sym(ref g, kiRd));
            p.iMvY = (short)Sym(ref g, kiRp);
            f.Mv[i].iMvY = (short)(p.iMvY + Sym(ref g, kiRd));
        }

        for (int i = 0; i < 48; i++)
        {
            pCache->iNonZeroCoeffCount[i] = (sbyte)((int)(g.Next() % 18) - 1);
        }
        SDCTCoeff* dct = f.Dct;
        for (int k = 0; k < 16; k++)
        {
            for (int j = 0; j < 16; j++)
            {
                dct->iLumaBlock[k][j] = Coef(ref g, iDensity, iRange);
            }
        }
        for (int j = 0; j < 16; j++)
        {
            dct->iLumaI16x16Dc[j] = Coef(ref g, iDensity, iRange);
        }
        for (int k = 0; k < 8; k++)
        {
            for (int j = 0; j < 16; j++)
            {
                dct->iChromaBlock[k][j] = Coef(ref g, iDensity, iRange);
            }
        }
        for (int k = 0; k < 2; k++)
        {
            for (int j = 0; j < 4; j++)
            {
                dct->iChromaDc[k][j] = Coef(ref g, iDensity, iRange);
            }
        }

        // Each coded block's own count, over the coefficients the writer reads.
        int kiLumaEnd = IS_INTRA16x16(uiMbType) ? 14 : 15;
        for (int k = 0; k < 16; k++)
        {
            int n = 0;
            for (int j = 0; j <= kiLumaEnd; j++)
            {
                n += dct->iLumaBlock[k][j] != 0 ? 1 : 0;
            }
            pCache->iNonZeroCoeffCount[g_kuiCache48CountScan4Idx[k]] = (sbyte)n;
        }
        for (int k = 0; k < 8; k++)
        {
            int n = 0;
            for (int j = 0; j <= 14; j++)
            {
                n += dct->iChromaBlock[k][j] != 0 ? 1 : 0;
            }
            pCache->iNonZeroCoeffCount[(k < 4 ? 0 : 24) + g_kuiCache48CountScan4Idx[16 + (k & 3)]] = (sbyte)n;
        }

        pCache->pPrevIntra4x4PredModeFlag = f.PrevFlag;
        pCache->pRemIntra4x4PredModeFlag = f.RemMode;
        pCache->pDct = dct;
    }

    public static T* Alloc<T>() where T : unmanaged => (T*)NativeMemory.AllocZeroed((nuint)sizeof(T));
}

/// <summary>
/// fixture Bs: a random physical buffer with a writer over its first logical
/// bytes, and pre random bits already written.
/// </summary>
internal sealed unsafe class FixtureBs : IDisposable
{
    private readonly NativeBuffer _buf;

    public SBitStringAux* Bs { get; }

    public byte* Buf => _buf.Ptr;

    public int Length => _buf.Length;

    public FixtureBs(ref FixtureRng g, int physical, int logical, int pre)
    {
        _buf = new NativeBuffer(physical);
        g.Fill(_buf.Span);
        Bs = CavlcFixtures.Alloc<SBitStringAux>();
        InitBits(Bs, _buf.Ptr, logical);
        if (pre > 0)
        {
            BsWriteBits(Bs, pre, g.Next() >> (32 - pre));
        }
    }

    public void Hash(FixtureHasher h)
    {
        h.AddI32((int)(Bs->pCurBuf - Bs->pStartBuf));
        h.AddI32((int)Bs->uiCurBits);
        h.AddI32(Bs->iLeftBits);
        h.Add(_buf.Span);
    }

    public void Dispose()
    {
        NativeMemory.Free(Bs);
        _buf.Dispose();
    }
}

/// <summary>fixture MbFixture: everything one macroblock write reads.</summary>
internal sealed unsafe class MbFixture : IDisposable
{
    public SMB* Mb { get; } = CavlcFixtures.Alloc<SMB>();
    public SMVUnitXY* Mv { get; } = (SMVUnitXY*)NativeMemory.AllocZeroed(16, (nuint)sizeof(SMVUnitXY));
    public sbyte* Ref { get; } = (sbyte*)NativeMemory.AllocZeroed(4);
    public bool* PrevFlag { get; } = (bool*)NativeMemory.AllocZeroed(16);
    public sbyte* RemMode { get; } = (sbyte*)NativeMemory.AllocZeroed(16);
    public SDCTCoeff* Dct { get; } = CavlcFixtures.Alloc<SDCTCoeff>();

    public void Dispose()
    {
        NativeMemory.Free(Mb);
        NativeMemory.Free(Mv);
        NativeMemory.Free(Ref);
        NativeMemory.Free(PrevFlag);
        NativeMemory.Free(RemMode);
        NativeMemory.Free(Dct);
    }
}

/// <summary>
/// fixture WriterCtx: the function list from InitCoeffFunc, the DQ layer's PPS,
/// the context and the slice a macroblock write reads.
/// </summary>
internal sealed unsafe class WriterCtx : IDisposable
{
    public SWelsFuncPtrList* Funcs { get; } = CavlcFixtures.Alloc<SWelsFuncPtrList>();
    public SWelsPPS* Pps { get; } = CavlcFixtures.Alloc<SWelsPPS>();
    public SDqLayer* Dq { get; } = CavlcFixtures.Alloc<SDqLayer>();
    public sWelsEncCtx* Ctx { get; } = CavlcFixtures.Alloc<sWelsEncCtx>();
    public SSlice* Slice { get; } = CavlcFixtures.Alloc<SSlice>();

    public WriterCtx(EWelsSliceType eType, int iRefs, byte uiChromaQpOffset)
    {
        InitCoeffFunc(Funcs, 0, 0);
        Pps->uiChromaQpIndexOffset = uiChromaQpOffset;
        Dq->sLayerInfo.pPpsP = Pps;
        Ctx->pFuncList = Funcs;
        Ctx->pCurDqLayer = Dq;
        Ctx->eSliceType = eType;
        Slice->sSliceHeaderExt.sSliceHeader.eSliceType = eType;
        Slice->sSliceHeaderExt.sSliceHeader.uiNumRefIdxL0Active = (byte)iRefs;
    }

    public void Dispose()
    {
        NativeMemory.Free(Slice);
        NativeMemory.Free(Ctx);
        NativeMemory.Free(Dq);
        NativeMemory.Free(Pps);
        NativeMemory.Free(Funcs);
    }
}
