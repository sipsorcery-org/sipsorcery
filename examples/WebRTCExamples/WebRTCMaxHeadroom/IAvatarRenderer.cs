//-----------------------------------------------------------------------------
// Filename: IAvatarRenderer.cs
//
// Description: The swappable "renderer" seam for the avatar. An IAvatarRenderer is a
// video source (it publishes encoded frames to the WebRTC peer connection like any
// IVideoSource) that is *driven by speech audio*: the speaker hands it the synthesised
// PCM and the renderer animates the face however it likes.
//
// This is the same decoupling LiveKit's avatar agents use (an AvatarSession that
// receives the agent's TTS audio via DataStreamAudioOutput and publishes a video track)
// and that bitHuman's SDK exposes with push_audio()/flush(). Splitting the contract this
// way makes the animation engine interchangeable:
//
//   * MaxHeadroomVideoSource - the in-box SkiaSharp cartoon. PushAudio computes a short
//     RMS window and maps loudness onto one of the 0-21 viseme mouth shapes. The face is
//     drawn procedurally, so "lip-sync" is an amplitude heuristic.
//   * Wav2LipAvatarRenderer - the photoreal head: PushAudio feeds the PCM to the Wav2Lip
//     audio-to-video model (via onnxruntime) whose emitted frames become the encoded
//     samples. The face is a photo, so lip-sync is done by the model, not the caller.
//
// The speaker (LipSyncTtsSpeaker and ElevenLabsStreamingTtsSpeaker) only sees this
// interface, so neither knows or cares which renderer is behind it - exactly like
// swapping tavus.AvatarSession for bithuman.AvatarSession in the LiveKit examples.
//
// Contract / threading:
//   * BeginSpeech is called once before an utterance, EndSpeech once after (the renderer
//     should return the face to a neutral/closed state on EndSpeech).
//   * PushAudio is called repeatedly *between* those - paced to playback, or as fast as
//     available, per PacesAudioInternally - with windows of 16-bit mono PCM.
//     Implementations must treat it as a synchronous, non-blocking hand-off (copy what
//     they need and return).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorceryMedia.Abstractions;

namespace demo;

/// <summary>
/// A video source whose animation is driven by speech audio. See the file header for the
/// rationale; implementations are <see cref="MaxHeadroomVideoSource"/> (SkiaSharp cartoon)
/// and <see cref="Wav2LipAvatarRenderer"/> (photoreal audio-to-video model). The mouth contract
/// lives in <see cref="IAvatarMouth"/> (in the shared AvatarPipeline library) so it can be reused
/// by renderers that are not themselves in-process video sources (e.g. the Godot VRM demo).
/// </summary>
public interface IAvatarRenderer : IAvatarMouth, IVideoSource, IDisposable
{
}
