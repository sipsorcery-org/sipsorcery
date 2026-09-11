//-----------------------------------------------------------------------------
// Filename: IAvatarMouth.cs
//
// Description: The speech-driven half of an avatar - the seam the whole pipeline is
// built around. Fed synthesised PCM between BeginSpeech and EndSpeech, an IAvatarMouth
// animates the face however it likes. It is deliberately independent of any video
// source so a face that renders itself elsewhere (e.g. the Godot VRM renderer, which
// owns its own WebRTC video path) can be driven by the same speakers as the in-process
// SkiaSharp/Wav2Lip renderers (which combine it with IVideoSource via IAvatarRenderer).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace demo;

public interface IAvatarMouth
{
    /// <summary>
    /// True when the renderer buffers and paces speech audio itself (e.g. the Wav2Lip
    /// renderer's 25fps loop): the speaker should push audio AS FAST AS IT IS AVAILABLE so
    /// the model's look-ahead never starves. False when the renderer reacts to
    /// <see cref="PushAudio"/> immediately (the cartoon's amplitude heuristic): the speaker
    /// must pace pushes to real time alongside playback.
    /// </summary>
    bool PacesAudioInternally { get; }

    /// <summary>Called once at the start of an utterance so the renderer can enter its "talking" state.</summary>
    void BeginSpeech();

    /// <summary>
    /// Feeds a window of 16-bit mono PCM to the renderer, which uses it to drive the face.
    /// Called repeatedly between <see cref="BeginSpeech"/> and <see cref="EndSpeech"/> - paced
    /// to playback, or as fast as available, per <see cref="PacesAudioInternally"/>.
    /// Must not block.
    /// </summary>
    void PushAudio(ReadOnlySpan<short> pcm16, int sampleRate);

    /// <summary>Called once at the end of an utterance; the renderer should return the face to neutral.</summary>
    void EndSpeech();
}
