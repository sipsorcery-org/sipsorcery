//-----------------------------------------------------------------------------
// Filename: FFmpegVideoEndPoint.cs
//
// Description: A combined video source and sink backed by an FFmpeg codec. As a
// sink it decodes full video frames received from the remote party; as a source
// it encodes raw frames supplied by the application (via the
// ExternalVideoSourceRawSample* methods) for transmission. A single
// FFmpegVideoEncoder instance handles both directions (it maintains independent
// encode and decode contexts internally), so the endpoint encodes outgoing and
// decodes incoming media using the same negotiated codec.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Jun 2026  Aaron Clauson   Added the missing IVideoSource interface and implementation.
// 19 Sep 2026  Aaron Clauson   Added full IVideoEndPoint implementation and wired up the
//                              OnVideoSinkDecodedSample event.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.FFmpeg;

public class FFmpegVideoEndPoint : IVideoEndPoint, IDisposable
{
    public static ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEndPoint>();

    public static readonly List<VideoFormat> _supportedFormats = Helper.GetSupportedVideoFormats();

    private readonly FFmpegVideoEncoder _ffmpegEncoder;

    private readonly MediaFormatManager<VideoFormat> _videoFormatManager;
    private bool _isStarted;
    private bool _isPaused;
    private bool _isClosed;

    // The sink (decode) side has its own pause/close state so that controlling one direction does not affect
    // the other. Note the encoder instance is shared, so closing the source also stops the sink.
    private bool _isSinkPaused;
    private bool _isSinkClosed;

    /// <summary>
    /// Fires when an encoded frame has been decoded and is ready for the caller. This event differs
    /// from <see cref="OnVideoSinkDecodedSampleFaster"/> by handing over a new byte buffer for every event. This
    /// makes it safe to hold on to the sample after the handler returns but less performant due to the extra
    /// allocation and copy.
    /// </summary>
    public event VideoSinkSampleDecodedDelegate? OnVideoSinkDecodedSample;

    /// <summary>
    /// Fires when an encoded frame has been decoded and is ready for the caller. The supplied
    /// <see cref="RawImage"/> points at memory owned by the decoder that is re-used for the next frame, so it
    /// is only valid for the duration of the handler. Copy it, for example with <see cref="RawImage.GetBuffer"/>,
    /// if it is needed after the handler returns.
    /// </summary>
    public event VideoSinkSampleDecodedFasterDelegate? OnVideoSinkDecodedSampleFaster;

    /// <summary>
    /// Fired when a raw sample supplied via <see cref="ExternalVideoSourceRawSample"/> or
    /// <see cref="ExternalVideoSourceRawSampleFaster"/> has been encoded and is ready to transmit.
    /// </summary>
    public event EncodedSampleDelegate? OnVideoSourceEncodedSample;

#pragma warning disable CS0067
    // This endpoint only produces ENCODED video samples (it encodes raw input supplied via the
    // ExternalVideoSourceRawSample* methods). It never emits raw source samples.

    /// <summary>
    /// Not raised by this endpoint. It is part of the <see cref="IVideoSource"/> contract but this endpoint only
    /// encodes raw frames supplied to it via <see cref="ExternalVideoSourceRawSample"/>, it does not produce them.
    /// For a local preview subscribe to the raw sample event on the source that is supplying those frames, for
    /// example a capture device or test pattern source.
    /// </summary>
    public event RawVideoSampleDelegate? OnVideoSourceRawSample;

    /// <summary>
    /// Not raised by this endpoint. It is part of the <see cref="IVideoSource"/> contract but this endpoint only
    /// encodes raw frames supplied to it via <see cref="ExternalVideoSourceRawSampleFaster"/>, it does not produce
    /// them. For a local preview subscribe to the raw sample event on the source that is supplying those frames,
    /// for example a capture device or test pattern source.
    /// </summary>
    public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster;
#pragma warning restore CS0067

    /// <summary>
    /// Fired when encoding a raw sample supplied via <see cref="ExternalVideoSourceRawSample"/> or
    /// <see cref="ExternalVideoSourceRawSampleFaster"/> fails.
    /// </summary>
    public event SourceErrorDelegate? OnVideoSourceError;

