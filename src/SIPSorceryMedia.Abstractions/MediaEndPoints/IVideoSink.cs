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

/// <summary>
/// Delegate for a decoded video frame supplied as a managed array.
/// </summary>
/// <remarks>
/// The sample is only valid for the duration of the call. Handlers that display the frame should
/// copy it into a bitmap they own, rather than wrapping the buffer, and should re-use that bitmap
/// across frames instead of allocating one per frame. See <see cref="IVideoSink"/> for why.
/// </remarks>
public delegate void VideoSinkSampleDecodedDelegate(byte[] sample, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat);

/// <summary>
/// Delegate for a decoded video frame supplied as a pointer, avoiding a byte[] allocation per frame.
/// </summary>
/// <remarks>
/// <see cref="RawImage.Sample"/> points at memory owned by the decoder and is only valid for the
/// duration of the call. See <see cref="IVideoSink"/> for the recommended way to display it.
/// </remarks>
public delegate void VideoSinkSampleDecodedFasterDelegate(RawImage rawImage); // Avoid to use byte[] to improve performance

/// <summary>
/// Interface to represent a video sink.
/// </summary>
/// <remarks>
/// <para>
/// Decoded frames are handed to subscribers as borrowed memory that is only valid for the duration
/// of the event handler. Decoders re-use a single conversion buffer for every frame, so the pointer
/// supplied with one frame is overwritten by the next, and the managed array supplied to the byte[]
/// overload becomes unreachable as soon as the handler returns.
/// </para>
/// <para>
/// A handler that renders the frame synchronously can use the sample in place. A handler that defers
/// the work, for example marshalling to a UI thread, must copy the sample first. Wrapping the buffer
/// in something that outlives the call, such as a Bitmap constructed over the pointer, reads memory
/// that has since been overwritten or freed.
/// </para>
/// <para>
/// The recommended pattern is to copy each frame into a bitmap the caller owns, allocated once and
/// re-used until the frame dimensions change. Allocating a bitmap per frame costs several times more
/// than the copy itself and, at high resolutions, generates enough garbage to stall the application.
/// </para>
/// </remarks>
public interface IVideoSink
{
    /// <summary>
    /// This event will be fired by the sink after is decodes a video frame from the RTP stream.
    /// </summary>
    /// <remarks>
    /// The sample is only valid for the duration of the handler. See <see cref="IVideoSink"/>.
    /// </remarks>
    event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

    /// <summary>
    /// As for <see cref="OnVideoSinkDecodedSample"/> but avoids a byte[] allocation per frame.
    /// </summary>
    /// <remarks>
    /// The <see cref="RawImage.Sample"/> pointer is only valid for the duration of the handler.
    /// See <see cref="IVideoSink"/>.
    /// </remarks>
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