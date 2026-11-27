//-----------------------------------------------------------------------------
// Filename: sad_common_simd.cs
//
// Description: SIMD versions of the SAD kernels in sad_common.cs (P5.2), for the
// slots upstream's X86_ASM and HAVE_NEON blocks fill in WelsInitSampleSadFunc.
// Not a port: upstream's are assembly. Each returns what its C kernel returns,
// for any input (sad_common_simd_unittest.cs), and reads only the samples the C
// kernel reads.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.2).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using static SIPSorcery.H264.Simd;

namespace SIPSorcery.H264;

internal static unsafe partial class WelsCommon
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int32_t Sad16xN_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2, int32_t iHeight)
    {
        Vector128<ushort> acc = Vector128<ushort>.Zero;
        for (int32_t i = 0; i < iHeight; i += 2)
        {
            acc = SadAdd(acc, Load16(pSample1), Load16(pSample2));
            acc = SadAdd(acc, Load16(pSample1 + iStride1), Load16(pSample2 + iStride2));
            pSample1 += 2 * iStride1;
            pSample2 += 2 * iStride2;
        }
        return SadTotal(acc);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int32_t Sad8xN_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2, int32_t iHeight)
    {
        Vector128<ushort> acc = Vector128<ushort>.Zero;
        for (int32_t i = 0; i < iHeight; i += 2)
        {
            acc = SadAdd(acc, Load8x2(pSample1, pSample1 + iStride1), Load8x2(pSample2, pSample2 + iStride2));
            pSample1 += 2 * iStride1;
            pSample2 += 2 * iStride2;
        }
        return SadTotal(acc);
    }

    public static int32_t WelsSampleSad4x4_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        SadTotal(SadAdd(Vector128<ushort>.Zero, Load4x4(pSample1, iStride1), Load4x4(pSample2, iStride2)));

    public static int32_t WelsSampleSad8x4_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        Sad8xN_simd(pSample1, iStride1, pSample2, iStride2, 4);

    public static int32_t WelsSampleSad4x8_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2)
    {
        Vector128<ushort> acc = SadAdd(Vector128<ushort>.Zero, Load4x4(pSample1, iStride1), Load4x4(pSample2, iStride2));
        acc = SadAdd(acc, Load4x4(pSample1 + 4 * iStride1, iStride1), Load4x4(pSample2 + 4 * iStride2, iStride2));
        return SadTotal(acc);
    }

    public static int32_t WelsSampleSad8x8_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        Sad8xN_simd(pSample1, iStride1, pSample2, iStride2, 8);

    public static int32_t WelsSampleSad16x8_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        Sad16xN_simd(pSample1, iStride1, pSample2, iStride2, 8);

    public static int32_t WelsSampleSad8x16_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        Sad8xN_simd(pSample1, iStride1, pSample2, iStride2, 16);

    public static int32_t WelsSampleSad16x16_simd(uint8_t* pSample1, int32_t iStride1, uint8_t* pSample2, int32_t iStride2) =>
        Sad16xN_simd(pSample1, iStride1, pSample2, iStride2, 16);

    // The four neighbours, as the C kernels: pSad[0] is one row up, [1] one down, [2] one
    // sample left and [3] one right. The 16-wide ones share the source rows and the up and
    // down reference rows between the four.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SadFour16xN_simd(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t iHeight,
                                         int32_t* pSad)
    {
        Vector128<ushort> up = Vector128<ushort>.Zero, down = up, left = up, right = up;
        Vector128<byte> above = Load16(iSample2 - iStride2);
        Vector128<byte> row = Load16(iSample2);
        for (int32_t i = 0; i < iHeight; i++)
        {
            Vector128<byte> src = Load16(iSample1);
            Vector128<byte> below = Load16(iSample2 + iStride2);
            up = SadAdd(up, src, above);
            down = SadAdd(down, src, below);
            left = SadAdd(left, src, Load16(iSample2 - 1));
            right = SadAdd(right, src, Load16(iSample2 + 1));
            above = row;
            row = below;
            iSample1 += iStride1;
            iSample2 += iStride2;
        }
        pSad[0] = SadTotal(up);
        pSad[1] = SadTotal(down);
        pSad[2] = SadTotal(left);
        pSad[3] = SadTotal(right);
    }

    public static void WelsSampleSadFour16x16_simd(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad) =>
        SadFour16xN_simd(iSample1, iStride1, iSample2, iStride2, 16, pSad);

    public static void WelsSampleSadFour16x8_simd(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad) =>
        SadFour16xN_simd(iSample1, iStride1, iSample2, iStride2, 8, pSad);

    public static void WelsSampleSadFour8x16_simd(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad8x16_simd(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad8x16_simd(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad8x16_simd(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad8x16_simd(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour8x8_simd(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad8x8_simd(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad8x8_simd(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad8x8_simd(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad8x8_simd(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour4x4_simd(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad4x4_simd(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad4x4_simd(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad4x4_simd(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad4x4_simd(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour8x4_simd(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad8x4_simd(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad8x4_simd(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad8x4_simd(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad8x4_simd(iSample1, iStride1, (iSample2 + 1), iStride2);
    }

    public static void WelsSampleSadFour4x8_simd(uint8_t* iSample1, int32_t iStride1, uint8_t* iSample2, int32_t iStride2, int32_t* pSad)
    {
        *(pSad) = WelsSampleSad4x8_simd(iSample1, iStride1, (iSample2 - iStride2), iStride2);
        *(pSad + 1) = WelsSampleSad4x8_simd(iSample1, iStride1, (iSample2 + iStride2), iStride2);
        *(pSad + 2) = WelsSampleSad4x8_simd(iSample1, iStride1, (iSample2 - 1), iStride2);
        *(pSad + 3) = WelsSampleSad4x8_simd(iSample1, iStride1, (iSample2 + 1), iStride2);
    }
}
