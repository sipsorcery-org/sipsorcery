//-----------------------------------------------------------------------------
// Filename: IAudioEncoder.cs
//
// Description: Common interface for an audio codec.
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
using System.Buffers;
using System.Collections.Generic;

namespace SIPSorceryMedia.Abstractions;

public interface IAudioEncoder
{
    /// <summary>
    /// Needs to be set with the list of audio formats that the encoder supports.
    /// </summary>
    List<AudioFormat> SupportedFormats { get; }

    /// <summary>
    /// Encodes 16bit signed PCM samples.
    /// </summary>
    /// <param name="pcm">An array of 16 bit signed audio samples.</param>
    /// <param name="format">The audio format to encode the PCM sample to.</param>
    /// <param name="destination">A <see cref="IBufferWriter{T}"/> of <see langword="byte"/> to receieve the encoded sample.</param>
    void EncodeAudio(ReadOnlySpan<short> pcm, AudioFormat format, IBufferWriter<byte> destination);

    /// <summary>
    /// Decodes to 16bit signed PCM samples.
    /// </summary>
    /// <param name="encodedSample">The span containing the encoded sample.</param>
    /// <param name="format">The audio format of the encoded sample.</param>
    /// <param name="destination">A <see cref="IBufferWriter{T}"/> of <see langword="short"/> to receive the decoded PCM samples.</param>
    void DecodeAudio(ReadOnlySpan<byte> encodedSample, AudioFormat format, IBufferWriter<short> destination);
}
