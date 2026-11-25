//-----------------------------------------------------------------------------
// Filename: ParameterSetTests.cs
//
// Description: E1 acceptance: the ported encoder must accept or reject the
// same configurations as the C-only reference build, validate them into the
// same parameters, and write the same SPS, subset SPS and PPS NAL units, byte
// for byte (refharness `h264ref paramsets`, reference/paramsets.tsv).
//
// The encoder API and frame coding are not ported yet (E4, E8), so the test
// drives the ported functions the way CWelsH264SVCEncoder::Initialize /
// InitializeExt, WelsInitEncoderExt, RequestMemorySvc and InitDqLayers do,
// keeping only their parameter set work (ParamSetEncoder below). For frame
// operations (I and F) it cannot decide the frame type itself yet, so it takes
// the reference build's: an IDR writes the parameter sets as
// PrepareEncodeFrame does, any other type writes none, and the test compares
// the non-VCL layers. P (EncodeParameterSets) runs as the encoder runs it.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created (E1).
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using SIPSorcery.H264.Encoder;
using Xunit;

namespace SIPSorcery.H264.TestVectors;

/// <summary>One operation of a reference configuration.</summary>
public sealed record ParamSetOp(int Index, char Op, int ReturnCode, string FrameType, string NonVclLayers);

/// <summary>One reference configuration: its settings, init result, validated parameters and operations.</summary>
public sealed record ParamSetConfig(string Name, string Ops, string Settings)
{
    public int InitReturnCode { get; set; } = int.MinValue;
    public string Params { get; set; }
    public List<ParamSetOp> Steps { get; } = new();

    public override string ToString() => Name;
}

public static class ParamSetReferences
{
    public const string TableFile = "paramsets.tsv";

    private static readonly Lazy<(string Defaults, IReadOnlyList<ParamSetConfig> Configs)> _table = new(Load);

    /// <summary>GetDefaultParams as the reference build reports it.</summary>
    public static string Defaults => _table.Value.Defaults;

    public static IReadOnlyList<ParamSetConfig> Configs => _table.Value.Configs;

    public static IEnumerable<object[]> ConfigNames => Configs.Select(c => new object[] { c.Name });

    private static (string, IReadOnlyList<ParamSetConfig>) Load()
    {
        string defaults = null;
        var configs = new List<ParamSetConfig>();
        var byName = new Dictionary<string, ParamSetConfig>();
        foreach (string line in File.ReadLines(Path.Combine(AppContext.BaseDirectory, TableFile)))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] c = line.Split('\t');
            switch (c[0])
            {
                case "defaults":
                    defaults = c[1];
                    break;
                case "config":
                    var config = new ParamSetConfig(c[1], c[2] == "-" ? "" : c[2], c[3]);
                    configs.Add(config);
                    byName.Add(config.Name, config);
                    break;
                case "init":
                    byName[c[1]].InitReturnCode = int.Parse(c[2]);
                    break;
                case "params":
                    byName[c[1]].Params = c[2];
                    break;
                case "op":
                    byName[c[1]].Steps.Add(new ParamSetOp(int.Parse(c[2]), c[3][0], int.Parse(c[4]), c[5], c[6]));
                    break;
                default:
                    throw new InvalidDataException($"{TableFile}: unknown row type {c[0]}.");
            }
        }
        return (defaults ?? throw new InvalidDataException($"{TableFile}: no defaults row."), configs);
    }
}

public class ParameterSetTests
{
    [Fact]
    public void DefaultsMatchReference()
    {
        var p = default(SEncParamExt);
        SWelsSvcCodingParam.FillDefault(ref p);
        Assert.Equal(ParamSetReferences.Defaults, ParamSetEncoder.DumpParamExt(p));
    }

    [Fact]
    public void TableCoversTheRealtimeProfile()
    {
        // The WebRTC configurations must be present and initialise: they are the port's target.
        var webrtc = ParamSetReferences.Configs.Where(c => c.Name.StartsWith("webrtc-")).ToList();
        Assert.NotEmpty(webrtc);
        Assert.All(webrtc, c => Assert.Equal(0, c.InitReturnCode));
    }

