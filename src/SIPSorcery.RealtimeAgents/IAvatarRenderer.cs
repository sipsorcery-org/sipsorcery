//-----------------------------------------------------------------------------
// Filename: IAvatarRenderer.cs
//
// Description: The composed contract for an avatar whose video is produced
// in-process: a speech-driven face (IAvatarMouth) that is also a video source
// the pipeline can attach to a peer connection or RTP session (IVideoSource).
//
// Implementations from the examples: the WebRTCMaxHeadroom SkiaSharp cartoon
// (amplitude -> viseme heuristic) and its Wav2Lip photoreal head (PCM -> an
// audio-to-video model whose frames become the encoded samples).
//
// Avatars hosted in an external engine (Godot, Unity) should NOT implement
// this interface - they own their own capture/encode/send path and implement
// only IAvatarMouth. See that interface for the split's rationale.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Jul 2026  Aaron Clauson   Created, promoted from the WebRTCMaxHeadroom
//                              example.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.RealtimeAgents;

/// <summary>
/// An in-process avatar: a speech-driven face (<see cref="IAvatarMouth"/>)
/// that also publishes encoded video frames like any other
/// <see cref="IVideoSource"/>, so the pipeline can own its video track.
/// </summary>
public interface IAvatarRenderer : IAvatarMouth, IVideoSource, IDisposable
{
}
