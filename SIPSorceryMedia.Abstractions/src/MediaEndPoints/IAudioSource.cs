//-----------------------------------------------------------------------------
// Filename: IAudioSource.cs
//
// Description: Interface to represent an audio source or capture device,
// such as a microphone.
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
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions;

public interface IAudioSource
{
    [Obsolete("Use OnAudioSourceEncodedFrameReady instead.")]
    event Action<uint, ReadOnlyMemory<byte>> OnAudioSourceEncodedSample;

    event Action<EncodedAudioFrame> OnAudioSourceEncodedFrameReady;

    event Action<AudioSamplingRatesEnum, uint, short[]> OnAudioSourceRawSample;

    event SourceErrorDelegate OnAudioSourceError;

    Task PauseAudio();

    Task ResumeAudio();

    Task StartAudio();

    Task CloseAudio();

    List<AudioFormat> GetAudioSourceFormats();

    void SetAudioSourceFormat(AudioFormat audioFormat);

    void RestrictFormats(Func<AudioFormat, bool> filter);

    void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample);

    bool HasEncodedAudioSubscribers();

    bool IsAudioSourcePaused();
}