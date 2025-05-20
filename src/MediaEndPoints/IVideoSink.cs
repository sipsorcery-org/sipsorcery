//-----------------------------------------------------------------------------
// Filename: IVideoSink.cs
//
// Description: Interface to represent a video sink. Typically a video sink is
// a bitmap on a screen or a video file.
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
using System.Net;
using System.Threading.Tasks;

namespace SIPSorceryMedia.Abstractions;

public delegate void VideoSinkSampleDecodedDelegate(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat);

public delegate void VideoSinkSampleDecodedFasterDelegate(RawImage rawImage); // Avoid to use byte[] to improve performance

public interface IVideoSink
{
    /// <summary>
    /// This event will be fired by the sink after is decodes a video frame from the RTP stream.
    /// </summary>
    event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

    event VideoSinkSampleDecodedFasterDelegate OnVideoSinkDecodedSampleFaster; // Avoid to use byte[] to improve performance

    void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload);

    void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] payload, VideoFormat format);

    List<VideoFormat> GetVideoSinkFormats();

    void SetVideoSinkFormat(VideoFormat videoFormat);

    void RestrictFormats(Func<VideoFormat, bool> filter);

    Task PauseVideoSink();

    Task ResumeVideoSink();

    Task StartVideoSink();

    Task CloseVideoSink();
}