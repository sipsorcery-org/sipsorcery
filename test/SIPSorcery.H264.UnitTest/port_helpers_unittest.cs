//-----------------------------------------------------------------------------
// Filename: port_helpers_unittest.cs
//
// Description: Unit tests for the port's pointer-array helpers, and the struct
// layout conventions the ported api types rely on.
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

using Xunit;

namespace SIPSorcery.H264.UnitTest;

public unsafe class port_helpers_unittest
{
    [Fact]
    public void PtrArray_StoresAndReturnsPointers()
    {
        byte* buffer = stackalloc byte[16];
        SBufferInfo info = default;

        info.pDst[0] = buffer;
        info.pDst[1] = buffer + 4;
        info.pDst[2] = null;

        byte* y = info.pDst[0];
        Assert.True(y == buffer);
        Assert.True(info.pDst[1] == buffer + 4);
        Assert.True(info.pDst[2].IsNull);
        Assert.True(info.pDst[0] + 8 == buffer + 8);
        Assert.True(info.pDst[1] - 4 == buffer);

        // In-place pointer arithmetic on an element, as upstream does with pDst[i] += stride.
        info.pDst[0] = info.pDst[0] + 2;
        Assert.True(info.pDst[0] == buffer + 2);
    }

    [Fact]
    public void FixedBuffersAndInlineArrays_AreWritable()
    {
        SBufferInfo info = default;
        info.UsrData.sSystemBuffer.iStride[0] = 640;
        info.UsrData.sSystemBuffer.iStride[1] = 320;
        Assert.Equal(640, info.UsrData.sSystemBuffer.iStride[0]);
        Assert.Equal(320, info.UsrData.sSystemBuffer.iStride[1]);

        SEncParamExt param = default;
        param.sSpatialLayers[3].iVideoWidth = 1280;
        param.sSpatialLayers[3].sSliceArgument.uiSliceMbNum[codec_app_def.MAX_SLICES_NUM_TMP - 1] = 7;
        Assert.Equal(1280, param.sSpatialLayers[3].iVideoWidth);
        Assert.Equal(7u, param.sSpatialLayers[3].sSliceArgument.uiSliceMbNum[codec_app_def.MAX_SLICES_NUM_TMP - 1]);
    }

    [Fact]
    public void ApiConstants_MatchUpstream()
    {
        // MAX_SLICES_NUM_TMP = ((128 - (4*4 + 1 + 4)) / 3) in codec_app_def.h.
        Assert.Equal(35, codec_app_def.MAX_SLICES_NUM_TMP);
        Assert.True(codec_def.IS_IDR_NAL((int)ENalPriority.NAL_PRIORITY_HIGHEST, (int)ENalUnitType.NAL_SLICE_IDR));
        Assert.False(codec_def.IS_IDR_NAL((int)ENalPriority.NAL_PRIORITY_HIGH, (int)ENalUnitType.NAL_SLICE_IDR));
        Assert.Equal(DECODING_STATE.dsErrorFree, (DECODING_STATE)0);
        Assert.Equal(ERROR_CON_IDC.ERROR_CON_SLICE_COPY, (ERROR_CON_IDC)2);
        Assert.Equal("OpenH264 version:2.7.0.2610", codec_ver.g_strCodecVer);
    }
}
