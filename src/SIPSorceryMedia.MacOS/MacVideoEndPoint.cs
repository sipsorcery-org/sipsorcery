//-----------------------------------------------------------------------------
// Filename: MacVideoEndPoint.cs
//
// Description: RTP video endpoint that uses AVFoundation for camera capture
// and rendering on macOS.
//
// This implementation is intentionally close to WindowsVideoEndPoint:
// - AVCaptureSession + AVCaptureVideoDataOutput replace MediaCapture.
// - Camera frames are captured in BGRA pixel format.
// - Raw frames are forwarded via OnVideoSourceRawSample.
// - Encoding is delegated to the IVideoEncoder supplied at construction time
//   and the result is forwarded via OnVideoSourceEncodedSample.
// - Received encoded frames are decoded by IVideoEncoder and forwarded via
//   OnVideoSinkDecodedSample.
//
// The containing application must provide an NSCameraUsageDescription entry
// in its Info.plist before camera access is granted by macOS.
//
// License:
// BSD 3-Clause "New" or "Revised" License and the additional
// BDS BY-NC-SA restriction, see included LICENSE.md file.
//-----------------------------------------------------------------------------

#nullable disable

using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AVFoundation;
using CoreFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.MacOS
{
    /// <summary>
    /// Video endpoint for the default macOS camera and remote video stream.
    /// </summary>
    /// <remarks>
    /// The containing application must provide an NSCameraUsageDescription
    /// entry in its Info.plist before camera capture is enabled.
    /// </remarks>
    public class MacVideoEndPoint : IVideoEndPoint, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;

        /// <summary>Pixel format requested from the camera (BGRA, 4 bytes per pixel).</summary>
        private const VideoPixelFormatsEnum CAPTURE_PIXEL_FORMAT = VideoPixelFormatsEnum.Bgra;

        private static ILogger logger =
            SIPSorcery.LogFactory.CreateLogger<MacVideoEndPoint>();

        private readonly IVideoEncoder _videoEncoder;
        private readonly MediaFormatManager<VideoFormat> _videoFormatManager;
        private readonly bool _disableSource;
        private readonly bool _disableSink;
        private readonly uint _fps;
        private readonly object _encoderLock = new object();

        private AVCaptureSession _captureSession;
        private AVCaptureDeviceInput _deviceInput;
        private FrameDelegate _frameDelegate;

        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _disposed;
        private bool _forceKeyFrame;
        private DateTime _lastFrameAt = DateTime.MinValue;

        /// <summary>
        /// Raised whenever an encoded video frame is ready for RTP transport.
        /// </summary>
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;

        /// <summary>
        /// Raised for each raw (unencoded) BGRA frame captured from the camera.
        /// Subscribe to this event to display local video preview.
        /// </summary>
        public event RawVideoSampleDelegate OnVideoSourceRawSample;

#pragma warning disable 0067
        /// <inheritdoc />
        public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster;

        /// <inheritdoc />
        public event VideoSinkSampleDecodedFasterDelegate OnVideoSinkDecodedSampleFaster;
#pragma warning restore 0067

        /// <summary>
        /// Raised after a received encoded video frame has been decoded.
        /// Subscribe to this event to display the remote party's video.
        /// </summary>
        public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

        /// <summary>
        /// Raised if there is an error acquiring or running the camera.
        /// </summary>
        public event SourceErrorDelegate OnVideoSourceError;

        /// <summary>
        /// Creates a macOS video endpoint backed by the default camera.
        /// </summary>
        /// <param name="videoEncoder">Encoder used to encode captured frames and decode received frames.</param>
        /// <param name="disableSource">When true, camera capture is disabled (receive-only mode).</param>
        /// <param name="disableSink">When true, decoding of received frames is disabled (send-only mode).</param>
        /// <param name="fps">Target capture frame rate. Defaults to 30.</param>
        public MacVideoEndPoint(
            IVideoEncoder videoEncoder,
            bool disableSource = false,
            bool disableSink = false,
            uint fps = DEFAULT_FRAMES_PER_SECOND)
        {
            if (videoEncoder == null)
            {
                throw new ArgumentNullException(nameof(videoEncoder));
            }

            _videoEncoder = videoEncoder;
            _disableSource = disableSource;
            _disableSink = disableSink;
            _fps = fps > 0 ? fps : DEFAULT_FRAMES_PER_SECOND;
            _videoFormatManager = new MediaFormatManager<VideoFormat>(videoEncoder.SupportedFormats);
        }

        /// <inheritdoc />
        public List<VideoFormat> GetVideoSourceFormats() => _videoFormatManager.GetSourceFormats();

        /// <inheritdoc />
        public void SetVideoSourceFormat(VideoFormat videoFormat) => _videoFormatManager.SetSelectedFormat(videoFormat);

        /// <inheritdoc />
        public List<VideoFormat> GetVideoSinkFormats() => _videoFormatManager.GetSourceFormats();

        /// <inheritdoc />
        public void SetVideoSinkFormat(VideoFormat videoFormat) => _videoFormatManager.SetSelectedFormat(videoFormat);

        /// <inheritdoc />
        public void RestrictFormats(Func<VideoFormat, bool> filter) => _videoFormatManager.RestrictFormats(filter);

        /// <inheritdoc />
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;

        /// <inheritdoc />
        public bool IsVideoSourcePaused() => _isPaused;

        /// <inheritdoc />
        public void ForceKeyFrame() => _forceKeyFrame = true;

        /// <inheritdoc />
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            throw new ApplicationException("The macOS Video End Point does not support external samples. Use the video end point from SIPSorceryMedia.Encoders.");

        /// <inheritdoc />
        public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage) =>
            throw new ApplicationException("The macOS Video End Point does not support external samples. Use the video end point from SIPSorceryMedia.Encoders.");

        /// <inheritdoc />
        public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload) =>
            throw new ApplicationException("The macOS Video End Point requires full video frames rather than individual RTP packets.");

        /// <inheritdoc />
        public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] frame, VideoFormat format)
        {
            if (_isClosed || _disableSink)
            {
                return;
            }

            var decodedFrames = _videoEncoder.DecodeVideo(frame, VideoPixelFormatsEnum.Bgr, _videoFormatManager.SelectedFormat.Codec);

            if (decodedFrames == null)
            {
                logger.LogWarning("Video decode of received frame failed.");
            }
            else
            {
                foreach (var decodedFrame in decodedFrames)
                {
                    OnVideoSinkDecodedSample?.Invoke(
                        decodedFrame.Sample,
                        decodedFrame.Width,
                        decodedFrame.Height,
                        (int)(decodedFrame.Width * 3),
                        VideoPixelFormatsEnum.Bgr);
                }
            }
        }

        /// <inheritdoc />
        public Task StartVideo()
        {
            if (_isStarted || _disableSource)
            {
                return Task.CompletedTask;
            }

            _isStarted = true;

            if (InitialiseCaptureSession())
            {
                _captureSession.StartRunning();
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PauseVideo()
        {
            _isPaused = true;
            _captureSession?.StopRunning();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task ResumeVideo()
        {
            _isPaused = false;

            if (_isStarted && !_isClosed)
            {
                _captureSession?.StartRunning();
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                _captureSession?.StopRunning();
                Dispose();
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PauseVideoSink() => Task.CompletedTask;

        /// <inheritdoc />
        public Task ResumeVideoSink() => Task.CompletedTask;

        /// <inheritdoc />
        public Task StartVideoSink() => Task.CompletedTask;

        /// <inheritdoc />
        public Task CloseVideoSink() => Task.CompletedTask;

        /// <inheritdoc />
        public Task Start() => StartVideo();

        /// <inheritdoc />
        public Task Close() => CloseVideo();

        /// <inheritdoc />
        public Task Pause() => PauseVideo();

        /// <inheritdoc />
        public Task Resume() => ResumeVideo();

        /// <summary>
        /// Returns the display names of all available video capture devices on this system.
        /// </summary>
        public static IReadOnlyList<string> GetVideoCaptureDevices()
        {
            var names = new List<string>();

#pragma warning disable CA1422
            var devices = AVCaptureDevice.DevicesWithMediaType("vide");
#pragma warning restore CA1422

            if (devices != null)
            {
                foreach (var device in devices)
                {
                    names.Add(device.LocalizedName);
                }
            }

            return names;
        }

        /// <summary>
        /// Configures and connects the AVCaptureSession for the default camera.
        /// </summary>
        /// <returns>True if the session was successfully initialised.</returns>
        private bool InitialiseCaptureSession()
        {
            var device = AVCaptureDevice.GetDefaultDevice(NSString.Empty);

            if (device == null)
            {
                logger.LogWarning("No video capture device found on this macOS system.");
                OnVideoSourceError?.Invoke("No video capture device found.");
                return false;
            }

            _deviceInput = new AVCaptureDeviceInput(device, out NSError inputError);

            if (inputError != null)
            {
                logger.LogWarning("Failed to create video capture device input: {Error}", inputError.LocalizedDescription);
                OnVideoSourceError?.Invoke(inputError.LocalizedDescription);
                return false;
            }

            _captureSession = new AVCaptureSession
            {
                SessionPreset = AVCaptureSession.PresetMedium
            };

            if (!_captureSession.CanAddInput(_deviceInput))
            {
                logger.LogWarning("Cannot add video capture device input to AVCaptureSession.");
                OnVideoSourceError?.Invoke("Cannot add video capture device input.");
                return false;
            }

            _captureSession.AddInput(_deviceInput);

            var videoOutput = new AVCaptureVideoDataOutput
            {
                AlwaysDiscardsLateVideoFrames = true,
                WeakVideoSettings = NSDictionary.FromObjectAndKey(
                    NSNumber.FromInt32((int)CVPixelFormatType.CV32BGRA),
                    CVPixelBuffer.PixelFormatTypeKey)
            };

            _frameDelegate = new FrameDelegate(OnCameraFrame);
            videoOutput.SetSampleBufferDelegate(_frameDelegate, DispatchQueue.DefaultGlobalQueue);

            if (!_captureSession.CanAddOutput(videoOutput))
            {
                logger.LogWarning("Cannot add video output to AVCaptureSession.");
                OnVideoSourceError?.Invoke("Cannot add video output.");
                return false;
            }

            _captureSession.AddOutput(videoOutput);

            ConfigureFrameRate(device, _fps);

            logger.LogInformation("MacVideoEndPoint initialised with device: {DeviceName}.", device.LocalizedName);

            return true;
        }

        /// <summary>
        /// Called on each BGRA frame produced by the camera.
        /// Fires the raw sample and encoded sample events.
        /// </summary>
        private void OnCameraFrame(byte[] bgraBuffer, int width, int height)
        {
            if (_isClosed || _isPaused)
            {
                return;
            }

            uint frameSpacing = 0;

            if (_lastFrameAt != DateTime.MinValue)
            {
                frameSpacing = (uint)Math.Max(0, DateTime.Now.Subtract(_lastFrameAt).TotalMilliseconds);
            }

            _lastFrameAt = DateTime.Now;

            OnVideoSourceRawSample?.Invoke(frameSpacing, width, height, bgraBuffer, CAPTURE_PIXEL_FORMAT);

            if (OnVideoSourceEncodedSample != null && !_videoFormatManager.SelectedFormat.IsEmpty())
            {
                lock (_encoderLock)
                {
                    var encoded = _videoEncoder.EncodeVideo(
                        width,
                        height,
                        bgraBuffer,
                        CAPTURE_PIXEL_FORMAT,
                        _videoFormatManager.SelectedFormat.Codec);

                    if (encoded != null)
                    {
                        uint durationRtpTS = VIDEO_SAMPLING_RATE / _fps;
                        OnVideoSourceEncodedSample.Invoke(durationRtpTS, encoded);
                    }

                    if (_forceKeyFrame)
                    {
                        _videoEncoder.ForceKeyFrame();
                        _forceKeyFrame = false;
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to lock the capture device and set the active frame rate.
        /// Silently ignored if the requested rate is not supported.
        /// </summary>
        private static void ConfigureFrameRate(AVCaptureDevice device, uint targetFps)
        {
            if (!device.LockForConfiguration(out NSError lockError))
            {
                return;
            }

            try
            {
                var targetDuration = new CMTime(1, (int)targetFps);

                foreach (var format in device.Formats)
                {
                    foreach (var range in format.VideoSupportedFrameRateRanges)
                    {
                        if (range.MinFrameRate <= targetFps && targetFps <= range.MaxFrameRate)
                        {
                            device.ActiveVideoMinFrameDuration = targetDuration;
                            device.ActiveVideoMaxFrameDuration = targetDuration;
                            return;
                        }
                    }
                }
            }
            finally
            {
                device.UnlockForConfiguration();
            }
        }

        /// <summary>
        /// Releases the capture session and encoder resources.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _captureSession?.Dispose();
            _captureSession = null;

            _deviceInput?.Dispose();
            _deviceInput = null;

            _frameDelegate?.Dispose();
            _frameDelegate = null;

            lock (_encoderLock)
            {
                _videoEncoder?.Dispose();
            }

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// AVFoundation delegate that receives raw camera sample buffers and
        /// extracts the BGRA pixel data for further processing.
        /// </summary>
        private sealed class FrameDelegate : AVCaptureVideoDataOutputSampleBufferDelegate
        {
            private readonly Action<byte[], int, int> _onFrame;

            public FrameDelegate(Action<byte[], int, int> onFrame)
            {
                _onFrame = onFrame;
            }

            public override void DidOutputSampleBuffer(
                AVCaptureOutput captureOutput,
                CMSampleBuffer sampleBuffer,
                AVCaptureConnection connection)
            {
                using (sampleBuffer)
                {
                    if (!(sampleBuffer.GetImageBuffer() is CVPixelBuffer pixelBuffer))
                    {
                        return;
                    }

                    pixelBuffer.Lock(CVPixelBufferLock.None);

                    try
                    {
                        int width = (int)pixelBuffer.Width;
                        int height = (int)pixelBuffer.Height;
                        int bytesPerRow = (int)pixelBuffer.BytesPerRow;
                        IntPtr baseAddress = pixelBuffer.BaseAddress;

                        int bufferSize = height * bytesPerRow;
                        byte[] rawBuffer = new byte[bufferSize];
                        Marshal.Copy(baseAddress, rawBuffer, 0, bufferSize);

                        // Strip row padding when stride > width * 4 (BGRA).
                        int expectedStride = width * 4;
                        byte[] bgraBuffer;

                        if (bytesPerRow == expectedStride)
                        {
                            bgraBuffer = rawBuffer;
                        }
                        else
                        {
                            bgraBuffer = new byte[height * expectedStride];

                            for (int row = 0; row < height; row++)
                            {
                                Array.Copy(
                                    rawBuffer, row * bytesPerRow,
                                    bgraBuffer, row * expectedStride,
                                    expectedStride);
                            }
                        }

                        _onFrame(bgraBuffer, width, height);
                    }
                    finally
                    {
                        pixelBuffer.Unlock(CVPixelBufferLock.None);
                    }
                }
            }
        }
    }
}
