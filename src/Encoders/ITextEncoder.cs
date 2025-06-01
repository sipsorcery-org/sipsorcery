//-----------------------------------------------------------------------------
// Filename: ITextEncoder.cs
//
// Description: Common interface for a text codec.
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

public interface ITextEncoder
{
    /// <summary>
    /// Encode a text into a byte array.
    /// </summary>
    /// <param name="text">A symbol or text to be transmitted</param>
    /// <param name="format">The text format of the sample.</param>
    /// <returns>A byte array containing the encoded text sample</returns>
    byte[] EncodeText(char[] text, TextFormat format);

    /// <summary>
    /// Decode a byte array into a string type text.
    /// </summary>
    /// <param name="encodedSample">A symbol or text that was received</param>
    /// <param name="format">The text format of the sample.</param>
    /// <returns></returns>
    char[] DecodeText(byte[] encodedSample, TextFormat format);
}