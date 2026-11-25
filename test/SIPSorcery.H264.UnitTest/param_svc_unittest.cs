//-----------------------------------------------------------------------------
// Filename: param_svc_unittest.cs
//
// Description: Unit tests for encoder/param_svc.cs. SWelsSvcCodingParam
// derives from SEncParamExt upstream; the port repeats the base fields
// first, so the C++ upcast (and GetOption's memcpy of sizeof (SEncParamExt))
// only works while that prefix matches SEncParamExt exactly.
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

using System.Linq;
using System.Reflection;
using SIPSorcery.H264.Encoder;
using Xunit;

namespace SIPSorcery.H264.UnitTest;

public unsafe class param_svc_unittest
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void CodingParamStartsWithEncParamExt()
    {
        FieldInfo[] baseFields = typeof(SEncParamExt).GetFields(Instance).OrderBy(f => f.MetadataToken).ToArray();
        FieldInfo[] derivedFields = typeof(SWelsSvcCodingParam).GetFields(Instance).OrderBy(f => f.MetadataToken).ToArray();

        Assert.True(derivedFields.Length > baseFields.Length);
        for (int i = 0; i < baseFields.Length; i++)
        {
            Assert.Equal(baseFields[i].Name, derivedFields[i].Name);
            Assert.Equal(baseFields[i].FieldType, derivedFields[i].FieldType);
        }
        Assert.True(sizeof(SWelsSvcCodingParam) > sizeof(SEncParamExt));
    }

    [Fact]
    public void BaseAliasesTheLeadingFields()
    {
        var p = default(SWelsSvcCodingParam);
        p.FillDefault();

        p.Base().iPicWidth = 1280;
        p.Base().sSpatialLayers[3].iVideoHeight = 720;
        p.bPsnrV = true; // the last SEncParamExt field

        Assert.Equal(1280, p.iPicWidth);
        Assert.Equal(720, p.sSpatialLayers[3].iVideoHeight);
        Assert.True(p.Base().bPsnrV);
        Assert.True(((SEncParamExt*)&p)->bPsnrV);
    }

    [Fact]
    public void FillDefaultSetsTheDerivedFields()
    {
        var p = default(SWelsSvcCodingParam);
        p.FillDefault();

        Assert.Equal(1u, p.uiGopSize);
        Assert.Equal(codec_app_def.AUTO_REF_PIC_COUNT, p.iMaxNumRefFrame);
        Assert.Equal(10, p.iBitsVaryPercentage);
        Assert.Equal(codec_app_def.AUTO_REF_PIC_COUNT, p.iNumRefFrame);
    }
}
