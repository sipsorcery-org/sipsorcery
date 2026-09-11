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

public class FFmpegVideoEndPoint : IVideoSource, IVideoSink, IDisposable
{
    public static ILogger logger = SIPSorcery.LogFactory.CreateLogger<FFmpegVideoEndPoint>();

    public static readonly List<VideoFormat> _supportedFormats = Helper.GetSupportedVideoFormats();

    private FFmpegVideoEncoder _ffmpegEncoder;

    private MediaFormatManager<VideoFormat> _videoFormatManager;
    private bool _isStarted;
    private bool _isPaused;
    private bool _isClosed;

    // ---- Sink (decode) events ----

#pragma warning disable CS0067
    // Decoded frames are delivered via the faster RawImage event below. The byte[] variant is part of
    // the IVideoSink contract but is not currently raised by this endpoint.
    public event VideoSinkSampleDecodedDelegate? OnVideoSinkDecodedSample;
#pragma warning restore CS0067

    public event VideoSinkSampleDecodedFasterDelegate? OnVideoSinkDecodedSampleFaster;

    // ---- Source (encode) events ----

    /// <summary>
    /// Fired when a raw sample supplied via <see cref="ExternalVideoSourceRawSample"/> or
    /// <see cref="ExternalVideoSourceRawSampleFaster"/> has been encoded and is ready to transmit.
    /// </summary>
    public event EncodedSampleDelegate? OnVideoSourceEncodedSample;

#pragma warning disable CS0067
    // This endpoint only produces ENCODED video samples (it encodes raw input supplied via the
    // ExternalVideoSourceRawSample* methods). It never emits raw source samples or source errors.
    public event RawVideoSampleDelegate? OnVideoSourceRawSample;
    public event RawVideoSampleFasterDelegate? OnVideoSourceRawSampleFaster;
    public event SourceErrorDelegate? OnVideoSourceError;
#pragma warning restore CS0067

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

    public void SetDecoderWrapper(string wrapperName)
    {
        if (_ffmpegEncoder != null)
        {
            _ffmpegEncoder.SetCodec(wrapperName);
        }
        else
        {
            logger.LogError("Video Decoder is not yet initialized.");
            throw new InvalidOperationException("Video Decoder is not yet initialized.");
        }
    }

    public bool SetDecoderForCodec(VideoCodecsEnum codec, string name, Dictionary<string, string>? opts = null)
    {
        if (_ffmpegEncoder != null)
        {
            if (FFmpegConvert.GetAVCodecID(codec) is var cdc && cdc is not null)
                return _ffmpegEncoder.SetCodec((AVCodecID)cdc, name, opts);
            else
            {
                logger.LogError("Codec {codec} is not supported by this endpoint.", codec);
                throw new InvalidOperationException($"Codec {codec} is not supported by this endpoint.");
            }
        }
        else
        {
            logger.LogError("Video Encoder is not yet initialized.");
            throw new InvalidOperationException("Video Decoder is not yet initialized.");
        }
    }

    public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] payload, VideoFormat format)
    {
        if ( (!_isClosed) && (payload != null) && (OnVideoSinkDecodedSampleFaster != null) )
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
            _ffmpegEncoder?.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Encodes a raw video frame supplied by the application and raises <see cref="OnVideoSourceEncodedSample"/>
    /// with the result, ready for the RTP transport.
    /// </summary>
    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        if (!_isClosed && OnVideoSourceEncodedSample != null)
        {
            var encodedBuffer = _ffmpegEncoder.EncodeVideo(width, height, sample, pixelFormat, _videoFormatManager.SelectedFormat.Codec);
            RaiseEncodedSample(durationMilliseconds, encodedBuffer);
        }
    }

    /// <summary>
    /// Encodes a raw video frame supplied by the application (zero-copy RawImage variant) and raises
    /// <see cref="OnVideoSourceEncodedSample"/> with the result.
    /// </summary>
    public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
    {
        if (!_isClosed && OnVideoSourceEncodedSample != null)
        {
            var encodedBuffer = _ffmpegEncoder.EncodeVideoFaster(rawImage, _videoFormatManager.SelectedFormat.Codec);
            RaiseEncodedSample(durationMilliseconds, encodedBuffer);
        }
    }

    private void RaiseEncodedSample(uint durationMilliseconds, byte[]? encodedBuffer)
    {
        if (encodedBuffer != null)
        {
            uint fps = (durationMilliseconds > 0) ? 1000 / durationMilliseconds : (uint)Helper.DEFAULT_VIDEO_FRAME_RATE;
            if (fps == 0)
            {
                fps = 1;
            }

            uint durationRtpTS = (uint)_videoFormatManager.SelectedFormat.ClockRate / fps;

            // Note the event handler can be removed while the encoding is in progress.
            OnVideoSourceEncodedSample?.Invoke(durationRtpTS, encodedBuffer);
        }
    }

    public void Dispose()
    {
        _ffmpegEncoder?.Dispose();
    }

    public Task PauseVideoSink()
    {
        return Task.CompletedTask;
    }

    public Task ResumeVideoSink()
    {
        return Task.CompletedTask;
    }

    public Task StartVideoSink()
    {
        return Task.CompletedTask;
    }

    public Task CloseVideoSink()
    {
        return Task.CompletedTask;
    }
}
