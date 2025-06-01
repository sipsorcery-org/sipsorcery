//-----------------------------------------------------------------------------
// Filename: AudioCodecsEnum.cs
//
// Description: Enum for common audio codecs.
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

public enum AudioCodecsEnum
{
    PCMU,
    GSM,
    G723,
    DVI4,
    LPC,
    PCMA,
    G722,
    L16,
    QCELP,
    CN,
    MPA,
    G728,
    G729,
    OPUS,

    PCM_S16LE,  // PCM signed 16-bit little-endian (equivalent to FFmpeg s16le). For use with Azure, not likely to be supported in VoIP/WebRTC.

    Unknown
}
