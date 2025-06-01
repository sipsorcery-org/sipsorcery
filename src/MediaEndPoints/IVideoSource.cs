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

public delegate void RawVideoSampleDelegate(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat);

public delegate void RawVideoSampleFasterDelegate(uint durationMilliseconds, RawImage rawImage); // Avoid to use byte[] to improve performance

public interface IVideoSource
{
    event EncodedSampleDelegate OnVideoSourceEncodedSample;

    event RawVideoSampleDelegate OnVideoSourceRawSample;

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
