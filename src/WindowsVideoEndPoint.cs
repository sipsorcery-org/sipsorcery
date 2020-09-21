//-----------------------------------------------------------------------------
// Filename: WindowsVideoEndPoint.cs
//
// Description: Implements a video source and sink for Windows.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 20 Aug 2020  Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Windows.Codecs;
using SIPSorceryMedia.Abstractions;

namespace SIPSorceryMedia.Windows
{
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    { 
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    public class WindowsVideoEndPoint : IVideoSource, IVideoSink, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;

        /// <summary>
        /// NV12 is the pixel format set on the VP8 encoder. If a capture device is able to 
        /// supply native NV12 frames it will save a transform.
        /// </summary>
        private const string VIDEO_DESIRED_PIXEL_FORMAT = "NV12";

        private ILogger logger = SIPSorcery.LogFactory.CreateLogger<WindowsVideoEndPoint>();

        public static readonly List<VideoCodecsEnum> SupportedCodecs = new List<VideoCodecsEnum>
        {
            VideoCodecsEnum.VP8
        };

        private CodecManager<VideoCodecsEnum> _codecManager;
        private Vp8Codec _vp8Encoder;
        private Vp8Codec _vp8Decoder;
        private bool _forceKeyFrame = false;
        private bool _isInitialised;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private SoftwareBitmap _encodeBmp;
        private MediaCapture _mediaCapture;
        private MediaFrameReader _mediaFrameReader;
        private MediaFrameSource _mediaFrameSource;
        private bool _encodingOnly;
        private uint _width = 0;
        private uint _height = 0;
        private uint _fpsNumerator = 0;
        private uint _fpsDenominator = 1;
        private bool _videoCaptureDeviceFailed;
        
        /// <summary>
        /// This video source DOES NOT generate raw samples. Subscribe to the encoded samples event
        /// to get samples ready for passing to the RTP transport layer.
        /// </summary>
        [Obsolete("This video source only generates encoded samples. No raw video samples will be supplied to this event.")]
        public event RawVideoSampleDelegate OnVideoSourceRawSample { add { } remove { } }

        /// <summary>
        /// This event will be fired whenever a video sample is encoded and is ready to transmit to the remote party.
        /// </summary>
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;

        /// <summary>
        /// This event is fired after the sink decodes a video frame from the remote party.
        /// </summary>
        public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

        public event SourceErrorDelegate OnVideoSourceError;

        /// <summary>
        /// Attempts to create a new video source from a local video capture device.
        /// </summary>
        /// <param name="encodingOnly">Optional. If set to true this instance will NOT attempt to initialise any 
        /// capture devices. It will provide encode and decode services for external video sources.</param>
        /// <param name="width">Optional. If specified the video capture device will be requested to initialise with this frame
        /// width. If the attempt fails an exception is thrown. If not specified the device's default frame width will
        /// be used.</param>
        /// <param name="height">Optional. If specified the video capture device will be requested to initialise with this frame
        /// height. If the attempt fails an exception is thrown. If not specified the device's default frame height will
        /// be used.</param>
        /// <param name="fps">Optional. If specified the video capture device will be requested to initialise with this frame
        /// rate. If the attempt fails an exception is thrown. If not specified the device's default frame rate will
        /// be used.</param>
        public WindowsVideoEndPoint(bool encodingOnly = false, uint width = 0, uint height = 0, uint fps = 0)
        {
            _codecManager = new CodecManager<VideoCodecsEnum>(SupportedCodecs);
            _encodingOnly = encodingOnly;
            _width = width;
            _height = height;
            _fpsNumerator = fps;

            _vp8Decoder = new Vp8Codec();
            _vp8Decoder.InitialiseDecoder();

            if (!_encodingOnly)
            {
                _mediaCapture = new MediaCapture();
                _mediaCapture.Failed += VideoCaptureDevice_Failed;
            }
        }

        public void RestrictCodecs(List<VideoCodecsEnum> codecs) => _codecManager.RestrictCodecs(codecs);
        public List<VideoCodecsEnum> GetVideoSourceFormats() => _codecManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat) => _codecManager.SetSelectedCodec(videoFormat);
        public List<VideoCodecsEnum> GetVideoSinkFormats() => _codecManager.GetSourceFormats();
        public void SetVideoSinkFormat(VideoCodecsEnum videoFormat) => _codecManager.SetSelectedCodec(videoFormat);

