//-----------------------------------------------------------------------------
// Filename: AudioSegment.cs
//
// Description: A chunk of 16-bit mono PCM speech audio flowing through the
// agent pipeline, e.g. from a speech synthesiser towards the transport's
// audio track and the avatar's mouth.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Jul 2026  Aaron Clauson   Created, extracted from the WebRTCMaxHeadroom
//                              and WebRTCGodotAvatar prototypes.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.RealtimeAgents;

/// <summary>
/// A chunk of 16-bit mono PCM speech audio, tagged with its sample rate so
/// consumers (playback pacing, resamplers, avatar mouths) never have to guess.
/// Segments are the unit of streaming between pipeline stages; an engine that
/// synthesises a whole utterance at once simply yields a single segment.
/// </summary>
public readonly record struct AudioSegment(System.ReadOnlyMemory<short> Pcm16, int SampleRateHz)
{
    /// <summary>The playback duration of this segment in milliseconds.</summary>
    public int DurationMilliseconds => SampleRateHz > 0 ? (int)((long)Pcm16.Length * 1000 / SampleRateHz) : 0;
}
