//-----------------------------------------------------------------------------
// Filename: VideoBitmapSource.cs
//
// Description: Implements a video source from a Bitmap.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 17 Feb 2025	Aaron Clauson	Created based on VideoTestPatternSource.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Media;

public class VideoBitmapSource : IVideoSource, IDisposable
{
    private const int VIDEO_SAMPLING_RATE = 90000;
    private const int MAXIMUM_FRAMES_PER_SECOND = 60; // Note the Threading.Timer's maximum callback rate is approx 60/s so allowing higher has no effect.
    private const int DEFAULT_FRAMES_PER_SECOND = 30;
    private const int MINIMUM_FRAMES_PER_SECOND = 1;
    private const int TIMER_DISPOSE_WAIT_MILLISECONDS = 1000;
    private const int VP8_SUGGESTED_FORMAT_ID = 96;
    private const int H264_SUGGESTED_FORMAT_ID = 100;

    public static ILogger logger = NullLogger.Instance;

    public static readonly List<VideoFormat> SupportedFormats = new List<VideoFormat>
    {
        new VideoFormat(VideoCodecsEnum.VP8, VP8_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE),
        new VideoFormat(VideoCodecsEnum.H264, H264_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE, "packetization-mode=1")
    };

    private int _frameSpacing;
    private Timer _sendTimer;
    private bool _isStarted;
    private bool _isPaused;
    private bool _isClosed;
    private int _frameCount;
    private IVideoEncoder _videoEncoder;
    private MediaFormatManager<VideoFormat> _formatManager;

    private int _bitmapWidth;
    private int _bitmapHeight;
    private byte[] _i420Buffer = [];
    private byte[] _bgrBuffer = [];

    /// <summary>
    /// Unencoded test pattern samples.
    /// </summary>
    public event RawVideoSampleDelegate OnVideoSourceRawSample = delegate { };

#pragma warning disable CS0067
    public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster = delegate { };
#pragma warning restore CS0067

    /// <summary>
    /// If a video encoder has been set then this event contains the encoded video
    /// samples.
    /// </summary>
    public event EncodedSampleDelegate OnVideoSourceEncodedSample = delegate { };

    public event SourceErrorDelegate OnVideoSourceError = delegate { };

    public bool IsClosed => _isClosed;

    public VideoBitmapSource(IVideoEncoder encoder)
    {
        _videoEncoder = encoder;

        _formatManager = new MediaFormatManager<VideoFormat>(SupportedFormats);

        _sendTimer = new Timer(SendFrame, null, Timeout.Infinite, Timeout.Infinite);
        _frameSpacing = 1000 / DEFAULT_FRAMES_PER_SECOND;
    }

