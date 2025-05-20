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

namespace SIPSorceryMedia.Abstractions;

public delegate void EncodedSampleDelegate(uint durationRtpUnits, byte[] sample);


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
