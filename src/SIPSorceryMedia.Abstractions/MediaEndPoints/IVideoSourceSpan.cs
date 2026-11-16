//-----------------------------------------------------------------------------
// Filename: IVideoSourceSpan.cs
//
// Description: Interface for a video source that can also raise each encoded
// frame as a span over memory it re-uses.
//
// Author(s):
// SIPSorcery contributors
//
// History:
// 16 Nov 2026  SIPSorcery contributors  Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorceryMedia.Abstractions;

/// <summary>
/// A video source that raises each encoded frame as a span over memory it re-uses, as well as in a new array on
/// <see cref="IVideoSource.OnVideoSourceEncodedSample"/>. Code that sends a source's frames checks for this interface
/// and subscribes a span based send method, such as <c>RTPSession.SendVideo(uint, ReadOnlySpan&lt;byte&gt;)</c>, to
/// <see cref="OnVideoSourceEncodedSampleFaster"/> instead, which saves the array per frame. <c>VoIPMediaSession</c>
/// does so for its video sources.
/// </summary>
/// <remarks>
/// <see cref="EncodedVideoSampleEvents"/> implements both events for a source, encoding each frame once whichever of
/// them have subscribers.
/// </remarks>
public interface IVideoSourceSpan : IVideoSource
{
    /// <summary>
    /// Fired, like <see cref="IVideoSource.OnVideoSourceEncodedSample"/>, for every encoded frame, but with the frame in
    /// memory the source re-uses for the next one. The sample is only valid for the duration of the handler: a handler
    /// that needs the frame later must copy it.
    /// </summary>
    event EncodedSampleFasterDelegate OnVideoSourceEncodedSampleFaster;
}