    public FFmpegVideoEndPoint(Dictionary<string, string>? decoderOptions = null)
    {
        FFmpegInit.EnsureBinariesRegistered();
        _videoFormatManager = new MediaFormatManager<VideoFormat>(_supportedFormats);
        _ffmpegEncoder = new FFmpegVideoEncoder(decoderOptions);
    }

    public MediaEndPoints ToMediaEndPoints()
    {
        return new MediaEndPoints
        {
            VideoSource = this,
            VideoSink = this
        };
    }

    public List<VideoFormat> GetVideoSinkFormats() => _videoFormatManager.GetSourceFormats();
    public void SetVideoSinkFormat(VideoFormat videoFormat) => _videoFormatManager.SetSelectedFormat(videoFormat);
    public void RestrictFormats(Func<VideoFormat, bool> filter) => _videoFormatManager.RestrictFormats(filter);
    public List<VideoFormat> GetVideoSourceFormats() => _videoFormatManager.GetSourceFormats();
    public void SetVideoSourceFormat(VideoFormat videoFormat) => _videoFormatManager.SetSelectedFormat(videoFormat);
    public void ForceKeyFrame() => _ffmpegEncoder.ForceKeyFrame();
    public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
    public bool IsVideoSourcePaused() => _isPaused;
    public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload) =>
        throw new ApplicationException("The FFmpeg Video End Point requires full video frames rather than individual RTP packets.");

    public void SetDecoderWrapper(string wrapperName) => _ffmpegEncoder.SetCodec(wrapperName);

    public bool SetDecoderForCodec(VideoCodecsEnum codec, string name, Dictionary<string, string>? opts = null)
    {
        AVCodecID? codecID = FFmpegConvert.GetAVCodecID(codec);

        if (codecID == null)
        {
            logger.LogError("Codec {codec} is not supported by this endpoint.", codec);
            throw new InvalidOperationException($"Codec {codec} is not supported by this endpoint.");
        }

        return _ffmpegEncoder.SetCodec(codecID.Value, name, opts);
    }

    public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] payload, VideoFormat format)
    {
        if (!_isClosed && !_isSinkClosed && !_isSinkPaused && payload != null &&
            (OnVideoSinkDecodedSampleFaster != null || OnVideoSinkDecodedSample != null))
        {
            if (_videoFormatManager.SelectedFormat.Codec != format.Codec)
            {
                if (_videoFormatManager.GetSourceFormats().Exists(f => f.Codec == format.Codec))
                {
                    logger.LogWarning("Video format {format} is not selected but supported, continuing by using it.", format.FormatName);
                    _videoFormatManager.SetSelectedFormat(format);
                }
                else
                {
                    logger.LogError("Video format {format} is not supported by this endpoint.", format.FormatName);
                    return;
                }
            }

            AVCodecID? codecID = FFmpegConvert.GetAVCodecID(_videoFormatManager.SelectedFormat.Codec);
            if(codecID != null)
            {
                var imageRawSamples = _ffmpegEncoder.DecodeFaster(codecID.Value, payload, out var width, out var height);

                if (imageRawSamples == null || width == 0 || height == 0)
                {
                    logger.LogWarning("Decode of video sample failed, width {Width}, height {Height}.", width, height);
                }
                else
                {
                    foreach (var imageRawSample in imageRawSamples)
                    {
                        OnVideoSinkDecodedSampleFaster?.Invoke(imageRawSample);

                        if (OnVideoSinkDecodedSample != null)
                        {
                            // The RawImage points at decoder owned memory that is re-used for the next frame, so the
                            // byte[] event needs a copy. GetBuffer keeps the decoder's row layout, which means the
                            // stride passed alongside it is the RawImage's own stride.
                            var sample = imageRawSample.GetBuffer();

                            if (sample != null)
                            {
                                OnVideoSinkDecodedSample(sample, (uint)imageRawSample.Width, (uint)imageRawSample.Height, imageRawSample.Stride, imageRawSample.PixelFormat);
                            }
                        }
                    }
                }
            }
        }
    }

    public Task PauseVideo()
    {
        _isPaused = true;
        return Task.CompletedTask;
    }

    public Task ResumeVideo()
    {
        _isPaused = false;
        return Task.CompletedTask;
    }

    public Task StartVideo()
    {
        if (!_isStarted)
        {
            _isStarted = true;
        }

        return Task.CompletedTask;
    }

    public Task CloseVideo()
    {
        if (!_isClosed)
        {
            _isClosed = true;
            _ffmpegEncoder.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Encodes a raw video frame supplied by the application and raises <see cref="OnVideoSourceEncodedSample"/>
    /// with the result, ready for the RTP transport.
    /// </summary>
    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        if (!_isClosed && !_isPaused && OnVideoSourceEncodedSample != null)
        {
            byte[]? encodedBuffer;

            try
            {
                encodedBuffer = _ffmpegEncoder.EncodeVideo(width, height, sample, pixelFormat, _videoFormatManager.SelectedFormat.Codec);
            }
            catch (Exception excp)
            {
                RaiseEncodeError(excp);
                return;
            }

            RaiseEncodedSample(durationMilliseconds, encodedBuffer);
        }
    }

    /// <summary>
    /// Encodes a raw video frame supplied by the application (zero-copy RawImage variant) and raises
    /// <see cref="OnVideoSourceEncodedSample"/> with the result.
    /// </summary>
    public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
    {
        if (!_isClosed && !_isPaused && OnVideoSourceEncodedSample != null)
        {
            byte[]? encodedBuffer;

            try
            {
                encodedBuffer = _ffmpegEncoder.EncodeVideoFaster(rawImage, _videoFormatManager.SelectedFormat.Codec);
            }
            catch (Exception excp)
            {
                RaiseEncodeError(excp);
                return;
            }

            RaiseEncodedSample(durationMilliseconds, encodedBuffer);
        }
    }

    /// <summary>
    /// Reports an encode failure to <see cref="OnVideoSourceError"/> rather than throwing it back into the
    /// caller that pushed the frame, which is typically a capture or timer callback. If nothing is subscribed
    /// the exception is rethrown so the failure is not silently lost.
    /// </summary>
    private void RaiseEncodeError(Exception excp)
    {
        logger.LogWarning(excp, "Exception encoding video sample. {ErrorMessage}", excp.Message);

        var onError = OnVideoSourceError;

        if (onError == null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(excp).Throw();
        }

        onError!.Invoke($"FFmpeg video encode failed. {excp.Message}");
    }

    private void RaiseEncodedSample(uint durationMilliseconds, byte[]? encodedBuffer)
    {
        if (encodedBuffer != null)
        {
            uint clockRate = (uint)_videoFormatManager.SelectedFormat.ClockRate;

            // Scale the duration straight to RTP clock units rather than going via an integer frame rate, which
            // truncates twice, e.g. a 17ms frame would come out as 1551 ticks at 90kHz instead of 1530.
            uint durationRtpTS = (durationMilliseconds > 0) ?
                (uint)((ulong)clockRate * durationMilliseconds / 1000) :
                clockRate / (uint)Helper.DEFAULT_VIDEO_FRAME_RATE;

            // Note the event handler can be removed while the encoding is in progress.
            OnVideoSourceEncodedSample?.Invoke(durationRtpTS, encodedBuffer);
        }
    }

    public void Dispose()
    {
        // Mark both directions closed so no further encode or decode calls reach the disposed encoder.
        _isSinkClosed = true;
        CloseVideo();
    }

    public Task PauseVideoSink()
    {
        _isSinkPaused = true;
        return Task.CompletedTask;
    }

    public Task ResumeVideoSink()
    {
        _isSinkPaused = false;
        return Task.CompletedTask;
    }

    public Task StartVideoSink() => Task.CompletedTask;

    /// <summary>
    /// Stops decoding incoming frames. The encoder is shared with the source side so it is only disposed
    /// when the source is closed.
    /// </summary>
    public Task CloseVideoSink()
    {
        _isSinkClosed = true;
        return Task.CompletedTask;
    }

    public Task Start() => Task.WhenAll(StartVideo(), StartVideoSink());

    public Task Close() => Task.WhenAll(CloseVideoSink(), CloseVideo());

    public Task Pause() => Task.WhenAll(PauseVideo(), PauseVideoSink());

    public Task Resume() => Task.WhenAll(ResumeVideo(), ResumeVideoSink());
}
