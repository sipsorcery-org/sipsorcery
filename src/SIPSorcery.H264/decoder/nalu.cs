//-----------------------------------------------------------------------------
// Filename: nalu.cs
//
// Description: Port of codec/decoder/core/inc/nalu.h: the NAL unit and access
// unit structures.
// OpenH264 commit 78783f19 (v2.7.0-1).
//
// The sNalData union is an explicit-layout struct with both members at offset
// 0, as in C.
//
// Author(s):
// Claude Opus 5.5 (Anthropic AI assistant, model: claude-opus-5-5), commissioned by Aaron Clauson
//
// History:
// 25 Nov 2026  Claude          Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// The ported code is also covered by the OpenH264 BSD 2-Clause notice below.
//-----------------------------------------------------------------------------

/*!
 * \copy
 *     Copyright (c)  2013, Cisco Systems
 *     All rights reserved.
 *
 *     Redistribution and use in source and binary forms, with or without
 *     modification, are permitted provided that the following conditions
 *     are met:
 *
 *        * Redistributions of source code must retain the above copyright
 *          notice, this list of conditions and the following disclaimer.
 *
 *        * Redistributions in binary form must reproduce the above copyright
 *          notice, this list of conditions and the following disclaimer in
 *          the documentation and/or other materials provided with the
 *          distribution.
 *
 *     THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
 *     "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
 *     LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS
 *     FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE
 *     COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
 *     INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING,
 *     BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 *     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
 *     CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
 *     LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN
 *     ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 *     POSSIBILITY OF SUCH DAMAGE.
 *
 */

using System.Runtime.InteropServices;

namespace SIPSorcery.H264.Decoder;

///////////////////////////////////NAL UNIT level///////////////////////////////////

/// <summary>TagNalUnit::sNalData::SVclNal.</summary>
internal unsafe struct SVclNal
{
    public SSliceHeaderExt sSliceHeaderExt;
    public SBitStringAux sSliceBitsRead;
    public uint8_t* pNalPos;         // save the address of slice nal for GPU function
    public int32_t iNalLength;   // save the nal length for GPU function
    public bool bSliceHeaderExtFlag;
}

/// <summary>The TagNalUnit::sNalData union.</summary>
[StructLayout(LayoutKind.Explicit)]
internal struct SNalData
{
    [FieldOffset(0)] public SVclNal sVclNal;
    [FieldOffset(0)] public SPrefixNalUnit sPrefixNal;
}

/* NAL Unit Structure */
/// <summary>TagNalUnit (PNalUnit is SNalUnit*).</summary>
internal struct SNalUnit
{
    public SNalUnitHeaderExt sNalHeaderExt;

    public SNalData sNalData;
    public ulong uiTimeStamp;
}

///////////////////////////////////ACCESS Unit level///////////////////////////////////

/* Access Unit structure */
/// <summary>TagAccessUnits (PAccessUnit is SAccessUnit*).</summary>
internal unsafe struct SAccessUnit
{
    public SNalUnit** pNalUnitsList;  // list of NAL Units pointer in this AU
    public uint32_t uiAvailUnitsNum;   // Number of NAL Units available in each AU list based current bitstream,
    public uint32_t uiActualUnitsNum;       // actual number of NAL units belong to current au
    // While available number exceeds count size below, need realloc extra NAL Units for list space.
    public uint32_t uiCountUnitsNum;        // Count size number of malloced NAL Units in each AU list
    public uint32_t uiStartPos;
    public uint32_t uiEndPos;
    public bool bCompletedAuFlag;       // Indicate whether it is a completed AU
}