    [Theory]
    [MemberData(nameof(ParamSetReferences.ConfigNames), MemberType = typeof(ParamSetReferences))]
    public void ConfigMatchesReference(string name)
    {
        ParamSetConfig reference = ParamSetReferences.Configs.Single(c => c.Name == name);
        Assert.Equal(reference.Ops.Length, reference.Steps.Count);

        using var encoder = new ParamSetEncoder();
        int rv = encoder.Initialize(reference.Settings);
        Assert.Equal(reference.InitReturnCode, rv);
        if (rv != 0)
        {
            return;
        }
        Assert.Equal(reference.Params, encoder.DumpValidatedParams());

        foreach (ParamSetOp step in reference.Steps)
        {
            if (step.Op == 'P')
            {
                (int opRv, string frameType, string layers) = encoder.EncodeParameterSets();
                Assert.Equal((step.ReturnCode, step.FrameType, step.NonVclLayers), (opRv, frameType, layers));
            }
            else
            {
                // The frame type comes from the reference build (see the file header).
                string layers = encoder.EncodeFrameParameterSets(step.FrameType == "IDR");
                Assert.True(step.NonVclLayers == layers, $"{name} op {step.Index} ({step.Op}): expected {step.NonVclLayers}, got {layers}");
            }
        }
    }
}

/// <summary>
/// The parameter set work of the encoder's init and frame paths, over the ported
/// functions (see the file header). One instance is one encoder.
/// </summary>
internal sealed unsafe class ParamSetEncoder : IDisposable
{
    private const int cmInitParaError = 1;
    private const int BsBufferSize = 1 << 20;
    private const int NalListCount = 1024;

    private sWelsEncCtx* _ctx;
    private SFrameBSInfo* _info;

    public ParamSetEncoder()
    {
        _info = (SFrameBSInfo*)NativeMemory.AllocZeroed((nuint)sizeof(SFrameBSInfo));
    }

    /// <summary>
    /// Initialize (api=base) or InitializeExt (api=ext) with the settings applied, as the
    /// reference harness does. Returns the API's return code.
    /// </summary>
    public int Initialize(string settings)
    {
        var ext = default(SEncParamExt);
        SWelsSvcCodingParam.FillDefault(ref ext); // GetDefaultParams
        var bas = default(SEncParamBase);
        bool isBase = false;
        foreach (string item in settings.Split(';'))
        {
            int eq = item.IndexOf('=');
            string k = item[..eq], v = item[(eq + 1)..];
            if (k == "api")
            {
                isBase = v == "base";
                continue;
            }
            if (!ApplySetting(ref ext, ref bas, isBase, k, v))
            {
                throw new InvalidDataException($"Unknown setting {k}.");
            }
        }

        // SWelsSvcCodingParam sConfig; (the constructor calls FillDefault)
        var sConfig = default(SWelsSvcCodingParam);
        sConfig.FillDefault();
        if ((isBase ? sConfig.ParamBaseTranscode(bas) : sConfig.ParamTranscode(ext)) != 0)
        {
            return cmInitParaError;
        }
        return InitializeInternal(&sConfig);
    }

