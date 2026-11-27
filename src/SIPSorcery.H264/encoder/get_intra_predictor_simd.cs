//-----------------------------------------------------------------------------
// Filename: get_intra_predictor_simd.cs
//
// Description: The encoder's SIMD 16x16 luma and 8x8 chroma plane intra
// predictors (P5.6), in place of upstream's x86 intra_pred.asm and NEON
// versions. Not a port: those are assembly. Each writes the packed prediction
// its C predictor (get_intra_predictor.cs) writes, through the shared helpers
// in common/intra_pred_common_simd.cs, which also hold the 16x16 V and H ones.
//
// The other chroma predictors and the 4x4 ones stay C: their rows are single
// 8 or 4 byte stores already, and none shows in the encoder's profile.
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

namespace SIPSorcery.H264.Encoder;

internal static unsafe partial class WelsEnc
{
    public static void WelsI16x16LumaPredPlane_simd(uint8_t* pPred, uint8_t* pRef, int32_t kiStride) =>
        PredPlane16x16(pPred, 16, pRef, kiStride);

    public static void WelsI16x16LumaPredDc_simd(uint8_t* pPred, uint8_t* pRef, int32_t kiStride) =>
        PredFill16x16(pPred, 16, Vector128.Create((uint8_t)((16 + PredSumTop16(pRef, kiStride) + PredSumLeft16(pRef, kiStride)) >> 5)));

    public static void WelsI16x16LumaPredDcTop_simd(uint8_t* pPred, uint8_t* pRef, int32_t kiStride) =>
        PredFill16x16(pPred, 16, Vector128.Create((uint8_t)((8 + PredSumTop16(pRef, kiStride)) >> 4)));

    public static void WelsI16x16LumaPredDcLeft_simd(uint8_t* pPred, uint8_t* pRef, int32_t kiStride) =>
        PredFill16x16(pPred, 16, Vector128.Create((uint8_t)((8 + PredSumLeft16(pRef, kiStride)) >> 4)));

    public static void WelsI16x16LumaPredDcNA_simd(uint8_t* pPred, uint8_t* pRef, int32_t kiStride) =>
        PredFill16x16(pPred, 16, Vector128.Create((uint8_t)0x80));

    public static void WelsIChromaPredPlane_simd(uint8_t* pPred, uint8_t* pRef, int32_t kiStride) =>
        PredPlane8x8(pPred, I8x8_PRED_STRIDE, pRef, kiStride);
}
