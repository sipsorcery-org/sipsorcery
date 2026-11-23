//-----------------------------------------------------------------------------
// Filename: VideoSample.cs
//
// Description: Representation of an unencoded video sample.
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

public struct VideoSample
{
    public uint Width;
    public uint Height;
    public byte[] Sample;

    /// <summary>
    /// The pixel format of <see cref="Sample"/>. Decoders set this to the format they actually
    /// produced, which is not necessarily the format that was requested of them.
    /// </summary>
    public VideoPixelFormatsEnum PixelFormat;
}
