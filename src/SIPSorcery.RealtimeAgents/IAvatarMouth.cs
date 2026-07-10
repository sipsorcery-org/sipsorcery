//-----------------------------------------------------------------------------
// Filename: IAvatarMouth.cs
//
// Description: The minimal "speech audio drives a face" contract. This is the
// one seam that crosses into EVERY avatar host, however the video reaches the
// viewer:
//
//  * In-process renderers (the WebRTCMaxHeadroom SkiaSharp cartoon and Wav2Lip
//    head) implement the composed IAvatarRenderer, which is this interface
//    plus IVideoSource - the pipeline also owns their video track.
//  * Engine-hosted avatars (the WebRTCGodotAvatar Godot VRM/Live2D prototype,
//    a Unity puppet) implement ONLY this interface. The engine owns its own
//    capture -> encode -> send path and the pipeline never sees the video.
//
// The same decoupling LiveKit's avatar agents use (TTS audio in, the animation
// engine does whatever it likes with it) and bitHuman's push_audio()/flush().
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Jul 2026  Aaron Clauson   Created, promoted from the WebRTCMaxHeadroom
//                              IAvatarRenderer seam after the WebRTCGodotAvatar
//                              prototype showed the mouth contract must be
//                              separable from IVideoSource.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;

namespace SIPSorcery.RealtimeAgents;

/// <summary>
/// An avatar face driven by speech audio. The pipeline hands the synthesised
/// PCM to the mouth as it is spoken; the implementation animates however it
/// likes (viseme heuristics, an audio-to-video model, engine morph targets).
/// </summary>
/// <remarks>
/// Threading contract: <see cref="BeginSpeech"/> is called once before an
/// utterance and one of <see cref="EndSpeech"/> / <see cref="AbortSpeech"/>
/// once after. <see cref="PushAudio"/> is called repeatedly between those,
/// from the pipeline's speech thread, and must be treated as a synchronous,
/// non-blocking hand-off: copy what you need and return.
/// </remarks>
public interface IAvatarMouth
{
    /// <summary>
    /// True when the implementation buffers and paces speech audio itself
    /// (e.g. an audio-to-video model with a fixed-fps render loop that needs
    /// look-ahead): the pipeline pushes audio AS FAST AS IT IS AVAILABLE so
    /// the model never starves. False when the implementation reacts to
    /// <see cref="PushAudio"/> immediately (amplitude/viseme heuristics): the
    /// pipeline paces pushes to real time alongside playback.
    /// </summary>
    bool PacesAudioInternally { get; }

    /// <summary>Called once at the start of an utterance so the face can enter its "talking" state.</summary>
    void BeginSpeech();

    /// <summary>
    /// Feeds a window of 16-bit mono PCM that is being spoken on the audio
    /// track. Called repeatedly between <see cref="BeginSpeech"/> and
    /// <see cref="EndSpeech"/> - paced to playback, or as fast as available,
    /// per <see cref="PacesAudioInternally"/>. Must not block.
    /// </summary>
    void PushAudio(ReadOnlySpan<short> pcm16, int sampleRateHz);

    /// <summary>
    /// Called once at the natural end of an utterance. An implementation that
    /// paces internally should finish playing out any buffered audio, then
    /// return the face to neutral.
    /// </summary>
    void EndSpeech();

    /// <summary>
    /// Called when the utterance is cut short (barge-in, disconnect). Unlike
    /// <see cref="EndSpeech"/> any buffered audio must be DISCARDED and the
    /// face returned to neutral immediately.
    /// </summary>
    void AbortSpeech();
}
