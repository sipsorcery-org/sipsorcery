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
using System.Runtime.InteropServices.WindowsRuntime;
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
        private readonly string MF_NV12_PIXEL_FORMAT = MediaEncodingSubtypes.Nv12;
        private const string MF_I420_PIXEL_FORMAT = "{30323449-0000-0010-8000-00AA00389B71}";

        private static ILogger logger = SIPSorcery.LogFactory.CreateLogger<WindowsVideoEndPoint>();

        public static readonly List<VideoCodecsEnum> SupportedCodecs = new List<VideoCodecsEnum>
        {
            VideoCodecsEnum.VP8
        };

        private CodecManager<VideoCodecsEnum> _codecManager;
        private Vp8Codec _vp8Encoder;
        private Vp8Codec _vp8Decoder;
        private vpxmd.VpxImgFmt _encodeInputFormat;
        private bool _forceKeyFrame = false;
        private bool _isInitialised;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private MediaCapture _mediaCapture;
        private MediaFrameReader _mediaFrameReader;
        private MediaFrameSource _mediaFrameSource;
        private string _videoDeviceID;
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
        public WindowsVideoEndPoint(string videoDeviceID = null, uint width = 0, uint height = 0, uint fps = 0)
        {
            _codecManager = new CodecManager<VideoCodecsEnum>(SupportedCodecs);
            _videoDeviceID = videoDeviceID;
            _width = width;
            _height = height;
            _fpsNumerator = fps;

            _vp8Decoder = new Vp8Codec();
            _vp8Decoder.InitialiseDecoder();

            _mediaCapture = new MediaCapture();
            _mediaCapture.Failed += VideoCaptureDevice_Failed;
        }

        public void RestrictCodecs(List<VideoCodecsEnum> codecs) => _codecManager.RestrictCodecs(codecs);
        public List<VideoCodecsEnum> GetVideoSourceFormats() => _codecManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat) => _codecManager.SetSelectedCodec(videoFormat);
        public List<VideoCodecsEnum> GetVideoSinkFormats() => _codecManager.GetSourceFormats();
        public void SetVideoSinkFormat(VideoCodecsEnum videoFormat) => _codecManager.SetSelectedCodec(videoFormat);
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
                var vidCapDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
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

            await _mediaCapture.InitializeAsync(mediaCaptureSettings); //.AsTask().ConfigureAwait(false);
            MediaFrameSource mediaFrameSource = null;

            if (width == 0 && height == 0 && fps == 0)
            {
                // If no specific width, height or frame rate was requested still try and find an optimal pixel format.
                mediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                    source.Value.Info.SourceKind == MediaFrameSourceKind.Color &&
                    //source.Value.SupportedFormats.Any(x =>
                    //    (x.Subtype == MF_NV12_PIXEL_FORMAT || x.Subtype == MF_I420_PIXEL_FORMAT))).Value;
                    source.Value.SupportedFormats.Any(x =>
                        (x.Subtype == MediaEncodingSubtypes.Rgb24))).Value;

                if (mediaFrameSource == null)
                {
                    // Couldn't match the pixel format so just accept anything the video device will give us.
                    mediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source =>
                       source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                       source.Value.Info.SourceKind == MediaFrameSourceKind.Color).Value;
                }
            }
            else
            {
                // If specific capture settings have been requested then the device needs to be initialised in
                // exclusive mode as the current settings and format will most likely be changed.
                mediaFrameSource = _mediaCapture.FrameSources.FirstOrDefault(source =>
                    source.Value.Info.MediaStreamType == MediaStreamType.VideoRecord &&
                    source.Value.Info.SourceKind == MediaFrameSourceKind.Color &&
                    source.Value.SupportedFormats.Any(x =>
                        (x.Subtype == MF_NV12_PIXEL_FORMAT || x.Subtype == MF_I420_PIXEL_FORMAT) &&
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
            }

            if (mediaFrameSource == null)
            {
                throw new ApplicationException("The video capture device does not support a compatible video format for the requested parameters.");
            }

            _mediaFrameReader = await _mediaCapture.CreateFrameReaderAsync(mediaFrameSource); //.AsTask().ConfigureAwait(false);
            _mediaFrameReader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

            // If there's a format that matches the desired pixel format set that on the media frame source.
            //var idealFormat = mediaFrameSource.SupportedFormats.FirstOrDefault(x =>
            //    (x.Subtype == MF_NV12_PIXEL_FORMAT || x.Subtype == MF_I420_PIXEL_FORMAT) &&
            //    (_width == 0 || x.VideoFormat.Width == _width) &&
            //    (_height == 0 || x.VideoFormat.Height == _height) &&
            //    (_fpsNumerator == 0 || x.FrameRate.Numerator == _fpsNumerator));

            //if (idealFormat != null)
            //{
            //    await mediaFrameSource.SetFormatAsync(idealFormat); //.AsTask().ConfigureAwait(false);
            //}

            _mediaFrameSource = mediaFrameSource;

            // Frame source and format have now been successfully set.
            _width = _mediaFrameSource.CurrentFormat.VideoFormat.Width;
            _height = _mediaFrameSource.CurrentFormat.VideoFormat.Height;
            _fpsNumerator = _mediaFrameSource.CurrentFormat.FrameRate.Numerator;
            _fpsDenominator = _mediaFrameSource.CurrentFormat.FrameRate.Denominator;

            double fpsSelected = _fpsNumerator / _fpsDenominator;
            string pixFmt = _mediaFrameSource.CurrentFormat.Subtype == MF_I420_PIXEL_FORMAT ? "I420" : _mediaFrameSource.CurrentFormat.Subtype;
            string deviceName = _mediaFrameSource.Info.DeviceInformation.Name;
            logger.LogInformation($"Video capture device {deviceName} successfully initialised: {_width}x{_height} {fpsSelected:0.##}fps pixel format {pixFmt}.");

            _encodeInputFormat = _mediaFrameSource.CurrentFormat.Subtype == MF_I420_PIXEL_FORMAT ? vpxmd.VpxImgFmt.VPX_IMG_FMT_I420 : vpxmd.VpxImgFmt.VPX_IMG_FMT_NV12;

            logger.LogInformation($"Video capture device VP8 encoder input format set to {_encodeInputFormat}.");

            _vp8Encoder = new Vp8Codec();
            _vp8Encoder.InitialiseEncoder(_width, _height, _encodeInputFormat);

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
                using (var frame = sender.TryAcquireLatestFrame())
                {
                    if (_isClosed || frame == null || (frame.VideoMediaFrame == null && frame.BufferMediaFrame == null)) return;

                    if (!_isClosed && OnVideoSourceEncodedSample != null)
                    {
                        byte[] encoderInBuffer = null;

                        if (frame.VideoMediaFrame == null & frame.BufferMediaFrame != null)
                        {
                            encoderInBuffer = new byte[frame.BufferMediaFrame.Buffer.Length];
                            frame.BufferMediaFrame.Buffer.CopyTo(encoderInBuffer);
                        }
                        else
                        {
                            VideoMediaFrame vmf = frame.VideoMediaFrame;
                            var videoFrame = vmf.GetVideoFrame();
                            var sbmp = await SoftwareBitmap.CreateCopyFromSurfaceAsync(videoFrame.Direct3DSurface);
                            SoftwareBitmap inputBmp = null;

                            try
                            {
                                if (sbmp == null)
                                {
                                    logger.LogWarning("Failed to get bitmap from video frame reader.");
                                }
                                else
                                {
                                    inputBmp = sbmp;

                                    if (_mediaFrameSource.CurrentFormat.Subtype != MF_I420_PIXEL_FORMAT &&
                                        _mediaFrameSource.CurrentFormat.Subtype != MF_NV12_PIXEL_FORMAT)
                                    {
                                        // Frame pixel format needs to be converted to a VP8 encoder compatible format.
                                        SoftwareBitmap nv12bmp = SoftwareBitmap.Convert(sbmp, BitmapPixelFormat.Nv12);
                                        inputBmp = nv12bmp;
                                    }

                                    using (BitmapBuffer buffer = inputBmp.LockBuffer(BitmapBufferAccessMode.Read))
                                    {
                                        using (var reference = buffer.CreateReference())
                                        {
                                            unsafe
                                            {
                                                byte* dataInBytes;
                                                uint capacity;
                                                ((IMemoryBufferByteAccess)reference).GetBuffer(out dataInBytes, out capacity);

                                                encoderInBuffer = new byte[capacity];
                                                Marshal.Copy((IntPtr)dataInBytes, encoderInBuffer, 0, (int)capacity);
                                            }
                                        }
                                    }
                                }
                            }
                            finally
                            {
                                if (inputBmp != sbmp)
                                {
                                    inputBmp?.Dispose();
                                }

                                sbmp.Dispose();
                                videoFrame.Dispose();
                            }
                        }

                        if (encoderInBuffer != null)
                        {
                            lock (_vp8Encoder)
                            {
                                var encodedBuffer = _vp8Encoder.Encode(encoderInBuffer, _forceKeyFrame);

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

        public void Dispose()
        {
            _vp8Encoder?.Dispose();
            _vp8Decoder?.Dispose();
        }
    }
}
