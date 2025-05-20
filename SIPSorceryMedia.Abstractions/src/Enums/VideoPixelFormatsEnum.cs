//-----------------------------------------------------------------------------
// Filename: VideoPixelFormatsEnum.cs
//
// Description: Enum for common video pixel formats.
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

public enum VideoPixelFormatsEnum
{
    Rgb = 0,        // 24 bits per pixel.
    Bgr = 1,        // 24 bits per pixel.
    Bgra = 2,       // 32 bits per pixel.
    I420 = 3,       // 12 bits per pixel.
    NV12 = 4,       // 12 bits per pixel.
    Rgba = 5,       // 32 bits per pixel.
}
