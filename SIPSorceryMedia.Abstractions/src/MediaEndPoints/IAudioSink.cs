//-----------------------------------------------------------------------------
// Filename: IAudioSink.cs
//
// Description: Interface to represent an audio sink or playback device,
// such as a speaker.
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
using System.Net;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions;

public interface IAudioSink
{
    event SourceErrorDelegate OnAudioSinkError;

    List<AudioFormat> GetAudioSinkFormats();

    void SetAudioSinkFormat(AudioFormat audioFormat);

    void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame);

    void RestrictFormats(Func<AudioFormat, bool> filter);

    Task PauseAudioSink();

    Task ResumeAudioSink();

    Task StartAudioSink();

    Task CloseAudioSink();
}