    /// <summary>CWelsH264SVCEncoder::InitializeInternal.</summary>
    private int InitializeInternal(SWelsSvcCodingParam* pCfg)
    {
        int iNumOfLayers = pCfg->iSpatialLayerNum;
        if (iNumOfLayers < 1 || iNumOfLayers > WelsEnc.MAX_DEPENDENCY_LAYER)
        {
            return cmInitParaError;
        }
        if (pCfg->iTemporalLayerNum < 1)
        {
            pCfg->iTemporalLayerNum = 1;
        }
        if (pCfg->iTemporalLayerNum > WelsEnc.MAX_TEMPORAL_LEVEL)
        {
            return cmInitParaError;
        }
        if (pCfg->uiGopSize < 1 || pCfg->uiGopSize > WelsEnc.MAX_GOP_SIZE)
        {
            return cmInitParaError;
        }
        if (!WelsCommon.WELS_POWER2_IF(pCfg->uiGopSize))
        {
            return cmInitParaError;
        }
        if (pCfg->uiIntraPeriod != 0 && pCfg->uiIntraPeriod < pCfg->uiGopSize)
        {
            return cmInitParaError;
        }
        if (pCfg->uiIntraPeriod != 0 && (pCfg->uiIntraPeriod & (pCfg->uiGopSize - 1)) != 0)
        {
            return cmInitParaError;
        }
        if (pCfg->iUsageType == EUsageType.SCREEN_CONTENT_REAL_TIME)
        {
            if (pCfg->bEnableLongTermReference)
            {
                pCfg->iLTRRefNum = WelsEnc.LONG_TERM_REF_NUM_SCREEN;
                if (pCfg->iNumRefFrame == codec_app_def.AUTO_REF_PIC_COUNT)
                {
                    pCfg->iNumRefFrame = Math.Max(1, WelsCommon.WELS_LOG2(pCfg->uiGopSize)) + pCfg->iLTRRefNum;
                }
            }
            else
            {
                pCfg->iLTRRefNum = 0;
                if (pCfg->iNumRefFrame == codec_app_def.AUTO_REF_PIC_COUNT)
                {
                    pCfg->iNumRefFrame = (int)Math.Max(1u, pCfg->uiGopSize >> 1);
                }
            }
        }
        else
        {
            pCfg->iLTRRefNum = pCfg->bEnableLongTermReference ? WelsEnc.LONG_TERM_REF_NUM : 0;
            if (pCfg->iNumRefFrame == codec_app_def.AUTO_REF_PIC_COUNT)
            {
                pCfg->iNumRefFrame = ((pCfg->uiGopSize >> 1) > 1) ? (int)((pCfg->uiGopSize >> 1) + (uint)pCfg->iLTRRefNum) :
                                     (WelsEnc.MIN_REF_PIC_COUNT + pCfg->iLTRRefNum);
                pCfg->iNumRefFrame = WelsCommon.WELS_CLIP3(pCfg->iNumRefFrame, WelsEnc.MIN_REF_PIC_COUNT, WelsEnc.MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA);
            }
        }
        if (pCfg->iLtrMarkPeriod == 0)
        {
            pCfg->iLtrMarkPeriod = 30;
        }
        int kiDecStages = WelsCommon.WELS_LOG2(pCfg->uiGopSize);
        pCfg->iTemporalLayerNum = (sbyte)(1 + kiDecStages);
        pCfg->iLoopFilterAlphaC0Offset = WelsCommon.WELS_CLIP3(pCfg->iLoopFilterAlphaC0Offset, -6, 6);
        pCfg->iLoopFilterBetaOffset = WelsCommon.WELS_CLIP3(pCfg->iLoopFilterBetaOffset, -6, 6);

        return WelsInitEncoderExt(pCfg) != 0 ? cmInitParaError : 0;
    }