        public void ForceKeyFrame() => _forceKeyFrame = true;
        public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload) =>
            throw new ApplicationException("The Windows Video End Point requires full video frames rather than individual RTP packets.");
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public bool IsVideoSourcePaused() => _isPaused;

        private async void VideoCaptureDevice_Failed(MediaCapture sender, MediaCaptureFailedEventArgs errorEventArgs)
        {
            if (!_videoCaptureDeviceFailed)
            {
                _videoCaptureDeviceFailed = true;

                // This means the video capture device can't be used.
                _encodingOnly = true;

                //logger.LogWarning($"Video capture device failed. {errorEventArgs.Message}.");
                OnVideoSourceError?.Invoke(errorEventArgs.Message);

                await CloseVideoCaptureDevice().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Initialises the video capture device. Ideally should be called before attempting to use the device,
        /// which happens after calling <see cref="StartVideo"/>. By initialising first any problem with the requested
        /// frame size and rate parameters can be caught.
        /// </summary>
        /// <returns>True if the local video capture device was successfully initialised. False if not.</returns>
        public Task<bool> InitialiseVideoSourceDevice()
        {
            if (!_isInitialised)
            {
                _isInitialised = true;

                if (_encodingOnly)
                {
                    return Task.FromResult(true);
                }
                else
                {
                    return InitialiseDevice(_width, _height, _fpsNumerator);
                }
            }
            else
            {
                return Task.FromResult(true);
            }
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
        {
            if (!_isClosed)
            {
                if (_vp8Encoder == null)
                {
                    _vp8Encoder = new Vp8Codec();
                    _vp8Encoder.InitialiseEncoder((uint)width, (uint)height);
                }

                if(_encodeBmp == null)
                {
                    _encodeBmp = new SoftwareBitmap(BitmapPixelFormat.Rgba8, width, height);
                }

                if (OnVideoSourceEncodedSample != null)
                {
                    //byte[] i420Buffer = PixelConverter.RGBtoI420(rgb24Sample, width, height);
                    //byte[] encodedBuffer = _vp8Encoder.Encode(i420Buffer, _forceKeyFrame);

                    SetBitmapData(sample, _encodeBmp, pixelFormat);

                    var nv12bmp = SoftwareBitmap.Convert(_encodeBmp, BitmapPixelFormat.Nv12);
                    byte[] nv12Buffer = null;

                    using (BitmapBuffer buffer = nv12bmp.LockBuffer(BitmapBufferAccessMode.Read))
                    {
                        using (var reference = buffer.CreateReference())
                        {
                            unsafe
                            {
                                byte* dataInBytes;
                                uint capacity;
                                ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacity);

                                nv12Buffer = new byte[capacity];
                                Marshal.Copy((IntPtr)dataInBytes, nv12Buffer, 0, (int)capacity);
                            }
                        }
                    }

                    byte[] encodedBuffer = _vp8Encoder.Encode(nv12Buffer, _forceKeyFrame);

                    if (encodedBuffer != null)
                    {
                        //Console.WriteLine($"encoded buffer: {encodedBuffer.HexStr()}");
                        uint fps = (durationMilliseconds > 0) ? 1000 / durationMilliseconds : DEFAULT_FRAMES_PER_SECOND;
                        uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;
                        OnVideoSourceEncodedSample.Invoke(durationRtpTS, encodedBuffer);
                    }

                    if (_forceKeyFrame)
                    {
                        _forceKeyFrame = false;
                    }
                }
            }
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                VideoSource = this,
                VideoSink = this
            };
        }

        public void GotVideoFrame(IPEndPoint remoteEndPoint, uint timestamp, byte[] frame)
        {
            if (!_isClosed)
            {
                //DateTime startTime = DateTime.Now;

                List<byte[]> decodedFrames = _vp8Decoder.Decode(frame, frame.Length, out var width, out var height);

                if (decodedFrames == null)
                {
                    logger.LogWarning("VPX decode of video sample failed.");
                }
                else
                {
                    foreach (var decodedFrame in decodedFrames)
                    {
                        byte[] rgb = PixelConverter.I420toRGB(decodedFrame, (int)width, (int)height);
                        //Console.WriteLine($"VP8 decode took {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms.");
                        OnVideoSinkDecodedSample(rgb, width, height, (int)(width * 3), VideoPixelFormatsEnum.Rgb);
                    }
                }
            }
        }

        public Task PauseVideo()
        {
            _isPaused = true;

            if (_mediaFrameReader != null)
            {
                return _mediaFrameReader.StopAsync().AsTask();
            }
            else
            {
                return Task.CompletedTask;
            }
        }

        public Task ResumeVideo()
        {
            _isPaused = false;

            if (_mediaFrameReader != null)
            {
                return _mediaFrameReader.StartAsync().AsTask();
            }
            else
            {
                return Task.CompletedTask;
            }
        }

        public async Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;

                if (!_encodingOnly)
                {
                    if (!_isInitialised)
                    {
                        await InitialiseVideoSourceDevice().ConfigureAwait(false);
                    }

                    await _mediaFrameReader.StartAsync().AsTask().ConfigureAwait(false);
                }
            }
        }

        public async Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                await CloseVideoCaptureDevice().ConfigureAwait(false);

                if (_vp8Encoder != null)
                {
                    lock (_vp8Encoder)
                    {
                        Dispose();
                    }
                }
                else
                {
                    Dispose();
                }
            }
        }

        private async Task CloseVideoCaptureDevice()
        {
            if (_mediaFrameReader != null)
            {
                _mediaFrameReader.FrameArrived -= FrameArrivedHandler;
                await _mediaFrameReader.StopAsync().AsTask().ConfigureAwait(false);
            }

            if (_mediaCapture != null && _mediaCapture.CameraStreamState == CameraStreamState.Streaming)
            {
                await _mediaCapture.StopRecordAsync().AsTask().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Event handler for video frames for the local video capture device.
        /// </summary>
        private async void FrameArrivedHandler(MediaFrameReader sender, MediaFrameArrivedEventArgs e)
        {
            if (!_isClosed)
            {
                using (var frame = sender.TryAcquireLatestFrame())
                {
                    if (_isClosed || frame == null) return;

                    var vmf = frame.VideoMediaFrame;
                    var videoFrame = vmf.GetVideoFrame();

                    var sbmp = await SoftwareBitmap.CreateCopyFromSurfaceAsync(videoFrame.Direct3DSurface);

                    if (sbmp == null)
                    {
                        logger.LogWarning("Failed to get bitmap from video frame reader.");
                    }
                    else
                    {
                        if (!_isClosed && OnVideoSourceEncodedSample != null)
                        {
                            lock (_vp8Encoder)
                            {
                                SoftwareBitmap nv12bmp = null;

                                // If the bitmap is not in the required pixel format for the encoder convert it.
                                if (_mediaFrameSource.CurrentFormat.Subtype != VIDEO_DESIRED_PIXEL_FORMAT)
                                {
                                    nv12bmp = SoftwareBitmap.Convert(sbmp, BitmapPixelFormat.Nv12);
                                }

                                byte[] nv12Buffer = null;
                                SoftwareBitmap inputBmp = nv12bmp ?? sbmp;

                                using (BitmapBuffer buffer = inputBmp.LockBuffer(BitmapBufferAccessMode.Read))
                                {
                                    using (var reference = buffer.CreateReference())
                                    {
                                        unsafe
                                        {
                                            byte* dataInBytes;
                                            uint capacity;
                                            ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacity);

                                            nv12Buffer = new byte[capacity];
                                            Marshal.Copy((IntPtr)dataInBytes, nv12Buffer, 0, (int)capacity);
                                        }
                                    }
                                }

                                byte[] encodedBuffer = null;

                                encodedBuffer = _vp8Encoder.Encode(nv12Buffer, _forceKeyFrame);

                                if (encodedBuffer != null)
                                {
                                    uint fps = (_fpsDenominator > 0 && _fpsNumerator > 0) ? _fpsNumerator / _fpsDenominator : DEFAULT_FRAMES_PER_SECOND;
                                    uint durationRtpTS = VIDEO_SAMPLING_RATE / fps;
                                    OnVideoSourceEncodedSample.Invoke(durationRtpTS, encodedBuffer);
                                }

                                if (_forceKeyFrame)
                                {
                                    _forceKeyFrame = false;
                                }

                                nv12bmp?.Dispose();
                            }
                        }

                        sbmp.Dispose();
                        videoFrame.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Attempts to initialise the local video capture device.
        /// </summary>
        /// <param name="width">The frame width to attempt to initialise the video capture device with. Set as 0 to use default.</param>
        /// <param name="height">The frame height to attempt to initialise the video capture device with. Set as 0 to use default.</param>
        /// <param name="fps">The frame rate, in frames per second, to attempt to initialise the video capture device with. 
        /// Set as 0 to use default.</param>
        private async Task<bool> InitialiseDevice(uint width, uint height, uint fps)
        {
            if (width == 0 && height == 0 && fps == 0)
            {
                // If no specific width, height or frame rate was requested then use the device's current settings.
                // In shared mode it's not possible to adjust the source format so if the frame is the wrong pixel
                // format it will need to be transformed on a frame by frame basis.
                var mediaCaptureSettings = new MediaCaptureInitializationSettings()
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly
                };

                //await _mediaCapture.InitializeAsync(mediaCaptureSettings).AsTask().ConfigureAwait(false);
                await _mediaCapture.InitializeAsync(mediaCaptureSettings);

                var mediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                    source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;

                //_mediaFrameReader = await _mediaCapture.CreateFrameReaderAsync(mediaFrameSource).AsTask().ConfigureAwait(false);
                _mediaFrameReader = await _mediaCapture.CreateFrameReaderAsync(mediaFrameSource);

                _mediaFrameSource = mediaFrameSource;
            }
            else
            {
                // If specific capture settings have been requested then the device needs to be initialised in
                // exclusive mode as the current settings and format will most likely be changed.
                var mediaCaptureSettings = new MediaCaptureInitializationSettings()
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    SharingMode = MediaCaptureSharingMode.ExclusiveControl
                };
                await _mediaCapture.InitializeAsync(mediaCaptureSettings).AsTask().ConfigureAwait(false);

                var mediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                    source.Value.Info.SourceKind == MediaFrameSourceKind.Color &&
                    source.Value.SupportedFormats.Any(x => 
                        x.Subtype == VIDEO_DESIRED_PIXEL_FORMAT && 
                        (_width == 0 || x.VideoFormat.Width == _width) && 
                        (_height == 0 || x.VideoFormat.Height == _height) &&
                        (_fpsNumerator == 0 || x.FrameRate.Numerator == _fpsNumerator))).Value;

                if (mediaFrameSource == null)
                {
                    // Fallback to accepting any pixel format and use a software transform on each frame.
                    mediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                    source.Value.Info.SourceKind == MediaFrameSourceKind.Color &&
                    source.Value.SupportedFormats.Any(x => 
                        (_width == 0 || x.VideoFormat.Width == _width) && 
                        (_height == 0 || x.VideoFormat.Height == _height) &&
                        (_fpsNumerator == 0 || x.FrameRate.Numerator == _fpsNumerator))).Value;
                }

                if (mediaFrameSource == null)
                {
                    throw new ApplicationException("The video capture device does not support a compatible video format for the requested parameters.");
                }

                _mediaFrameReader = await _mediaCapture.CreateFrameReaderAsync(mediaFrameSource).AsTask().ConfigureAwait(false);

                // If there's a format that matches the desired pixel format set that.
                var idealFormat = mediaFrameSource.SupportedFormats.FirstOrDefault(x =>
                    x.Subtype == VIDEO_DESIRED_PIXEL_FORMAT &&
                    (_width == 0 || x.VideoFormat.Width == _width) &&
                    (_height == 0 || x.VideoFormat.Height == _height) &&
                    (_fpsNumerator == 0 || x.FrameRate.Numerator == _fpsNumerator));

                if (idealFormat != null)
                {
                    await mediaFrameSource.SetFormatAsync(idealFormat).AsTask().ConfigureAwait(false);
                }

                _mediaFrameSource = mediaFrameSource;
            }

            // Frame source and format have now been successfully set.
            _width = _mediaFrameSource.CurrentFormat.VideoFormat.Width;
            _height = _mediaFrameSource.CurrentFormat.VideoFormat.Height;
            _fpsNumerator = _mediaFrameSource.CurrentFormat.FrameRate.Numerator;
            _fpsDenominator = _mediaFrameSource.CurrentFormat.FrameRate.Denominator;

            _vp8Encoder = new Vp8Codec();
            _vp8Encoder.InitialiseEncoder(_width, _height);

            _mediaFrameReader.FrameArrived += FrameArrivedHandler;
            _mediaFrameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

            return true;
        }

        /// <summary>
        /// Copies data from an RGB buffer to a software bitmap.
        /// </summary>
        /// <param name="rgb24Buffer">The RGB buffer to copy from.</param>
        /// <param name="sbmp">The software bitmap to copy the data to.</param>
        private void SetBitmapData(byte[] buffer, SoftwareBitmap sbmp, VideoPixelFormatsEnum pixelFormat)
        {
            using (BitmapBuffer bmpBuffer = sbmp.LockBuffer(BitmapBufferAccessMode.Write))
            {
                using (var reference = bmpBuffer.CreateReference())
                {
                    unsafe
                    {
                        byte* dataInBytes;
                        uint capacity;
                        ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacity);
                        int posn = 0;

                        // Fill-in the RGBA plane
                        BitmapPlaneDescription bufferLayout = bmpBuffer.GetPlaneDescription(0);
                        for (int i = 0; i < bufferLayout.Height; i++)
                        {
                            for (int j = 0; j < bufferLayout.Width; j++)
                            {
                                // NOTE: Same as for System.Drawing.Bitmap pixel formats that have "rgb" in their name, such as
                                // BitmapPixelFormat.Rgba8, use a buffer format of BGR. Many issues on StackOverflow regarding this,
                                // e.g. https://stackoverflow.com/questions/5106505/converting-gdi-pixelformat-to-wpf-pixelformat.
                                // Notice the switch of the Blue and Red pixels below.
                                if (pixelFormat == VideoPixelFormatsEnum.Rgb)
                                {
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 0] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 1] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 2] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 3] = (byte)255;
                                }
                                else if(pixelFormat == VideoPixelFormatsEnum.Bgr)
                                {
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 2] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 1] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 0] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 3] = (byte)255;
                                }
                                //if (pixelFormat == VideoPixelFormatsEnum.Rgba)
                                //{
                                //    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 0] = buffer[posn++];
                                //    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 1] = buffer[posn++];
                                //    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 2] = buffer[posn++];
                                //    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 3] = buffer[posn++];
                                //}
                                else if (pixelFormat == VideoPixelFormatsEnum.Bgra)
                                {
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 2] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 1] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 0] = buffer[posn++];
                                    dataInBytes[bufferLayout.StartIndex + bufferLayout.Stride * i + 4 * j + 3] = buffer[posn++];
                                }
                            }
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            _vp8Encoder?.Dispose();
            _vp8Decoder?.Dispose();
            _encodeBmp?.Dispose();
        }
    }
}
