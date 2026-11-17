//-----------------------------------------------------------------------------
// Filename: MediaEndPoints.cs
//
// Description: Reresents the media end points, sink and sources, that can be
// attached to the real-time transports (e.g. SIP or WebRTC).
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

namespace SIPSorceryMedia.Abstractions;

public delegate void EncodedSampleDelegate(uint durationRtpUnits, byte[] sample);

/// <summary>
/// Delegate for an encoded video frame supplied in memory owned by the source, avoiding a byte[] allocation per frame.
/// </summary>
/// <param name="durationRtpUnits">The duration of the frame in RTP timestamp units.</param>
/// <param name="sample">The encoded frame. It is only valid for the duration of the call: the source writes the next
/// frame into the same memory. A handler that needs the frame later must copy it, for example with
/// <see cref="ReadOnlySpan{T}.ToArray"/>. A span based RTP send method, which copies each packet it builds, can use it
/// in place.</param>
public delegate void EncodedSampleFasterDelegate(uint durationRtpUnits, ReadOnlySpan<byte> sample);


public delegate void SourceErrorDelegate(string errorMessage);

public class MediaEndPoints
{
    public IAudioSource AudioSource { get; set; }
    public IAudioSink AudioSink { get; set; }
    public IVideoSource VideoSource { get; set; }
    public IVideoSink VideoSink { get; set; }
    public ITextSource TextSource { get; set; }
    public ITextSink TextSink { get; set; }
}
