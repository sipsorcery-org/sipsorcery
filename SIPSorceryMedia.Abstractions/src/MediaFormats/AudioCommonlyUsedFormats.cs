//-----------------------------------------------------------------------------
// Filename: AudioCommonlyUsedFormats.cs
//
// Description: Standard audio and video format definitions.
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
/// A list of audio formats that are commonly used but not standardised.
/// </summary>
public static class AudioCommonlyUsedFormats
{
    public const int OPUS_SAMPLE_RATE = (int)AudioSamplingRatesEnum.Rate48kHz;  // Opus codec typical sampling rate, 48KHz.
    public const int OPUS_CHANNELS = 2;                                         // Opus codec number of channels.

    /// <summary>
    /// The Opus audio format typical used for WebRTC scenarios.
    /// </summary>
    public static AudioFormat OpusWebRTC => new AudioFormat(111, nameof(AudioCodecsEnum.OPUS), OPUS_SAMPLE_RATE, OPUS_CHANNELS, "useinbandfec=1");
}