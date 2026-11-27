//-----------------------------------------------------------------------------
// Filename: UpstreamEncoderHashTests.cs
//
// Description: E7 acceptance: openh264's own encoder conformance hashes
// (test/api/encoder_test.cpp, EncoderOutputTest.CompareOutput, at 78783f19)
// for camera content, SM_SINGLE_SLICE, one spatial layer and CAVLC: the
// three without the denoiser, which go through Initialize (SEncParamBase,
// so the quality mode and the default scene change detection, background
// detection and adaptive quantisation), and the one with it, which goes
// through InitializeExt. As upstream's BaseEncoderTest does, every frame of
// the file is encoded with timestamp 0 and the SHA1 covers the bytes of
// every frame that is not a skip.
//
// The YUV files are not committed: the rows run when OPENH264_RES points at
// an openh264 res/ directory.
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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using SIPSorcery.H264.Encoder;
using Xunit;
using Xunit.Abstractions;

namespace SIPSorcery.H264.TestVectors;

public class UpstreamEncoderHashTests
{
    private const string ResEnvironmentVariable = "OPENH264_RES";

    private readonly ITestOutputHelper _output;

    public UpstreamEncoderHashTests(ITestOutputHelper output) => _output = output;

    // kFileParamArray's rows for CAMERA_VIDEO_REAL_TIME, SM_SINGLE_SLICE, one layer, no
    // lossless link, no LTR and CAVLC: file, hash, width, height, frame rate, denoise.
    public static IEnumerable<object[]> Rows => new[]
    {
        new object[] { "CiscoVT2people_320x192_12fps.yuv", "672a52fb6b6e6d52b5b3f3480d13d44e88481fb9", 320, 192, 12.0f, false },
        new object[] { "CiscoVT2people_160x96_6fps.yuv", "08ade1853e4e49d50be675393780e75519586143", 160, 96, 6.0f, false },
        new object[] { "Static_152_100.yuv", "e60f12e3c24500d4306d812b0811d3c21855dd1c", 152, 100, 6.0f, false },
        new object[] { "CiscoVT2people_320x192_12fps.yuv", "913e49c787a0abdb378e9bc55bcffc27da89b965", 320, 192, 12.0f, true },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void CompareOutput(string fileName, string hash, int width, int height, float frameRate, bool denoise)
    {
        string res = Environment.GetEnvironmentVariable(ResEnvironmentVariable);
        if (string.IsNullOrEmpty(res))
        {
            _output.WriteLine($"{ResEnvironmentVariable} is not set; {fileName} is not committed, nothing to check.");
            return;
        }
        string path = Path.Combine(res, fileName);
        Assert.True(File.Exists(path), $"{path} not found ({ResEnvironmentVariable}={res}).");

        using var encoder = new EncoderDriver();
        Assert.Equal(0, InitWithParam(encoder, width, height, frameRate, denoise));

        // I420: 1(Y) + 1/4(U) + 1/4(V)
        int frameSize = width * height * 3 / 2;
        var buf = new byte[frameSize];
        using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        int frames = 0, coded = 0;
        using (FileStream fs = File.OpenRead(path))
        {
            while (fs.ReadAtLeast(buf, frameSize, throwOnEndOfStream: false) == frameSize)
            {
                EncodedFrame f = encoder.EncodeFrame(buf, 0);
                Assert.Equal(0, f.ReturnCode);
                frames++;
                if (f.FrameType != "skip")
                {
                    sha1.AppendData(f.Bytes);
                    coded++;
                }
            }
        }
        _output.WriteLine($"{fileName}: {frames} frames, {coded} coded.");
        Assert.Equal(hash, Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant());
        Assert.Equal(0u, encoder.Release());
    }

    /// <summary>BaseEncoderTest.cpp's InitWithParam for these rows.</summary>
    private static unsafe int InitWithParam(EncoderDriver encoder, int width, int height, float frameRate, bool denoise)
    {
        // bBaseParamFlag: SM_SINGLE_SLICE, no denoiser, one layer, no lossless link, no LTR, CAVLC.
        if (!denoise)
        {
            SEncParamBase param = default;  // memset (&param, 0, sizeof (SEncParamBase))
            param.iUsageType = EUsageType.CAMERA_VIDEO_REAL_TIME;
            param.fMaxFrameRate = frameRate;
            param.iPicWidth = width;
            param.iPicHeight = height;
            param.iTargetBitrate = 5000000;
            return encoder.Initialize(param);
        }
        else
        {
            SEncParamExt param = default;
            SWelsSvcCodingParam.FillDefault(ref param);    // GetDefaultParams

            param.iUsageType = EUsageType.CAMERA_VIDEO_REAL_TIME;
            param.fMaxFrameRate = frameRate;
            param.iPicWidth = width;
            param.iPicHeight = height;
            param.iTargetBitrate = 5000000;
            param.bEnableDenoise = true;
            param.iSpatialLayerNum = 1;
            param.bIsLosslessLink = false;
            param.bEnableLongTermReference = false;
            param.iEntropyCodingModeFlag = 0;

            for (int i = 0; i < param.iSpatialLayerNum; i++)
            {
                ref SSpatialLayerConfig sl = ref param.sSpatialLayers[i];
                sl.iVideoWidth = width >> (param.iSpatialLayerNum - 1 - i);
                sl.iVideoHeight = height >> (param.iSpatialLayerNum - 1 - i);
                sl.fFrameRate = frameRate;
                sl.iSpatialBitrate = param.iTargetBitrate;
                sl.sSliceArgument.uiSliceMode = SliceModeEnum.SM_SINGLE_SLICE;
            }
            param.iTargetBitrate *= param.iSpatialLayerNum;
            return encoder.InitializeExt(param);
        }
    }
}
