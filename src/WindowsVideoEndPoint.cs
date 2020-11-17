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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Encoders.Codecs;
using SIPSorceryMedia.Abstractions;
using Windows.Devices.Enumeration;
using Windows.Media.MediaProperties;

namespace SIPSorceryMedia.Windows
{
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    public struct VideoCaptureDeviceInfo
    {
        public string ID;
        public string Name;
    }

    public class WindowsVideoEndPoint : IVideoSource, IVideoSink, IDisposable
    {
        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;
        private const int VP8_FORMATID = 96;
        private readonly string MF_NV12_PIXEL_FORMAT = MediaEncodingSubtypes.Nv12;
        private const string MF_I420_PIXEL_FORMAT = "{30323449-0000-0010-8000-00AA00389B71}";

        // NV12 seems to be what the Software Bitmaps provided from MF tend to prefer.
        private readonly vpxmd.VpxImgFmt EncoderInputFormat = vpxmd.VpxImgFmt.VPX_IMG_FMT_NV12;

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<WindowsVideoEndPoint>();

        public static readonly List<VideoFormat> SupportedFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_FORMATID, VIDEO_SAMPLING_RATE)
        };

        private MediaFormatManager<VideoFormat> _videoFormatManager;
        private Vp8Codec _vp8Encoder;
        private Vp8Codec _vp8Decoder;
        private bool _forceKeyFrame = false;
        private bool _isInitialised;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private MediaCapture _mediaCapture;
        private MediaFrameReader _mediaFrameReader;
        private SoftwareBitmap _backBuffer;
        private string _videoDeviceID;
        private uint _width = 0;
        private uint _height = 0;
        private uint _fpsNumerator = 0;
        private uint _fpsDenominator = 1;
        private bool _videoCaptureDeviceFailed;
        private DateTime _lastFrameAt = DateTime.MinValue;

        /// <summary>
        /// This event is fired when local video samples are available. The samples
        /// are for applications that wish to display the local video stream. The 
        /// <seealso cref="OnVideoSourceEncodedSample"/> event is fired after the sample
        /// has been encoded and is ready for transmission.
        /// </summary>
        public event RawVideoSampleDelegate OnVideoSourceRawSample;

        /// <summary>
        /// This event will be fired whenever a video sample is encoded and is ready to transmit to the remote party.
        /// </summary>
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;

        /// <summary>
        /// This event is fired after the sink decodes a video frame from the remote party.
        /// </summary>
        public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

        /// <summary>
        /// This event will be fired if there is a problem acquiring the capture device.
        /// </summary>
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
        public WindowsVideoEndPoint(string videoDeviceID = null, uint width = 0, uint height = 0, uint fps = 0)
        {
            _videoFormatManager = new MediaFormatManager<VideoFormat>(SupportedFormats);
            _videoDeviceID = videoDeviceID;
            _width = width;
            _height = height;
            _fpsNumerator = fps;

            _vp8Decoder = new Vp8Codec();
            _vp8Decoder.InitialiseDecoder();

            _mediaCapture = new MediaCapture();
            _mediaCapture.Failed += VideoCaptureDevice_Failed;
        }

        public void RestrictFormats(Func<VideoFormat, bool> filter) => _videoFormatManager.RestrictFormats(filter);
        public List<VideoFormat> GetVideoSourceFormats() => _videoFormatManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoFormat videoFormat) => _videoFormatManager.SetSelectedFormat(videoFormat);
        public List<VideoFormat> GetVideoSinkFormats() => _videoFormatManager.GetSourceFormats();
        public void SetVideoSinkFormat(VideoFormat videoFormat) => _videoFormatManager.SetSelectedFormat(videoFormat);
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
             throw new ApplicationException("The Windows Video End Point does not support external samples. Use the video end point from SIPSorceryMedia.Encoders.");

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
                return InitialiseDevice(_width, _height, _fpsNumerator);
            }
            else
            {
                return Task.FromResult(true);
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

        /// <summary>
        /// Attempts to initialise the local video capture device.
        /// </summary>
        /// <param name="width">The frame width to attempt to initialise the video capture device with. Set as 0 to use default.</param>
        /// <param name="height">The frame height to attempt to initialise the video capture device with. Set as 0 to use default.</param>
        /// <param name="fps">The frame rate, in frames per second, to attempt to initialise the video capture device with. 
        /// Set as 0 to use default.</param>
        private async Task<bool> InitialiseDevice(uint width, uint height, uint fps)
        {
            var mediaCaptureSettings = new MediaCaptureInitializationSettings()
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                SharingMode = MediaCaptureSharingMode.ExclusiveControl,
                MediaCategory = MediaCategory.Communications
            };

            if (_videoDeviceID != null)
            {
                var vidCapDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask().ConfigureAwait(false);
                var vidDevice = vidCapDevices.FirstOrDefault(x => x.Id == _videoDeviceID || x.Name == _videoDeviceID);

                if (vidDevice == null)
                {
                    logger.LogWarning($"Could not find video capture device for specified ID {_videoDeviceID}, using default device.");
                }
                else
                {
                    logger.LogInformation($"Video capture device {vidDevice.Name} selected.");
                    mediaCaptureSettings.VideoDeviceId = vidDevice.Id;
                }
            }

            await _mediaCapture.InitializeAsync(mediaCaptureSettings).AsTask().ConfigureAwait(false);

            MediaFrameSourceInfo colorSourceInfo = null;
            foreach (var srcInfo in _mediaCapture.FrameSources)
            {
                if (srcInfo.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                   srcInfo.Value.Info.SourceKind == MediaFrameSourceKind.Color)
                {
                    colorSourceInfo = srcInfo.Value.Info;
                    break;
                }
            }

            var colorFrameSource = _mediaCapture.FrameSources[colorSourceInfo.Id];

            var preferredFormat = colorFrameSource.SupportedFormats.Where(format =>
            {
                return format.VideoFormat.Width >= _width &&
                       format.VideoFormat.Width >= _height &&
                       (format.FrameRate.Numerator / format.FrameRate.Denominator) >= fps
                        && format.Subtype == MF_NV12_PIXEL_FORMAT;
            }).FirstOrDefault();

            if (preferredFormat == null)
            {
                // Try again without the pixel format.
                preferredFormat = colorFrameSource.SupportedFormats.Where(format =>
                {
                    return format.VideoFormat.Width >= _width &&
                           format.VideoFormat.Width >= _height &&
                           (format.FrameRate.Numerator / format.FrameRate.Denominator) >= fps;
                }).FirstOrDefault();
            }

            if (preferredFormat == null)
            {
                // Still can't get what we want. Log a warning message and take the default.
                logger.LogWarning($"The video capture device did not support the requested format (or better) {_width}x{_height} {fps}fps. Using default mode.");

                preferredFormat = colorFrameSource.SupportedFormats.First();
            }

            if (preferredFormat == null)
            {
                throw new ApplicationException("The video capture device does not support a compatible video format for the requested parameters.");
            }

            await colorFrameSource.SetFormatAsync(preferredFormat).AsTask().ConfigureAwait(false);

            _mediaFrameReader = await _mediaCapture.CreateFrameReaderAsync(colorFrameSource).AsTask().ConfigureAwait(false);
            _mediaFrameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

            // Frame source and format have now been successfully set.
            _width = preferredFormat.VideoFormat.Width;
            _height = preferredFormat.VideoFormat.Height;
            _fpsNumerator = preferredFormat.FrameRate.Numerator;
            _fpsDenominator = preferredFormat.FrameRate.Denominator;

            //double fpsSelected = _fpsNumerator / _fpsDenominator;
            //string pixFmt = preferredFormat.Subtype == MF_I420_PIXEL_FORMAT ? "I420" : preferredFormat.Subtype;
            //string deviceName = colorFrameSource.Info.DeviceInformation.Name;
            //logger.LogInformation($"Video capture device {deviceName} successfully initialised: {_width}x{_height} {fpsSelected:0.##}fps pixel format {pixFmt}.");

            PrintFrameSourceInfo(colorFrameSource);

            _vp8Encoder = new Vp8Codec();
            _vp8Encoder.InitialiseEncoder(_width, _height, EncoderInputFormat);

            _mediaFrameReader.FrameArrived += FrameArrivedHandler;

            return true;
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
                        // Windows bitmaps expect BGR when supplying System.Drawing.Imaging.PixelFormat.Format24bppRgb. 
                        byte[] bgr = PixelConverter.I420toBGR(decodedFrame, (int)width, (int)height);
                        //Console.WriteLine($"VP8 decode took {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms.");
                        OnVideoSinkDecodedSample(bgr, width, height, (int)(width * 3), VideoPixelFormatsEnum.Bgr);
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

                if (!_isInitialised)
                {
                    await InitialiseVideoSourceDevice().ConfigureAwait(false);
                }

                await _mediaFrameReader.StartAsync().AsTask().ConfigureAwait(false);
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

        /// <summary>
        /// Attempts to list the system video capture devices and supported  video modes.
        /// </summary>
        public static async Task<List<VideoCaptureDeviceInfo>> GetVideoCatpureDevices()
        {
            var vidCapDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

            if (vidCapDevices != null)
            {
                return vidCapDevices.Select(x => new VideoCaptureDeviceInfo { ID = x.Id, Name = x.Name }).ToList();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Attempts to list the system video capture devices and supported  video modes.
        /// </summary>
        public static async Task ListDevicesAndFormats()
        {
            var vidCapDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            foreach (var vidCapDevice in vidCapDevices)
            {
                // The block below is how the reference documentation shows how to list modes but as of Sep 2020 it does not work.
                // https://docs.microsoft.com/en-us/uwp/api/windows.media.capture.mediacapture.findallvideoprofiles?view=winrt-19041.
                //logger.LogDebug($"Supported formats for video capture device {vidCapDevice.Name}:");
                //foreach (var recordProfiles in MediaCapture.FindAllVideoProfiles(vidCapDevice.Id).Select(x => x.SupportedRecordMediaDescription))
                //{
                //    logger.LogDebug($"Support profile count {recordProfiles.Count}");
                //    foreach (var profile in recordProfiles)
                //    {
                //        logger.LogDebug($"Capture device frame source {profile.Width}x{profile.Height} {profile.FrameRate:0.##}fps {profile.Subtype}");
                //    }
                //}

                var mediaCaptureSettings = new MediaCaptureInitializationSettings()
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    SharingMode = MediaCaptureSharingMode.SharedReadOnly,
                    VideoDeviceId = vidCapDevice.Id
                };

                MediaCapture mediaCapture = new MediaCapture();
                await mediaCapture.InitializeAsync(mediaCaptureSettings);

                foreach (var srcFmtList in mediaCapture.FrameSources.Values.Select(x => x.SupportedFormats).Select(y => y.ToList()))
                {
                    foreach (var srcFmt in srcFmtList)
                    {
                        var vidFmt = srcFmt.VideoFormat;
                        float vidFps = vidFmt.MediaFrameFormat.FrameRate.Numerator / vidFmt.MediaFrameFormat.FrameRate.Denominator;
                        string pixFmt = vidFmt.MediaFrameFormat.Subtype == MF_I420_PIXEL_FORMAT ? "I420" : vidFmt.MediaFrameFormat.Subtype;
                        logger.LogDebug($"Video Capture device {vidCapDevice.Name} format {vidFmt.Width}x{vidFmt.Height} {vidFps:0.##}fps {pixFmt}");
                    }
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
                if (!_isClosed && (OnVideoSourceEncodedSample != null || OnVideoSourceRawSample != null))
                {
                    using (var mediaFrameReference = sender.TryAcquireLatestFrame())
                    {
                        var videoMediaFrame = mediaFrameReference?.VideoMediaFrame;
                        var softwareBitmap = videoMediaFrame?.SoftwareBitmap;

                        if (softwareBitmap == null && videoMediaFrame != null)
                        {
                            var videoFrame = videoMediaFrame.GetVideoFrame();
                            softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(videoFrame.Direct3DSurface);
                        }

                        if (softwareBitmap != null)
                        {
                            int width = softwareBitmap.PixelWidth;
                            int height = softwareBitmap.PixelHeight;

                            if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Nv12)
                            {
                                softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Nv12, BitmapAlphaMode.Ignore);
                            }

                            // Swap the processed frame to _backBuffer and dispose of the unused image.
                            softwareBitmap = Interlocked.Exchange(ref _backBuffer, softwareBitmap);

                            using (BitmapBuffer buffer = _backBuffer.LockBuffer(BitmapBufferAccessMode.Read))
                            {
                                using (var reference = buffer.CreateReference())
                                {
                                    unsafe
                                    {
                                        byte* dataInBytes;
                                        uint capacity;
                                        ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacity);
                                        byte[] nv12Buffer = new byte[capacity];
                                        Marshal.Copy((IntPtr)dataInBytes, nv12Buffer, 0, (int)capacity);

                                        if (OnVideoSourceEncodedSample != null)
                                        {
                                            lock (_vp8Encoder)
                                            {
                                                var encodedBuffer = _vp8Encoder.Encode(nv12Buffer, _forceKeyFrame);

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
                                            }
                                        }

                                        if (OnVideoSourceRawSample != null)
                                        {
                                            uint frameSpacing = 0;
                                            if (_lastFrameAt != DateTime.MinValue)
                                            {
                                                frameSpacing = Convert.ToUInt32(DateTime.Now.Subtract(_lastFrameAt).TotalMilliseconds);
                                            }

                                            var bgrBuffer = PixelConverter.NV12toBGR(nv12Buffer, width, height);

                                            OnVideoSourceRawSample(frameSpacing, width, height, bgrBuffer, VideoPixelFormatsEnum.Bgr);
                                        }
                                    }
                                }
                            }

                            _backBuffer?.Dispose();
                            softwareBitmap?.Dispose();
                        }

                        _lastFrameAt = DateTime.Now;
                    }
                }
            }
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
                                else if (pixelFormat == VideoPixelFormatsEnum.Bgr)
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

        /// <summary>
        /// Diagnostic method to print the details of a video frame source.
        /// </summary>
        private void PrintFrameSourceInfo(MediaFrameSource frameSource)
        {
            var width = frameSource.CurrentFormat.VideoFormat.Width;
            var height = frameSource.CurrentFormat.VideoFormat.Height;
            var fpsNumerator = frameSource.CurrentFormat.FrameRate.Numerator;
            var fpsDenominator = frameSource.CurrentFormat.FrameRate.Denominator;

            double fps = fpsNumerator / fpsDenominator;
            string pixFmt = frameSource.CurrentFormat.Subtype;
            string deviceName = frameSource.Info.DeviceInformation.Name;

            logger.LogInformation($"Video capture device {deviceName} successfully initialised: {width}x{height} {fps:0.##}fps pixel format {pixFmt}.");
        }

        public void Dispose()
        {
            _vp8Encoder?.Dispose();
            _vp8Decoder?.Dispose();
        }

        //List<VideoFormat> IVideoSink.GetVideoSinkFormats()
        //{
        //    throw new NotImplementedException();
        //}

        //public void SetVideoSinkFormat(VideoFormat videoFormat)
        //{
        //    throw new NotImplementedException();
        //}

        //public void RestrictFormats(Func<VideoFormat, bool> filter)
        //{
        //    throw new NotImplementedException();
        //}

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
}
