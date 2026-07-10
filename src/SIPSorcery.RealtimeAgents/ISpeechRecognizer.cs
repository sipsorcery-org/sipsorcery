//-----------------------------------------------------------------------------
// Filename: ISpeechRecognizer.cs
//
// Description: The speech-to-text engine seam. The pipeline pushes decoded
// caller audio in (16-bit mono PCM, tagged with its rate - PCMU delivers 8kHz
// today but the seam doesn't bake that in) and utterance events come out.
//
// OnSpeechStarted exists for barge-in: the agent session cancels any speech in
// progress the moment the caller starts talking, without waiting for the full
// utterance to be recognised. Engines whose upstream service does its own
// voice-activity detection may never raise it; local/VAD-segmenting engines
// should.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Jul 2026  Aaron Clauson   Created, promoted from the WebRTCMaxHeadroom
//                              example with the sample rate generalised and
//                              the barge-in and partial-result events added.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace SIPSorcery.RealtimeAgents;

/// <summary>
/// A speech-to-text engine: decoded caller PCM is pushed in via
/// <see cref="WriteAudio"/> and recognised utterances are raised as events.
/// Implementations segment the audio themselves (local VAD) or delegate that
/// to a streaming service.
/// </summary>
public interface ISpeechRecognizer : IDisposable
{
    /// <summary>
    /// Raised when voice activity is first detected in a new utterance, as
    /// early as possible and before any text is available. The agent session
    /// uses this to interrupt the avatar (barge-in). Optional: engines that
    /// cannot detect onset never raise it.
    /// </summary>
    event Action? OnSpeechStarted;

    /// <summary>
    /// Raised with interim recognition results for the utterance in progress
    /// (e.g. live captions). Optional: batch engines never raise it.
    /// </summary>
    event Action<string>? OnPartialUtterance;

    /// <summary>Raised with the final text of each recognised utterance. Never empty and never partial.</summary>
    event Action<string>? OnUtterance;

    /// <summary>Initialises the engine and starts recognition. Safe to call once per instance.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pushes a block of decoded 16-bit mono PCM caller audio into the
    /// recogniser. Called from the transport's receive path; must be treated
    /// as a synchronous, non-blocking hand-off (copy what you need and
    /// return).
    /// </summary>
    void WriteAudio(ReadOnlySpan<short> pcm16, int sampleRateHz);
}
