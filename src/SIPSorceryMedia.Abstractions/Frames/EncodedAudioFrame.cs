//-----------------------------------------------------------------------------
// Filename: EncodedAudioFrame.cs
//
// Description: Represents an encoded audio frame received from the RTP transport.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 30 May 2025  Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorceryMedia.Abstractions;

/// <summary>
/// Represents an encoded media frame. Typically received from the RTP transport.
/// </summary>
public class EncodedAudioFrame
{
    public EncodedAudioFrame(int mediaStreamIndex, AudioFormat mediaformat, uint durationMilliSeconds, ReadOnlyMemory<byte> encodedMedia)
    {
        MediaStreamIndex = mediaStreamIndex;
        AudioFormat = mediaformat;
        DurationMilliSeconds = durationMilliSeconds;
        EncodedAudio = encodedMedia;
    }

    public int MediaStreamIndex { get; }

    public AudioFormat AudioFormat { get; }

    public uint DurationMilliSeconds { get; }

    public ReadOnlyMemory<byte> EncodedAudio { get; }
}
