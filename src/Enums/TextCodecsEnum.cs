//-----------------------------------------------------------------------------
// Filename: TextCodecsEnum.cs
//
// Description: Enum for common text codecs.
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

public enum TextCodecsEnum
{
    Unknown,
    T140, //T.140 specifies that text and other T.140 elements must be transmitted in ISO 10646-1 code with UTF-8 transformation.
    RED,
}
