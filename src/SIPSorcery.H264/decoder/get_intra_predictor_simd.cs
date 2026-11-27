//-----------------------------------------------------------------------------
// Filename: get_intra_predictor_simd.cs
//
// Description: The decoder's SIMD 16x16 luma and 8x8 chroma plane intra
// predictors (P5.6), in place of upstream's x86 intra_pred.asm and NEON
// versions. Not a port: those are assembly. Each predicts in place the
// samples its C predictor (get_intra_predictor.cs) does, through the helpers
// in common/intra_pred_common_simd.cs, with the block as its own reference.
//
// The other chroma predictors and the 4x4 and 8x8 ones stay C: their rows are
// single 8 or 4 byte stores already. (Upstream's MMX versions of some are no
// wider.)
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 27 Nov 2026  Claude          Created (P5.6).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Runtime.Intrinsics;

namespace SIPSorcery.H264.Decoder;

internal static unsafe partial class WelsDec
{
    public static void WelsI16x16LumaPredV_simd(uint8_t* pPred, int32_t kiStride) =>
        PredV16x16(pPred, kiStride, pPred, kiStride);

    public static void WelsI16x16LumaPredH_simd(uint8_t* pPred, int32_t kiStride) =>
        PredH16x16(pPred, kiStride, pPred, kiStride);

    public static void WelsI16x16LumaPredPlane_simd(uint8_t* pPred, int32_t kiStride) =>
        PredPlane16x16(pPred, kiStride, pPred, kiStride);

    public static void WelsI16x16LumaPredDc_simd(uint8_t* pPred, int32_t kiStride) =>
        PredFill16x16(pPred, kiStride, Vector128.Create((uint8_t)((16 + PredSumTop16(pPred, kiStride) + PredSumLeft16(pPred, kiStride)) >> 5)));

    public static void WelsI16x16LumaPredDcTop_simd(uint8_t* pPred, int32_t kiStride) =>
        PredFill16x16(pPred, kiStride, Vector128.Create((uint8_t)((8 + PredSumTop16(pPred, kiStride)) >> 4)));

    public static void WelsI16x16LumaPredDcLeft_simd(uint8_t* pPred, int32_t kiStride) =>
        PredFill16x16(pPred, kiStride, Vector128.Create((uint8_t)((8 + PredSumLeft16(pPred, kiStride)) >> 4)));

    public static void WelsI16x16LumaPredDcNA_simd(uint8_t* pPred, int32_t kiStride) =>
        PredFill16x16(pPred, kiStride, Vector128.Create((uint8_t)0x80));

    public static void WelsIChromaPredPlane_simd(uint8_t* pPred, int32_t kiStride) =>
        PredPlane8x8(pPred, kiStride, pPred, kiStride);
}