    /// <summary>WelsInitEncoderExt, up to and including RequestMemorySvc's parameter set work.</summary>
    private int WelsInitEncoderExt(SWelsSvcCodingParam* pCodingParam)
    {
        short iSliceNum = 1;
        int iCacheLineSize = 16;
        uint uiCpuFeatureFlags = 0;

        int iRet = WelsEnc.ParamValidationExt(null, pCodingParam);
        if (iRet != 0)
        {
            return iRet;
        }
        iRet = pCodingParam->DetermineTemporalSettings();
        if (iRet != 0)
        {
            return iRet;
        }
        iRet = WelsEnc.GetMultipleThreadIdc(null, pCodingParam, ref iSliceNum, ref iCacheLineSize, ref uiCpuFeatureFlags);
        if (iRet != 0)
        {
            return iRet;
        }

        _ctx = (sWelsEncCtx*)NativeMemory.AllocZeroed((nuint)sizeof(sWelsEncCtx));
        _ctx->pSvcParam = (SWelsSvcCodingParam*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsSvcCodingParam));
        *_ctx->pSvcParam = *pCodingParam;
        _ctx->pFuncList = (SWelsFuncPtrList*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsFuncPtrList));
        // InitFunctionPointers
        _ctx->pFuncList->pParametersetStrategy = IWelsParametersetStrategy.CreateParametersetStrategy(
            _ctx->pSvcParam->eSpsPpsIdStrategy, _ctx->pSvcParam->bSimulcastAVC, _ctx->pSvcParam->iSpatialLayerNum);
        _ctx->iActiveThreadsNum = (short)pCodingParam->iMultipleThreadIdc;
        _ctx->iMaxSliceCount = iSliceNum;

        return RequestMemorySvc();
    }

    /// <summary>RequestMemorySvc: the checks, the iNumRefFrame clip, the output buffers and InitDqLayers.</summary>
    private int RequestMemorySvc()
    {
        SWelsSvcCodingParam* pParam = _ctx->pSvcParam;
        if (pParam->iSpatialLayerNum < 1 || pParam->iSpatialLayerNum > WelsEnc.MAX_DEPENDENCY_LAYER)
        {
            return 1;
        }
        if (pParam->uiGopSize == 0 || (pParam->uiIntraPeriod != 0 && (pParam->uiIntraPeriod % pParam->uiGopSize) != 0))
        {
            return 1;
        }

        pParam->iNumRefFrame = WelsCommon.WELS_CLIP3(pParam->iNumRefFrame, WelsEnc.MIN_REF_PIC_COUNT,
            pParam->iUsageType == EUsageType.CAMERA_VIDEO_REAL_TIME ? WelsEnc.MAX_REFERENCE_PICTURE_COUNT_NUM_CAMERA :
            WelsEnc.MAX_REFERENCE_PICTURE_COUNT_NUM_SCREEN);

        // The buffer sizes only bound the writes; these are larger than any parameter set output.
        _ctx->pOut = (SWelsEncoderOutput*)NativeMemory.AllocZeroed((nuint)sizeof(SWelsEncoderOutput));
        _ctx->pOut->pBsBuffer = (byte*)NativeMemory.AllocZeroed(BsBufferSize);
        _ctx->pOut->uiSize = BsBufferSize;
        _ctx->pOut->sNalList = (SWelsNalRaw*)NativeMemory.AllocZeroed((nuint)(NalListCount * sizeof(SWelsNalRaw)));
        _ctx->pOut->pNalLen = (int*)NativeMemory.AllocZeroed((nuint)(NalListCount * sizeof(int)));
        _ctx->pOut->iCountNals = NalListCount;
        _ctx->pFrameBs = (byte*)NativeMemory.AllocZeroed(BsBufferSize);
        _ctx->iFrameBsSize = BsBufferSize;

        return InitDqLayers();
    }

    /// <summary>InitDqLayers, the parameter set part.</summary>
    private int InitDqLayers()
    {
        SWelsSvcCodingParam* pParam = _ctx->pSvcParam;
        IWelsParametersetStrategy* pStrategy = _ctx->pFuncList->pParametersetStrategy;
        int iDlayerCount = pParam->iSpatialLayerNum;
        SWelsSPS* pSps = null;
        SSubsetSps* pSubsetSps = null;
        int iSpsId = 0;
        uint iPpsId = 0;

        int kiNeededSpsNum = (int)pStrategy->GetNeededSpsNum();
        int kiNeededSubsetSpsNum = (int)pStrategy->GetNeededSubsetSpsNum();
        _ctx->pSpsArray = (SWelsSPS*)NativeMemory.AllocZeroed((nuint)(kiNeededSpsNum * sizeof(SWelsSPS)));
        _ctx->pSubsetArray = kiNeededSubsetSpsNum > 0
            ? (SSubsetSps*)NativeMemory.AllocZeroed((nuint)(kiNeededSubsetSpsNum * sizeof(SSubsetSps)))
            : null;
        int kiNeededPpsNum = (int)pStrategy->GetNeededPpsNum();
        _ctx->pPPSArray = (SWelsPPS*)NativeMemory.AllocZeroed((nuint)(kiNeededPpsNum * sizeof(SWelsPPS)));

        pStrategy->LoadPrevious(null, _ctx->pSpsArray, _ctx->pSubsetArray, _ctx->pPPSArray);

        _ctx->pDqIdcMap = (SDqIdc*)NativeMemory.AllocZeroed((nuint)(iDlayerCount * sizeof(SDqIdc)));

        for (int iDlayerIndex = 0; iDlayerIndex < iDlayerCount; iDlayerIndex++)
        {
            SDqIdc* pDqIdc = &_ctx->pDqIdcMap[iDlayerIndex];
            bool bUseSubsetSps = !pParam->bSimulcastAVC && iDlayerIndex > 0;
            bool bSvcBaselayer = !pParam->bSimulcastAVC && iDlayerCount > 0 && iDlayerIndex == 0;
            pDqIdc->uiSpatialId = (sbyte)iDlayerIndex;

            iSpsId = (int)pStrategy->GenerateNewSps(_ctx, bUseSubsetSps, iDlayerIndex, iDlayerCount, (uint)iSpsId,
                ref pSps, ref pSubsetSps, bSvcBaselayer);
            if (0 > iSpsId)
            {
                return WelsEnc.ENC_RETURN_UNSUPPORTED_PARA;
            }
            if (!bUseSubsetSps)
            {
                pSps = &_ctx->pSpsArray[iSpsId];
            }
            else
            {
                pSubsetSps = &_ctx->pSubsetArray[iSpsId];
            }

            iPpsId = pStrategy->InitPps(_ctx, (uint)iSpsId, pSps, pSubsetSps, iPpsId, true, bUseSubsetSps,
                pParam->iEntropyCodingModeFlag != 0);

            pDqIdc->iSpsId = (byte)iSpsId;
            pDqIdc->iPpsId = (ushort)iPpsId;

            if (pParam->bSimulcastAVC || bUseSubsetSps)
            {
                ++iSpsId;
            }
            ++iPpsId;
            if (bUseSubsetSps)
            {
                ++_ctx->iSubsetSpsNum;
            }
            else
            {
                ++_ctx->iSpsNum;
            }
            ++_ctx->iPpsNum;
        }

        pStrategy->UpdateParaSetNum(_ctx);
        return 0;
    }

    /// <summary>GetOption (ENCODER_OPTION_SVC_ENCODE_PARAM_EXT), dumped as the harness does.</summary>
    public string DumpValidatedParams() => DumpParamExt(_ctx->pSvcParam->Base());

    /// <summary>EncodeParameterSets.</summary>
    public (int, string, string) EncodeParameterSets()
    {
        *_info = default;
        int rv = WelsEnc.WelsEncoderEncodeParameterSets(_ctx, _info);
        return (rv, FrameTypeName(_info->eFrameType), NonVclLayers(_info));
    }

    /// <summary>
    /// The parameter sets EncodeFrame writes for a frame of the given type
    /// (WelsEncoderEncodeExt's InitBitStream, then PrepareEncodeFrame for an IDR).
    /// </summary>
    public string EncodeFrameParameterSets(bool idr)
    {
        *_info = default;
        if (!idr)
        {
            return NonVclLayers(_info);
        }

        SWelsSvcCodingParam* pSvcParam = _ctx->pSvcParam;
        int iSpatialNum = pSvcParam->iSpatialLayerNum;
        int iLayerNum = 0;
        int iFrameSize = 0;

        WelsEnc.InitBitStream(_ctx);
        SLayerBSInfo* pLayerBsInfo = (SLayerBSInfo*)&_info->sLayerInfo;
        pLayerBsInfo->pBsBuf = _ctx->pFrameBs;
        pLayerBsInfo->pNalLengthInByte = _ctx->pOut->pNalLen;

        int iRet = 0;
        if (((int)EParameterSetStrategy.SPS_LISTING & (int)pSvcParam->eSpsPpsIdStrategy) == 0)
        {
            if (pSvcParam->bSimulcastAVC)
            {
                // PrepareEncodeFrame runs once per spatial layer for simulcast.
                for (int iCurDid = 0; iCurDid < iSpatialNum && iRet == 0; iCurDid++)
                {
                    iRet = WelsEnc.WriteSavcParaset(_ctx, iCurDid, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
                    SSpatialLayerInternal* pParamInternal = (SSpatialLayerInternal*)&pSvcParam->sDependencyLayers + iCurDid;
                    ++pParamInternal->uiIdrPicId;
                }
            }
            else
            {
                iRet = WelsEnc.WriteSsvcParaset(_ctx, iSpatialNum, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
            }
        }
        else
        {
            iRet = WelsEnc.WriteSavcParaset_Listing(_ctx, iSpatialNum, ref pLayerBsInfo, ref iLayerNum, ref iFrameSize);
        }
        Assert.Equal(0, iRet);

        _info->iLayerNum = iLayerNum;
        _info->eFrameType = EVideoFrameType.videoFrameTypeIDR;
        return NonVclLayers(_info);
    }

    public void Dispose()
    {
        if (_ctx != null)
        {
            IWelsParametersetStrategy.DeleteParametersetStrategy(ref _ctx->pFuncList->pParametersetStrategy);
            NativeMemory.Free(_ctx->pDqIdcMap);
            NativeMemory.Free(_ctx->pPPSArray);
            NativeMemory.Free(_ctx->pSubsetArray);
            NativeMemory.Free(_ctx->pSpsArray);
            if (_ctx->pOut != null)
            {
                NativeMemory.Free(_ctx->pOut->pNalLen);
                NativeMemory.Free(_ctx->pOut->sNalList);
                NativeMemory.Free(_ctx->pOut->pBsBuffer);
                NativeMemory.Free(_ctx->pOut);
            }
            NativeMemory.Free(_ctx->pFrameBs);
            NativeMemory.Free(_ctx->pFuncList);
            NativeMemory.Free(_ctx->pSvcParam);
            NativeMemory.Free(_ctx);
            _ctx = null;
        }
        if (_info != null)
        {
            NativeMemory.Free(_info);
            _info = null;
        }
    }

    // ---- the harness's helpers (refharness/src/encoder.cpp) ----

    private static bool ApplySetting(ref SEncParamExt e, ref SEncParamBase b, bool isBase, string key, string value)
    {
        long v = long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : (long)double.Parse(value, CultureInfo.InvariantCulture);
        float f = (float)double.Parse(value, CultureInfo.InvariantCulture);
        if (isBase)
        {
            switch (key)
            {
                case "iUsageType": b.iUsageType = (EUsageType)v; break;
                case "iPicWidth": b.iPicWidth = (int)v; break;
                case "iPicHeight": b.iPicHeight = (int)v; break;
                case "iTargetBitrate": b.iTargetBitrate = (int)v; break;
                case "iRCMode": b.iRCMode = (RC_MODES)v; break;
                case "fMaxFrameRate": b.fMaxFrameRate = f; break;
                default: return false;
            }
            return true;
        }
        if (key.Length > 3 && key[0] == 'L' && key[2] == '.')
        {
            ref SSpatialLayerConfig ly = ref e.sSpatialLayers[key[1] - '0'];
            switch (key[3..])
            {
                case "iVideoWidth": ly.iVideoWidth = (int)v; break;
                case "iVideoHeight": ly.iVideoHeight = (int)v; break;
                case "fFrameRate": ly.fFrameRate = f; break;
                case "iSpatialBitrate": ly.iSpatialBitrate = (int)v; break;
                case "iMaxSpatialBitrate": ly.iMaxSpatialBitrate = (int)v; break;
                case "uiProfileIdc": ly.uiProfileIdc = (EProfileIdc)v; break;
                case "uiLevelIdc": ly.uiLevelIdc = (ELevelIdc)v; break;
                case "iDLayerQp": ly.iDLayerQp = (int)v; break;
                case "uiSliceMode": ly.sSliceArgument.uiSliceMode = (SliceModeEnum)v; break;
                case "uiSliceNum": ly.sSliceArgument.uiSliceNum = (uint)v; break;
                case "uiSliceSizeConstraint": ly.sSliceArgument.uiSliceSizeConstraint = (uint)v; break;
                case "bVideoSignalTypePresent": ly.bVideoSignalTypePresent = v != 0; break;
                case "uiVideoFormat": ly.uiVideoFormat = (byte)v; break;
                case "bFullRange": ly.bFullRange = v != 0; break;
                case "bColorDescriptionPresent": ly.bColorDescriptionPresent = v != 0; break;
                case "uiColorPrimaries": ly.uiColorPrimaries = (byte)v; break;
                case "uiTransferCharacteristics": ly.uiTransferCharacteristics = (byte)v; break;
                case "uiColorMatrix": ly.uiColorMatrix = (byte)v; break;
                case "bAspectRatioPresent": ly.bAspectRatioPresent = v != 0; break;
                case "eAspectRatio": ly.eAspectRatio = (ESampleAspectRatio)v; break;
                case "sAspectRatioExtWidth": ly.sAspectRatioExtWidth = (ushort)v; break;
                case "sAspectRatioExtHeight": ly.sAspectRatioExtHeight = (ushort)v; break;
                default: return false;
            }
            return true;
        }
        switch (key)
        {
            case "iUsageType": e.iUsageType = (EUsageType)v; break;
            case "iPicWidth": e.iPicWidth = (int)v; break;
            case "iPicHeight": e.iPicHeight = (int)v; break;
            case "iTargetBitrate": e.iTargetBitrate = (int)v; break;
            case "iRCMode": e.iRCMode = (RC_MODES)v; break;
            case "fMaxFrameRate": e.fMaxFrameRate = f; break;
            case "iTemporalLayerNum": e.iTemporalLayerNum = (int)v; break;
            case "iSpatialLayerNum": e.iSpatialLayerNum = (int)v; break;
            case "uiIntraPeriod": e.uiIntraPeriod = (uint)v; break;
            case "iNumRefFrame": e.iNumRefFrame = (int)v; break;
            case "eSpsPpsIdStrategy": e.eSpsPpsIdStrategy = (EParameterSetStrategy)v; break;
            case "bSimulcastAVC": e.bSimulcastAVC = v != 0; break;
            case "iEntropyCodingModeFlag": e.iEntropyCodingModeFlag = (int)v; break;
            case "bEnableFrameSkip": e.bEnableFrameSkip = v != 0; break;
            case "iMaxBitrate": e.iMaxBitrate = (int)v; break;
            case "iMaxQp": e.iMaxQp = (int)v; break;
            case "iMinQp": e.iMinQp = (int)v; break;
            case "uiMaxNalSize": e.uiMaxNalSize = (uint)v; break;
            case "bEnableLongTermReference": e.bEnableLongTermReference = v != 0; break;
            case "iMultipleThreadIdc": e.iMultipleThreadIdc = (ushort)v; break;
            case "iLoopFilterDisableIdc": e.iLoopFilterDisableIdc = (int)v; break;
            case "bEnableDenoise": e.bEnableDenoise = v != 0; break;
            case "bEnableFrameCroppingFlag": e.bEnableFrameCroppingFlag = v != 0; break;
            case "bIsLosslessLink": e.bIsLosslessLink = v != 0; break;
            default: return false;
        }
        return true;
    }

    /// <summary>The harness's DumpParamExt: floats as %.9g, bools as 0/1, enums as numbers.</summary>
    public static string DumpParamExt(in SEncParamExt p)
    {
        var o = new StringBuilder();
        static string F(float x) => ((double)x).ToString("G9", CultureInfo.InvariantCulture);
        static int B(bool x) => x ? 1 : 0;
        o.Append($"iUsageType={(int)p.iUsageType};iPicWidth={p.iPicWidth};iPicHeight={p.iPicHeight}");
        o.Append($";iTargetBitrate={p.iTargetBitrate};iRCMode={(int)p.iRCMode};fMaxFrameRate={F(p.fMaxFrameRate)}");
        o.Append($";iTemporalLayerNum={p.iTemporalLayerNum};iSpatialLayerNum={p.iSpatialLayerNum}");
        o.Append($";iComplexityMode={(int)p.iComplexityMode};uiIntraPeriod={p.uiIntraPeriod}");
        o.Append($";iNumRefFrame={p.iNumRefFrame};eSpsPpsIdStrategy={(int)p.eSpsPpsIdStrategy}");
        o.Append($";bPrefixNalAddingCtrl={B(p.bPrefixNalAddingCtrl)};bEnableSSEI={B(p.bEnableSSEI)}");
        o.Append($";bSimulcastAVC={B(p.bSimulcastAVC)};iPaddingFlag={p.iPaddingFlag}");
        o.Append($";iEntropyCodingModeFlag={p.iEntropyCodingModeFlag};bEnableFrameSkip={B(p.bEnableFrameSkip)}");
        o.Append($";iMaxBitrate={p.iMaxBitrate};iMaxQp={p.iMaxQp};iMinQp={p.iMinQp}");
        o.Append($";uiMaxNalSize={p.uiMaxNalSize};bEnableLongTermReference={B(p.bEnableLongTermReference)}");
        o.Append($";iLTRRefNum={p.iLTRRefNum};iLtrMarkPeriod={p.iLtrMarkPeriod}");
        o.Append($";iMultipleThreadIdc={p.iMultipleThreadIdc};bUseLoadBalancing={B(p.bUseLoadBalancing)}");
        o.Append($";iLoopFilterDisableIdc={p.iLoopFilterDisableIdc};iLoopFilterAlphaC0Offset={p.iLoopFilterAlphaC0Offset}");
        o.Append($";iLoopFilterBetaOffset={p.iLoopFilterBetaOffset};bEnableDenoise={B(p.bEnableDenoise)}");
        o.Append($";bEnableBackgroundDetection={B(p.bEnableBackgroundDetection)};bEnableAdaptiveQuant={B(p.bEnableAdaptiveQuant)}");
        o.Append($";bEnableFrameCroppingFlag={B(p.bEnableFrameCroppingFlag)};bEnableSceneChangeDetect={B(p.bEnableSceneChangeDetect)}");
        o.Append($";bIsLosslessLink={B(p.bIsLosslessLink)};bFixRCOverShoot={B(p.bFixRCOverShoot)}");
        o.Append($";iIdrBitrateRatio={p.iIdrBitrateRatio};bPsnrY={B(p.bPsnrY)};bPsnrU={B(p.bPsnrU)}");
        o.Append($";bPsnrV={B(p.bPsnrV)}");
        for (int i = 0; i < codec_app_def.MAX_SPATIAL_LAYER_NUM; i++)
        {
            SSpatialLayerConfig l = p.sSpatialLayers[i];
            o.Append($";L{i}.iVideoWidth={l.iVideoWidth};L{i}.iVideoHeight={l.iVideoHeight}");
            o.Append($";L{i}.fFrameRate={F(l.fFrameRate)};L{i}.iSpatialBitrate={l.iSpatialBitrate}");
            o.Append($";L{i}.iMaxSpatialBitrate={l.iMaxSpatialBitrate};L{i}.uiProfileIdc={(int)l.uiProfileIdc}");
            o.Append($";L{i}.uiLevelIdc={(int)l.uiLevelIdc};L{i}.iDLayerQp={l.iDLayerQp}");
            o.Append($";L{i}.uiSliceMode={(int)l.sSliceArgument.uiSliceMode};L{i}.uiSliceNum={l.sSliceArgument.uiSliceNum}");
            o.Append($";L{i}.uiSliceSizeConstraint={l.sSliceArgument.uiSliceSizeConstraint}");
            o.Append($";L{i}.uiSliceMbNum0={l.sSliceArgument.uiSliceMbNum[0]}");
            o.Append($";L{i}.bVideoSignalTypePresent={B(l.bVideoSignalTypePresent)};L{i}.uiVideoFormat={l.uiVideoFormat}");
            o.Append($";L{i}.bFullRange={B(l.bFullRange)};L{i}.bColorDescriptionPresent={B(l.bColorDescriptionPresent)}");
            o.Append($";L{i}.uiColorPrimaries={l.uiColorPrimaries};L{i}.uiTransferCharacteristics={l.uiTransferCharacteristics}");
            o.Append($";L{i}.uiColorMatrix={l.uiColorMatrix};L{i}.bAspectRatioPresent={B(l.bAspectRatioPresent)}");
            o.Append($";L{i}.eAspectRatio={(int)l.eAspectRatio};L{i}.sAspectRatioExtWidth={l.sAspectRatioExtWidth}");
            o.Append($";L{i}.sAspectRatioExtHeight={l.sAspectRatioExtHeight}");
        }
        return o.ToString();
    }

    private static string FrameTypeName(EVideoFrameType t) => t switch
    {
        EVideoFrameType.videoFrameTypeInvalid => "invalid",
        EVideoFrameType.videoFrameTypeIDR => "IDR",
        EVideoFrameType.videoFrameTypeI => "I",
        EVideoFrameType.videoFrameTypeP => "P",
        EVideoFrameType.videoFrameTypeSkip => "skip",
        EVideoFrameType.videoFrameTypeIPMixed => "IPMixed",
        _ => "?",
    };

    /// <summary>(spatial,temporal,quality,subseq,frametype)=nal,nal,... joined with '|', or "-".</summary>
    private static string NonVclLayers(SFrameBSInfo* info)
    {
        var s = new StringBuilder();
        SLayerBSInfo* layers = (SLayerBSInfo*)&info->sLayerInfo;
        for (int i = 0; i < info->iLayerNum; i++)
        {
            SLayerBSInfo* l = &layers[i];
            if (l->uiLayerType != (byte)LAYER_TYPE.NON_VIDEO_CODING_LAYER)
            {
                continue;
            }
            if (s.Length > 0)
            {
                s.Append('|');
            }
            s.Append($"({l->uiSpatialId},{l->uiTemporalId},{l->uiQualityId},{l->iSubSeqId},{FrameTypeName(l->eFrameType)})=");
            byte* p = l->pBsBuf;
            for (int j = 0; j < l->iNalCount; j++)
            {
                if (j > 0)
                {
                    s.Append(',');
                }
                s.Append(Convert.ToHexString(new ReadOnlySpan<byte>(p, l->pNalLengthInByte[j])).ToLowerInvariant());
                p += l->pNalLengthInByte[j];
            }
        }
        return s.Length == 0 ? "-" : s.ToString();
    }
}
