//-----------------------------------------------------------------------------
// Filename: picture_handle_unittest.cs
//
// Description: Checks the layout AllocPicture makes, and that FreePicture
// gives it all back, against the C build ("picture_handle" rows in
// reference/kernels.tsv, refharness src/islice_encoder.cpp
// KernelsPictureHandle).
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 26 Nov 2026  Claude          Created (E4).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.H264.Encoder;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;
using static SIPSorcery.H264.WelsCommon;

namespace SIPSorcery.H264.UnitTest;

public unsafe class picture_handle_unittest
{
    private const string GroupName = "picture_handle";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    [Theory]
    [MemberData(nameof(Rows))]
    public void Kernel_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        string actual = kernel switch
        {
            nameof(AllocPicture) => Layout(row),
            _ => throw new ArgumentException($"No C# kernel {kernel}."),
        };
        Assert.Equal(sha1, actual);
    }

    // Strides, plane offsets, the fields AllocPicture sets, and the
    // macroblock arrays it zeroes when asked for them.
    private static string Layout(KernelFixture row)
    {
        int w = row.Param("w"), hgt = row.Param("h");
        bool mbinfo = row.Param("mbinfo") != 0;
        CMemoryAlign* pMa = WELS_NEW_CMemoryAlign(16);
        try
        {
            SPicture* pPic = AllocPicture(pMa, w, hgt, mbinfo, 0);
            int kiMbCount = ((w + 15) >> 4) * ((hgt + 15) >> 4);
            using var h = new FixtureHasher();
            for (int i = 0; i < 3; i++)
            {
                h.AddI32(pPic->iLineSize[i]);
                h.AddI32((int)((byte*)pPic->pData[i] - pPic->pBuffer));
            }
            h.AddI32(IsliceFixtures.PictureBufferSize(pPic));
            h.AddI32(pPic->iWidthInPixel);
            h.AddI32(pPic->iHeightInPixel);
            h.AddI32(pPic->iFrameNum);
            h.AddI32(pPic->bIsLongRef ? 1 : 0);
            h.AddI32(pPic->iLongTermPicNum);
            h.AddI32(pPic->uiRecieveConfirmed);
            h.AddI32(pPic->iMarkFrameNum);
            h.AddI32(pPic->uiRefMbType != null ? 1 : 0);
            h.AddI32(pPic->pRefMbQp != null ? 1 : 0);
            h.AddI32(pPic->sMvList != null ? 1 : 0);
            h.AddI32(pPic->pMbSkipSad != null ? 1 : 0);
            if (mbinfo)
            {
                h.Add(new ReadOnlySpan<byte>(pPic->uiRefMbType, kiMbCount * sizeof(uint)));
                h.Add(new ReadOnlySpan<byte>(pPic->pRefMbQp, kiMbCount));
                h.Add(new ReadOnlySpan<byte>(pPic->sMvList, kiMbCount * sizeof(SMVUnitXY)));
                h.Add(new ReadOnlySpan<byte>(pPic->pMbSkipSad, kiMbCount * sizeof(int)));
            }
            h.AddI32(pPic->pScreenBlockFeatureStorage != null ? 1 : 0);
            FreePicture(pMa, &pPic);
            h.AddI32(pPic != null ? 1 : 0);
            h.AddI32((int)pMa->WelsGetMemoryUsage());
            return h.Hex();
        }
        finally
        {
            WELS_DELETE_CMemoryAlign(ref pMa);
        }
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        var kernels = KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        Assert.Equal(new[] { nameof(AllocPicture) }, kernels);
    }
}