    public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
    public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
    public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
    public List<VideoFormat> GetVideoSinkFormats() => _formatManager.GetSourceFormats();
    public void SetVideoSinkFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);

    public void ForceKeyFrame() => _videoEncoder?.ForceKeyFrame();
    public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;

    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
        throw new NotImplementedException("The test pattern video source does not offer any encoding services for external sources.");

    public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) =>
        throw new NotImplementedException("The test pattern video source does not offer any encoding services for external sources.");

    public Task<bool> InitialiseVideoSourceDevice() =>
        throw new NotImplementedException("The test pattern video source does not use a device.");
    public bool IsVideoSourcePaused() => _isPaused;

    public void SetFrameRate(int framesPerSecond)
    {
        if (framesPerSecond < MINIMUM_FRAMES_PER_SECOND || framesPerSecond > MAXIMUM_FRAMES_PER_SECOND)
        {
            logger.LogWarning("{FramesPerSecond} fames per second not in the allowed range of {MinimumFramesPerSecond} to {MaximumFramesPerSecond}, ignoring.", framesPerSecond, MINIMUM_FRAMES_PER_SECOND, MAXIMUM_FRAMES_PER_SECOND);
        }
        else
        {
            _frameSpacing = 1000 / framesPerSecond;

            if (_isStarted)
            {
                _sendTimer.Change(0, _frameSpacing);
            }
        }
    }

    public Task PauseVideo()
    {
        _isPaused = true;
        _sendTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public Task ResumeVideo()
    {
        _isPaused = false;
        _sendTimer.Change(0, _frameSpacing);
        return Task.CompletedTask;
    }

    public Task StartVideo()
    {
        if (!_isStarted)
        {
            _isStarted = true;
            _sendTimer.Change(0, _frameSpacing);
        }

        return Task.CompletedTask;
    }

    public Task CloseVideo()
    {
        if (!_isClosed)
        {
            _isClosed = true;

            ManualResetEventSlim mre = new ManualResetEventSlim();
            _sendTimer?.Dispose(mre.WaitHandle);
            return Task.Run(() => mre.Wait(TIMER_DISPOSE_WAIT_MILLISECONDS));
        }
        return Task.CompletedTask;
    }

    public void SetSourceBitmap(Bitmap bitmap)
    {
        var bitmapWidth = bitmap.Width;
        var bitmapHeight = bitmap.Height;

        var i420Buffer = BitmapToI420(bitmap);
        var bgrBuffer = PixelConverter.I420toBGR(i420Buffer, bitmapWidth, bitmapHeight, out _);

        lock(_sendTimer)
        {
            _bitmapWidth = bitmapWidth;
            _bitmapHeight = bitmapHeight;
            _i420Buffer = i420Buffer;
            _bgrBuffer = bgrBuffer;
        }
    }

    private void SendFrame(object? state)
    {
        lock (_sendTimer)
        {
            if (!_isClosed && (OnVideoSourceRawSample != null || OnVideoSourceEncodedSample != null))
            {
                _frameCount++;

                if (OnVideoSourceRawSample != null && _bgrBuffer != null)
                {
                    OnVideoSourceRawSample?.Invoke((uint)_frameSpacing, _bitmapWidth, _bitmapHeight, _bgrBuffer, VideoPixelFormatsEnum.Bgr);
                }

                if (_videoEncoder != null && OnVideoSourceEncodedSample != null && _i420Buffer != null && !_formatManager.SelectedFormat.IsEmpty())
                {
                    var encodedBuffer = _videoEncoder.EncodeVideo(_bitmapWidth, _bitmapHeight, _i420Buffer, VideoPixelFormatsEnum.I420, _formatManager.SelectedFormat.Codec);

                    if (encodedBuffer != null)
                    {
                        uint fps = (_frameSpacing > 0) ? 1000 / (uint)_frameSpacing : DEFAULT_FRAMES_PER_SECOND;
                        uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;
                        OnVideoSourceEncodedSample.Invoke(durationRtpTS, encodedBuffer);
                    }
                }

                if (_frameCount == int.MaxValue)
                {
                    _frameCount = 0;
                }
            }
        }
    }

    private static byte[] BitmapToI420(Bitmap bitmap)
    {
        try
        {
            int width = bitmap.Width;
            int height = bitmap.Height;

            BitmapData bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            int rgbStride = bitmapData.Stride;
            int yStride = width;
            int uvStride = width / 2;

            int ySize = yStride * height;
            int uvSize = uvStride * (height / 2);

            byte[] i420Buffer = new byte[ySize + 2 * uvSize]; // I420 layout: Y plane, then U plane, then V plane.

            byte[] rgbBuffer = new byte[rgbStride * height];
            Marshal.Copy(bitmapData.Scan0, rgbBuffer, 0, rgbBuffer.Length);
            bitmap.UnlockBits(bitmapData);

            int yIndex = 0;
            int uIndex = ySize;         // U plane starts after Y plane
            int vIndex = uIndex + uvSize; // V plane starts after U plane

            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    int rgbIndex = j * rgbStride + (i * 3);
                    byte b = rgbBuffer[rgbIndex];
                    byte g = rgbBuffer[rgbIndex + 1];
                    byte r = rgbBuffer[rgbIndex + 2];

                    // Convert RGB to YUV (BT.601 standard)
                    byte y = (byte)((0.299 * r) + (0.587 * g) + (0.114 * b));
                    byte u = (byte)((-0.168736 * r) + (-0.331264 * g) + (0.5 * b) + 128);
                    byte v = (byte)((0.5 * r) + (-0.418688 * g) + (-0.081312 * b) + 128);

                    // Write Y plane
                    i420Buffer[yIndex++] = y;

                    // Subsample U & V (4:2:0 means one U & V sample for every 2x2 block)
                    if (j % 2 == 0 && i % 2 == 0)
                    {
                        i420Buffer[uIndex++] = u;
                        i420Buffer[vIndex++] = v;
                    }
                }
            }

            return i420Buffer;
        }
        catch (Exception)
        {
            return [];
        }
    }

    public void Dispose()
    {
        _isClosed = true;
        _sendTimer?.Dispose();
        _videoEncoder?.Dispose();
    }
}
