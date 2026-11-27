//-----------------------------------------------------------------------------
// Filename: decode_mb_aux_simd.cs
//
// Description: SIMD versions of IdctResAddPred_c (decode_mb_aux.cs) and of
// IdctFourResAddPred_ (decoder.cs) built on it (P5.5), for the slots upstream's
// X86_ASM and HAVE_NEON blocks fill in InitPredFunc. Not a port: upstream's are
// assembly. Each leaves the picture as its C kernel does
// (decode_mb_aux_simd_unittest.cs), and reads and writes only what the C
// kernel reads and writes. The transform is the one common/transform_simd.cs
// shares with the encoder.
//
// IdctFourResAddPred_simd skips the blocks the C one skips: where the
// non-zero count and the DC coefficient are both 0. Two side by side blocks
// that both go through take one pass.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.5).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    public static void IdctResAddPred_simd(uint8_t* pPred, int32_t kiStride, int16_t* pRs)
    {
        // The right half works out a copy, unused.
        Vector128<short> a01 = Vector128.Load(pRs), a23 = Vector128.Load(pRs + 8);
        Idct4x4x2(a01, a23, a01, a23, out Vector128<short> o0, out Vector128<short> o1, out Vector128<short> o2, out Vector128<short> o3);
        AddPred4x4(pPred, kiStride, pPred, kiStride, o0, o1, o2, o3);
    }

    /// <summary>The blocks at pRs[0..15] (left) and pRs[16..31] (right), each if it goes through.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void IdctTwoResAddPred(uint8_t* pPred, int32_t iStride, int16_t* pRs, bool bLeft, bool bRight)
    {
        if (bLeft && bRight)
        {
            Idct4x4x2(Vector128.Load(pRs), Vector128.Load(pRs + 8), Vector128.Load(pRs + 16), Vector128.Load(pRs + 24),
                      out Vector128<short> o0, out Vector128<short> o1, out Vector128<short> o2, out Vector128<short> o3);
            AddPred8x4(pPred, iStride, pPred, iStride, o0, o1, o2, o3);
        }
        else if (bLeft)
        {
            IdctResAddPred_simd(pPred, iStride, pRs);
        }
        else if (bRight)
        {
            IdctResAddPred_simd(pPred + 4, iStride, pRs + 16);
        }
    }

    public static void IdctFourResAddPred_simd(uint8_t* pPred, int32_t iStride, int16_t* pRs, int8_t* pNzc)
    {
        // The coefficients aren't changed, so the four tests can come first.
        IdctTwoResAddPred(pPred, iStride, pRs, pNzc[0] != 0 || pRs[0 * 16] != 0, pNzc[1] != 0 || pRs[1 * 16] != 0);
        IdctTwoResAddPred(pPred + 4 * iStride, iStride, pRs + 32, pNzc[4] != 0 || pRs[2 * 16] != 0, pNzc[5] != 0 || pRs[3 * 16] != 0);
    }
}
