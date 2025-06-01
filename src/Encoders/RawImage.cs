//-----------------------------------------------------------------------------
// Filename: RawImage.cs
//
// Description: A raw image for use with a video codec.
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

using System;
using System.Runtime.InteropServices;

namespace SIPSorceryMedia.Abstractions;

public class RawImage
{
    /// <summary>
    /// The width, in pixels, of the image
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// The height, in pixels, of the image
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Integer that specifies the byte offset between the beginning of one scan line and the next.
    /// </summary>
    public int Stride { get; set; }

    /// <summary>
    /// Pointer to an array of bytes that contains the pixel data.
    /// </summary>
    public IntPtr Sample { get; set; }

    /// <summary>
    /// The pixel format of the image
    /// </summary>
    public VideoPixelFormatsEnum PixelFormat { get; set; }

    /// <summary>
    /// Get bytes array of the image.
    /// 
    /// For performance reasons it's better to use directly Sample
    /// </summary>
    /// <returns></returns>
    public byte[] GetBuffer()
    {
        byte[] result = null;

        if ((Height > 0) && (Stride > 0))
        {
            var bufferSize = Height * Stride;

            result = new byte[bufferSize];
            Marshal.Copy(Sample, result, 0, bufferSize);
        }
        return result;
    }
}