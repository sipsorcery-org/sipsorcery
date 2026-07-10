//-----------------------------------------------------------------------------
// Filename: ISpeechSynthesizer.cs
//
// Description: The text-to-speech ENGINE seam: text in, PCM segments out,
// nothing else. This is a deliberate narrowing of the example projects'
// IAvatarSpeaker, which coupled synthesis to playback pacing, the audio track
// and the avatar mouth - so every engine (sherpa-onnx, ElevenLabs batch,
// ElevenLabs websocket) re-implemented that plumbing, and the copies drifted.
// In the package the agent session owns playback, pacing (per
// IAvatarMouth.PacesAudioInternally), mouth-driving and cancellation once;
// engines only convert text to audio.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Jul 2026  Aaron Clauson   Created, distilled from the WebRTCMaxHeadroom
//                              IAvatarSpeaker implementations.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;

namespace SIPSorcery.RealtimeAgents;

/// <summary>
/// A text-to-speech engine: synthesises text into a stream of 16-bit mono PCM
/// segments. Playback, pacing and lip-sync are the pipeline's job, not the
/// engine's.
/// </summary>
public interface ISpeechSynthesizer : IDisposable
{
    /// <summary>
    /// Synthesises <paramref name="text"/>, yielding audio segments as they
    /// become available. A batch engine may yield a single segment; a
    /// streaming engine should yield early and often so the avatar starts
    /// speaking before synthesis completes. Cancelling the token abandons the
    /// remainder of the utterance (barge-in).
    /// </summary>
    IAsyncEnumerable<AudioSegment> SynthesizeAsync(string text, CancellationToken cancellationToken = default);
}

/// <summary>
/// A text-to-speech engine that can consume text INCREMENTALLY as it is
/// produced (e.g. an LLM sentence stream) over a single synthesis session,
/// letting synthesis overlap generation - e.g. the ElevenLabs websocket API.
/// Engines without such a mode just implement <see cref="ISpeechSynthesizer"/>
/// and the pipeline feeds them sentence-by-sentence.
/// </summary>
public interface IStreamingSpeechSynthesizer : ISpeechSynthesizer
{
    /// <summary>
    /// Synthesises a stream of text chunks, yielding audio segments as they
    /// become available. Cancelling the token abandons both the text stream
    /// and any pending audio (barge-in).
    /// </summary>
    IAsyncEnumerable<AudioSegment> SynthesizeAsync(IAsyncEnumerable<string> textChunks, CancellationToken cancellationToken = default);
}
