//-----------------------------------------------------------------------------
// Filename: SDPWellKnownMediaFormatsEnum.cs
//
// Description: Enum for common SDP formats.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 20 May 2025  Aaron Clauson   Refactored from MediaEndPoints.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorceryMedia.Abstractions;

/// <summary>
/// A list of standard media formats that can be identified by an ID if
/// there is no qualifying format attribute provided.
/// </summary>
/// <remarks>
/// For definition of well known types see: https://tools.ietf.org/html/rfc3551#section-6.
/// </remarks>
public enum SDPWellKnownMediaFormatsEnum
{
    PCMU = 0,       // Audio (8000/1).
    GSM = 3,        // Audio (8000/1).
    G723 = 4,       // Audio (8000/1).
    DVI4 = 5,       // Audio (8000/1).
    DVI4_16K = 6,   // Audio (16000/1).
    LPC = 7,        // Audio (8000/1).
    PCMA = 8,       // Audio (8000/1).
    G722 = 9,       // Audio (8000/1).
    L16_2 = 10,     // Audio (44100/2).
    L16 = 11,       // Audio (44100/1).
    QCELP = 12,     // Audio (8000/1).
    CN = 13,        // Audio (8000/1).
    MPA = 14,       // Audio (90000/*).
    G728 = 15,      // Audio (8000/1).
    DVI4_11K = 16,  // Audio (11025/1).
    DVI4_22K = 17,  // Audio (22050/1).
    G729 = 18,      // Audio (8000/1).

    CELB = 24,  // Video (90000).
    JPEG = 26,  // Video (90000).
    NV = 28,    // Video (90000).
    H261 = 31,  // Video (90000).
    MPV = 32,   // Video (90000).
    MP2T = 33,  // Audio/Video (90000).
    H263 = 34,  // Video (90000).
}
