//-----------------------------------------------------------------------------
// Filename: VideoTestPatternSource.cs
//
// Description: Implements a video test pattern source based on a static 
// jpeg file.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Sep 2020	Aaron Clauson	Created, Dublin, Ireland.
// 05 Nov 2020  Aaron Clauson   Added video encoder parameter.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Media
{
    public class VideoTestPatternSource : IVideoSource, IDisposable
    {
        public const string TEST_PATTERN_RESOURCE_PATH = "SIPSorcery.media.testpattern.i420";
        public const int TEST_PATTERN_WIDTH = 640;
        public const int TEST_PATTERN_HEIGHT = 480;

        private const int VIDEO_SAMPLING_RATE = 90000;
        private const int MAXIMUM_FRAMES_PER_SECOND = 60;           // Note the Threading.Timer's maximum callback rate is approx 60/s so allowing higher has no effect.
        private const int DEFAULT_FRAMES_PER_SECOND = 30;
        private const int MINIMUM_FRAMES_PER_SECOND = 1;
        private const int STAMP_BOX_SIZE = 20;
        private const int STAMP_BOX_PADDING = 10;
        private const int TIMER_DISPOSE_WAIT_MILLISECONDS = 1000;
        private const int VP8_SUGGESTED_FORMAT_ID = 96;
        private const int H264_SUGGESTED_FORMAT_ID = 100;

        public static ILogger logger = Sys.Log.Logger;

        public static readonly List<VideoFormat> SupportedFormats = new List<VideoFormat>
        {
            new VideoFormat(VideoCodecsEnum.VP8, VP8_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE),
            new VideoFormat(VideoCodecsEnum.H264, H264_SUGGESTED_FORMAT_ID, VIDEO_SAMPLING_RATE)
        };

        private int _frameSpacing;
        private byte[] _testI420Buffer;
        private Timer _sendTestPatternTimer;
        private bool _isStarted;
        private bool _isPaused;
        private bool _isClosed;
        private bool _isMaxFrameRate;
        private int _frameCount;
        private IVideoEncoder _videoEncoder;
        private MediaFormatManager<VideoFormat> _formatManager;

        /// <summary>
        /// Unencoded test pattern samples.
        /// </summary>
        public event RawVideoSampleDelegate OnVideoSourceRawSample;

        /// <summary>
        /// If a video encoder has been set then this event contains the encoded video
        /// samples.
        /// </summary>
        public event EncodedSampleDelegate OnVideoSourceEncodedSample;

        public event SourceErrorDelegate OnVideoSourceError;

        public VideoTestPatternSource(IVideoEncoder encoder = null)
        {
            if (encoder != null)
            {
                _videoEncoder = encoder;
                _formatManager = new MediaFormatManager<VideoFormat>(SupportedFormats);
            }

            var assem = typeof(VideoTestPatternSource).GetTypeInfo().Assembly;
            var testPatternStm = assem.GetManifestResourceStream(TEST_PATTERN_RESOURCE_PATH);

            if (testPatternStm == null)
            {
                OnVideoSourceError?.Invoke($"Test pattern embedded resource could not be found, {TEST_PATTERN_RESOURCE_PATH}.");
            }
            else
            {
                _testI420Buffer = new byte[TEST_PATTERN_WIDTH * TEST_PATTERN_HEIGHT * 3 / 2];
                testPatternStm.Read(_testI420Buffer, 0, _testI420Buffer.Length);
                testPatternStm.Close();
                _sendTestPatternTimer = new Timer(GenerateTestPattern, null, Timeout.Infinite, Timeout.Infinite);
                _frameSpacing = 1000 / DEFAULT_FRAMES_PER_SECOND;
            }
        }

        public void RestrictFormats(Func<VideoFormat, bool> filter) => _formatManager.RestrictFormats(filter);
        public List<VideoFormat> GetVideoSourceFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSourceFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);
        public List<VideoFormat> GetVideoSinkFormats() => _formatManager.GetSourceFormats();
        public void SetVideoSinkFormat(VideoFormat videoFormat) => _formatManager.SetSelectedFormat(videoFormat);

        public void ForceKeyFrame() => _videoEncoder?.ForceKeyFrame();
        public bool HasEncodedVideoSubscribers() => OnVideoSourceEncodedSample != null;
        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixlFormat) =>
            throw new NotImplementedException("The test pattern video source does not offer any encoding services for external sources.");
        public Task<bool> InitialiseVideoSourceDevice() =>
            throw new NotImplementedException("The test pattern video source does not use a device.");
        public bool IsVideoSourcePaused() => _isPaused;

        //public void SetEmbeddedTestPatternPath(string path)
        //{
        //    var assem = typeof(VideoTestPatternSource).GetTypeInfo().Assembly;
        //    var testPatternStm = assem.GetManifestResourceStream(path);

        //    if (testPatternStm == null)
        //    {
        //        OnVideoSourceError?.Invoke($"Video test pattern source could not locate embedded path {path}.");
        //    }
        //    else
        //    {
        //        logger.LogDebug($"Test pattern loaded from embedded resource {path}.");

        //        lock (_sendTestPatternTimer)
        //        {
        //            var bmp = new Bitmap(testPatternStm);
        //            LoadI420Buffer(bmp);
        //            bmp.Dispose();
        //        }
        //    }
        //}

        //public void SetTestPatternPath(string path)
        //{
        //    if (!File.Exists(path))
        //    {
        //        logger.LogWarning($"The test pattern file could not be found at {path}.");
        //    }
        //    else
        //    {
        //        logger.LogDebug($"Test pattern loaded from {path}.");

        //        lock (_sendTestPatternTimer)
        //        {
        //            var bmp = new Bitmap(path);
        //            LoadI420Buffer(bmp);
        //            bmp.Dispose();
        //        }
        //    }
        //}

        public void SetFrameRate(int framesPerSecond)
        {
            if (framesPerSecond < MINIMUM_FRAMES_PER_SECOND || framesPerSecond > MAXIMUM_FRAMES_PER_SECOND)
            {
                logger.LogWarning($"Frames per second not in the allowed range of {MINIMUM_FRAMES_PER_SECOND} to {MAXIMUM_FRAMES_PER_SECOND}, ignoring.");
            }
            else
            {
                _frameSpacing = 1000 / framesPerSecond;

                if (_isStarted)
                {
                    _sendTestPatternTimer.Change(0, _frameSpacing);
                }
            }
        }

        /// <summary>
        /// If this gets set the frames will be generated in a loop with no pause. Ideally this would
        /// only ever be done in load test scenarios.
        /// </summary>
        public void SetMaxFrameRate(bool isMaxFrameRate)
        {
            if (_isMaxFrameRate != isMaxFrameRate)
            {
                _isMaxFrameRate = isMaxFrameRate;

                if (_isStarted)
                {
                    if (_isMaxFrameRate)
                    {
                        _sendTestPatternTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        GenerateMaxFrames();
                    }
                    else
                    {
                        _sendTestPatternTimer.Change(0, _frameSpacing);
                    }
                }
            }
        }

        public Task PauseVideo()
        {
            _isPaused = true;
            _sendTestPatternTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            _isPaused = false;
            _sendTestPatternTimer.Change(0, _frameSpacing);
            return Task.CompletedTask;
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                if (_isMaxFrameRate)
                {
                    GenerateMaxFrames();
                }
                else
                {
                    _sendTestPatternTimer.Change(0, _frameSpacing);
                }
            }
            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;

                ManualResetEventSlim mre = new ManualResetEventSlim();
                _sendTestPatternTimer?.Dispose(mre.WaitHandle);
                return Task.Run(() => mre.Wait(TIMER_DISPOSE_WAIT_MILLISECONDS));
            }
            return Task.CompletedTask;
        }

        //private void LoadI420Buffer(Bitmap bitmap)
        //{
        //    _testBufferWidth = bitmap.Width;
        //    _testBufferHeight = bitmap.Height;
        //    _testI420Buffer = BitmapToI420(bitmap);
        //}

        private void GenerateMaxFrames()
        {
            DateTime lastGenerateTime = DateTime.Now;

            while (!_isClosed && _isMaxFrameRate)
            {
                _frameSpacing = Convert.ToInt32(DateTime.Now.Subtract(lastGenerateTime).TotalMilliseconds);
                GenerateTestPattern(null);
                lastGenerateTime = DateTime.Now;
            }
        }

        private void GenerateTestPattern(object state)
        {
            lock (_sendTestPatternTimer)
            {
                if (!_isClosed && (OnVideoSourceRawSample != null || OnVideoSourceEncodedSample != null))
                {
                    _frameCount++;

                    //var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                    //AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                    //// This event handler could get removed while the timestamp text is being added.
                    //OnVideoSourceRawSample?.Invoke((uint)_frameSpacing, _testPattern.Width, _testPattern.Height, BitmapToBGR24(stampedTestPattern as Bitmap), VideoPixelFormatsEnum.Bgr);
                    //stampedTestPattern?.Dispose();
                    //OnVideoSourceRawSample?.Invoke((uint)_frameSpacing, _testPatternWidth, _testPatternHeight, _testPatternI420, VideoPixelFormatsEnum.I420);
                    StampI420Buffer(_testI420Buffer, TEST_PATTERN_WIDTH, TEST_PATTERN_HEIGHT, _frameCount);

                    OnVideoSourceRawSample?.Invoke((uint)_frameSpacing, TEST_PATTERN_WIDTH, TEST_PATTERN_HEIGHT, _testI420Buffer, VideoPixelFormatsEnum.I420);

                    if (_videoEncoder != null && OnVideoSourceEncodedSample != null)
                    {
                        var encodedBuffer = _videoEncoder.EncodeVideo(TEST_PATTERN_WIDTH, TEST_PATTERN_HEIGHT, _testI420Buffer, VideoPixelFormatsEnum.I420, _formatManager.SelectedFormat.Codec);

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

        //private void AddTimeStampAndLocation(System.Drawing.Image image, string timeStamp, string locationText)
        //{
        //    int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

        //    Graphics g = Graphics.FromImage(image);
        //    g.SmoothingMode = SmoothingMode.AntiAlias;
        //    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        //    g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        //    using (StringFormat format = new StringFormat())
        //    {
        //        format.LineAlignment = StringAlignment.Center;
        //        format.Alignment = StringAlignment.Center;

        //        using (Font f = new Font("Tahoma", pixelHeight, GraphicsUnit.Pixel))
        //        {
        //            using (var gPath = new GraphicsPath())
        //            {
        //                float emSize = g.DpiY * f.Size / POINTS_PER_INCH;
        //                if (locationText != null)
        //                {
        //                    gPath.AddString(locationText, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, TEXT_MARGIN_PIXELS, image.Width, pixelHeight), format);
        //                }

        //                gPath.AddString(timeStamp /* + " -- " + fps.ToString("0.00") + " fps" */, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, image.Height - (pixelHeight + TEXT_MARGIN_PIXELS), image.Width, pixelHeight), format);
        //                g.FillPath(Brushes.White, gPath);
        //                g.DrawPath(new Pen(Brushes.Black, pixelHeight * TEXT_OUTLINE_REL_THICKNESS), gPath);
        //            }
        //        }
        //    }
        //}

        //public static byte[] BitmapToBGR24(Bitmap bitmap)
        //{
        //    if (bitmap.PixelFormat != PixelFormat.Format24bppRgb)
        //    {
        //        throw new ApplicationException("BitmapToRGB24 cannot convert from a non 24bppRgb pixel format.");
        //    }

        //    // NOTE: Pixel formats that have "Rgb" in their name, such as PixelFormat.Format24bppRgb,
        //    // use a buffer format of BGR. Many issues on StackOverflow regarding this,
        //    // e.g. https://stackoverflow.com/questions/5106505/converting-gdi-pixelformat-to-wpf-pixelformat.
        //    // Needs to be taken into account by the receiver of the BGR buffer.

        //    BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, bitmap.PixelFormat);
        //    var length = Math.Abs(bitmapData.Stride) * bitmapData.Height;

        //    byte[] bgrValues = new byte[length];

        //    Marshal.Copy(bitmapData.Scan0, bgrValues, 0, length);
        //    bitmap.UnlockBits(bitmapData);

        //    return bgrValues;
        //}

        //public static byte[] BitmapToI420(Bitmap bitmap)
        //{
        //    return BGRtoI420(BitmapToBGR24(bitmap), bitmap.Width, bitmap.Height);
        //}

        public static byte[] BGRtoI420(byte[] bgr, int width, int height)
        {
            int size = width * height;
            int uOffset = size;
            int vOffset = size + size / 4;
            int r, g, b, y, u, v;
            int posn = 0;

            byte[] buffer = new byte[width * height * 3 / 2];

            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    b = bgr[posn++] & 0xff;
                    g = bgr[posn++] & 0xff;
                    r = bgr[posn++] & 0xff;

                    y = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    u = (int)(-0.147 * r - 0.289 * g + 0.436 * b) + 128;
                    v = (int)(0.615 * r - 0.515 * g - 0.100 * b) + 128;

                    buffer[col + row * width] = (byte)(y > 255 ? 255 : y < 0 ? 0 : y);

                    int uvposn = col / 2 + row / 2 * width / 2;

                    buffer[uOffset + uvposn] = (byte)(u > 255 ? 255 : u < 0 ? 0 : u);
                    buffer[vOffset + uvposn] = (byte)(v > 255 ? 255 : v < 0 ? 0 : v);
                }
            }

            return buffer;
        }

        /// <summary>
        /// TODO: Add something better for a dynamic stamp on an I420 buffer. This is useful to provide
        /// a visual indication to the receiver that the video stream has not stalled.
        /// </summary>
        public static void StampI420Buffer(byte[] i420Buffer, int width, int height, int frameNumber)
        {
            // Draws a varying gray scale square in the bottom right corner on the base I420 buffer.
            int startX = width - STAMP_BOX_SIZE - STAMP_BOX_PADDING;
            int startY = height - STAMP_BOX_SIZE - STAMP_BOX_PADDING;

            for (int y = startY; y < startY + STAMP_BOX_SIZE; y++)
            {
                for (int x = startX; x < startX + STAMP_BOX_SIZE; x++)
                {
                    i420Buffer[y * width + x] = (byte)(frameNumber % 255);
                }
            }
        }

        public void Dispose()
        {
            _isClosed = true;
            _sendTestPatternTimer?.Dispose();
            _videoEncoder?.Dispose();
        }
    }
}
