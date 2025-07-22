//-----------------------------------------------------------------------------
// Filename: AudioSamplingRatesEnum.cs
//
// Description: Enum for common audio sampling rates.
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

public enum AudioSamplingRatesEnum
{
    Rate8KHz = 8000,
    Rate16KHz = 16000,
    Rate24kHz = 24000,
    Rate44_1kHz = 44100,
    Rate48kHz = 48000
}
