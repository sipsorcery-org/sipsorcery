//-----------------------------------------------------------------------------
// Filename: IVideoSource.cs
//
// Description: Interface to represent a video source or capture device,
// such as a webcam.
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
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions;

/// <summary>
/// Delegate for a captured video frame supplied as a managed array.
/// </summary>
/// <remarks>
/// The sample is only valid for the duration of the call. Handlers that display the frame should
/// copy it into a bitmap they own, re-used across frames. See <see cref="IVideoSource"/>.
/// </remarks>
public delegate void RawVideoSampleDelegate(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat);

/// <summary>
/// Delegate for a captured video frame supplied as a pointer, avoiding a byte[] allocation per frame.
/// </summary>
/// <remarks>
/// <see cref="RawImage.Sample"/> points at memory owned by the source and is only valid for the
/// duration of the call. See <see cref="IVideoSource"/>.
/// </remarks>
public delegate void RawVideoSampleFasterDelegate(uint durationMilliseconds, RawImage rawImage); // Avoid to use byte[] to improve performance

/// <summary>
/// Interface to represent a video source or capture device.
/// </summary>
/// <remarks>
/// <para>
/// Captured frames are handed to subscribers as borrowed memory that is only valid for the duration
/// of the event handler. Sources re-use a single conversion buffer for every frame, and may free it
/// when the source closes, so a frame retained beyond the call reads memory that has since been
/// overwritten or freed.
/// </para>
/// <para>
/// A handler that renders the frame synchronously can use the sample in place. A handler that defers
/// the work, for example marshalling to a UI thread, must copy the sample first. The recommended
/// pattern is to copy into a bitmap the caller owns, allocated once and re-used until the frame
/// dimensions change, rather than allocating a bitmap per frame or wrapping the supplied buffer.
/// </para>
/// </remarks>
public interface IVideoSource
{
    event EncodedSampleDelegate OnVideoSourceEncodedSample;

    /// <summary>
    /// Fired for each captured frame. The sample is only valid for the duration of the handler.
    /// See <see cref="IVideoSource"/>.
    /// </summary>
    event RawVideoSampleDelegate OnVideoSourceRawSample;

    /// <summary>
    /// As for <see cref="OnVideoSourceRawSample"/> but avoids a byte[] allocation per frame. The
    /// <see cref="RawImage.Sample"/> pointer is only valid for the duration of the handler.
    /// See <see cref="IVideoSource"/>.
    /// </summary>
    event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster; // Avoid to use byte[] to improve performance

    event SourceErrorDelegate OnVideoSourceError;

    Task PauseVideo();

    Task ResumeVideo();

    Task StartVideo();

    Task CloseVideo();

    List<VideoFormat> GetVideoSourceFormats();

    void SetVideoSourceFormat(VideoFormat videoFormat);

    void RestrictFormats(Func<VideoFormat, bool> filter);

    void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat);

    void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage); // Avoid to use byte[] to improve performance

    void ForceKeyFrame();

    bool HasEncodedVideoSubscribers();

    bool IsVideoSourcePaused();
}
