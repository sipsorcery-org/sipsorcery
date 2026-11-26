//-----------------------------------------------------------------------------
// Filename: enc_get_intra_predictor_unittest.cs
//
// Description: Checks the encoder's intra predictors, which write a packed
// prediction, against the C build ("enc_get_intra_predictor" rows in
// reference/kernels.tsv, refharness src/kernels_encoder.cpp
// KernelsEncIntraPred and PredHash, whose layout EncoderFixtures.PredHash
// follows).
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
using System.Collections.Generic;
using System.Linq;
using Xunit;
using static SIPSorcery.H264.Encoder.WelsEnc;

namespace SIPSorcery.H264.UnitTest;

public unsafe class enc_get_intra_predictor_unittest
{
    private const string GroupName = "enc_get_intra_predictor";

    public static IEnumerable<object[]> Rows => KernelFixtures.Group(GroupName);

    private static void Lookup(string kernel, out delegate*<byte*, byte*, int, void> f, out int n)
    {
        switch (kernel)
        {
            case nameof(WelsI4x4LumaPredV_c): f = &WelsI4x4LumaPredV_c; n = 4; break;
            case nameof(WelsI4x4LumaPredH_c): f = &WelsI4x4LumaPredH_c; n = 4; break;
            case nameof(WelsI4x4LumaPredDc_c): f = &WelsI4x4LumaPredDc_c; n = 4; break;
            case nameof(WelsI4x4LumaPredDcLeft_c): f = &WelsI4x4LumaPredDcLeft_c; n = 4; break;
            case nameof(WelsI4x4LumaPredDcTop_c): f = &WelsI4x4LumaPredDcTop_c; n = 4; break;
            case nameof(WelsI4x4LumaPredDcNA_c): f = &WelsI4x4LumaPredDcNA_c; n = 4; break;
            case nameof(WelsI4x4LumaPredDDL_c): f = &WelsI4x4LumaPredDDL_c; n = 4; break;
            case nameof(WelsI4x4LumaPredDDLTop_c): f = &WelsI4x4LumaPredDDLTop_c; n = 4; break;
            case nameof(WelsI4x4LumaPredDDR_c): f = &WelsI4x4LumaPredDDR_c; n = 4; break;
            case nameof(WelsI4x4LumaPredVL_c): f = &WelsI4x4LumaPredVL_c; n = 4; break;
            case nameof(WelsI4x4LumaPredVLTop_c): f = &WelsI4x4LumaPredVLTop_c; n = 4; break;
            case nameof(WelsI4x4LumaPredVR_c): f = &WelsI4x4LumaPredVR_c; n = 4; break;
            case nameof(WelsI4x4LumaPredHU_c): f = &WelsI4x4LumaPredHU_c; n = 4; break;
            case nameof(WelsI4x4LumaPredHD_c): f = &WelsI4x4LumaPredHD_c; n = 4; break;
            case nameof(WelsIChromaPredV_c): f = &WelsIChromaPredV_c; n = 8; break;
            case nameof(WelsIChromaPredH_c): f = &WelsIChromaPredH_c; n = 8; break;
            case nameof(WelsIChromaPredPlane_c): f = &WelsIChromaPredPlane_c; n = 8; break;
            case nameof(WelsIChromaPredDc_c): f = &WelsIChromaPredDc_c; n = 8; break;
            case nameof(WelsIChromaPredDcLeft_c): f = &WelsIChromaPredDcLeft_c; n = 8; break;
            case nameof(WelsIChromaPredDcTop_c): f = &WelsIChromaPredDcTop_c; n = 8; break;
            case nameof(WelsIChromaPredDcNA_c): f = &WelsIChromaPredDcNA_c; n = 8; break;
            case nameof(WelsI16x16LumaPredPlane_c): f = &WelsI16x16LumaPredPlane_c; n = 16; break;
            case nameof(WelsI16x16LumaPredDc_c): f = &WelsI16x16LumaPredDc_c; n = 16; break;
            case nameof(WelsI16x16LumaPredDcTop_c): f = &WelsI16x16LumaPredDcTop_c; n = 16; break;
            case nameof(WelsI16x16LumaPredDcLeft_c): f = &WelsI16x16LumaPredDcLeft_c; n = 16; break;
            case nameof(WelsI16x16LumaPredDcNA_c): f = &WelsI16x16LumaPredDcNA_c; n = 16; break;
            default: throw new ArgumentException($"No C# kernel {kernel}.");
        }
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Predictor_MatchesCBuild(string kernel, string kase, string sha1)
    {
        var row = new KernelFixture(GroupName, kernel, kase, sha1);
        Lookup(kernel, out var f, out int n);
        Assert.Equal(sha1, EncoderFixtures.PredHash(f, n, row.Param("stride"), row.Param("seed")));
    }

    [Fact]
    public void Group_HasEveryKernel()
    {
        Assert.Equal(26, KernelFixtures.Group(GroupName).Select(r => (string)r[0]).Distinct().Count());
    }
}
