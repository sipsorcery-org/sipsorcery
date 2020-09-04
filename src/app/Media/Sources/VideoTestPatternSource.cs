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
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Media
{
    public class VideoTestPatternSource : IVideoSource, IDisposable
    {
        public const string TEST_PATTERN_RESOURCE_PATH = "media.testpattern.jpeg";
        public const string TEST_PATTERN_INVERTED_RESOURCE_PATH = "media.testpattern_inverted.jpeg";

        private const int MAXIMUM_FRAMES_PER_SECOND = 30;
        private const int DEFAULT_FRAMES_PER_SECOND = 30;
        private const int MINIMUM_FRAMES_PER_SECOND = 1;
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;          // Height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f;     // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;

        public static ILogger logger = Sys.Log.Logger;

        public static readonly List<VideoCodecsEnum> SupportedCodecs = new List<VideoCodecsEnum>
        {
            VideoCodecsEnum.VP8
        };

        private List<VideoCodecsEnum> _supportedCodecs = new List<VideoCodecsEnum>(SupportedCodecs);
        private int _frameSpacing;
        private Bitmap _testPattern;
        private Timer _sendTestPatternTimer;
        private bool _isStarted;
        private bool _isClosed;

        public event RawVideoSampleDelegate OnVideoSourceRawSample;

        /// <summary>
        /// This video source DOES NOT generate encoded samples. Subscribe to the raw samples
        /// event and pass to an encoder.
        /// </summary>
        [Obsolete("This video source is not currently capable of generating encoded samples.")]
        public event EncodedSampleDelegate OnVideoSourceEncodedSample { add { } remove { } }

        public VideoTestPatternSource()
        {
            EmbeddedFileProvider efp = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
            var testPatternFileInfo = efp.GetFileInfo(TEST_PATTERN_RESOURCE_PATH);

            if (testPatternFileInfo == null)
            {
                throw new ApplicationException($"Test pattern embedded resource could not be found, {TEST_PATTERN_RESOURCE_PATH}.");
            }
            else
            {
                _testPattern = new Bitmap(testPatternFileInfo.CreateReadStream());
                _sendTestPatternTimer = new Timer(GenerateTestPattern, null, Timeout.Infinite, Timeout.Infinite);
                _frameSpacing = 1000 / DEFAULT_FRAMES_PER_SECOND;
            }
        }

        public void SetEmbeddedTestPatternPath(string path)
        {
            EmbeddedFileProvider efp = new EmbeddedFileProvider(Assembly.GetExecutingAssembly());
            var testPattenFileInfo = efp.GetFileInfo(path);

            if(testPattenFileInfo == null)
            {
                logger.LogWarning($"Video test pattern source could not locate embedded path {path}.");
            }
            else
            {
                logger.LogDebug($"Test pattern loaded from embedded resource {path}.");

                lock (_sendTestPatternTimer)
                {
                    _testPattern?.Dispose();
                    _testPattern = new Bitmap(testPattenFileInfo.CreateReadStream());
                }
            }
        }

        public void SetTestPatternPath(string path)
        {
            if (!File.Exists(path))
            {
                logger.LogWarning($"The test pattern file could not be found at {path}.");
            }
            else
            {
                logger.LogDebug($"Test pattern loaded from {path}.");

                lock (_sendTestPatternTimer)
                {
                    _testPattern?.Dispose();
                    _testPattern = new Bitmap(path);
                }
            }
        }

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
        /// Requests that the video sink and source only advertise support for the supplied list of codecs.
        /// Only codecs that are already supported and in the <see cref="SupportedCodecs" /> list can be 
        /// used.
        /// </summary>
        /// <param name="codecs">The list of codecs to restrict advertised support to.</param>
        public void RestrictCodecs(List<VideoCodecsEnum> codecs)
        {
            if (codecs == null || codecs.Count == 0)
            {
                _supportedCodecs = new List<VideoCodecsEnum>(SupportedCodecs);
            }
            else
            {
                _supportedCodecs = new List<VideoCodecsEnum>();
                foreach (var codec in codecs)
                {
                    if (SupportedCodecs.Any(x => x == codec))
                    {
                        _supportedCodecs.Add(codec);
                    }
                    else
                    {
                        logger.LogWarning($"Not including unsupported codec {codec} in filtered list.");
                    }
                }
            }
        }

        public Task PauseVideo()
        {
            _sendTestPatternTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            _sendTestPatternTimer.Change(0, _frameSpacing);
            return Task.CompletedTask;
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _sendTestPatternTimer.Change(0, _frameSpacing);
            }
            return Task.CompletedTask;
        }

        public Task CloseVideo()
        {
            if (!_isClosed)
            {
                _isClosed = true;
                Dispose();
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// This source can only supply raw RGB bitmap samples.
        /// </summary>
        public List<VideoCodecsEnum> GetVideoSourceFormats()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This source can only supply raw RGB bitmap samples.
        /// </summary>
        public void SetVideoSourceFormat(VideoCodecsEnum videoFormat)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// This source does not have any video encoding capabilities.
        /// </summary>
        public void ForceKeyFrame()
        {
            throw new NotImplementedException();
        }

        private void GenerateTestPattern(object state)
        {
            lock (_sendTestPatternTimer)
            {
                if (!_isClosed && OnVideoSourceRawSample != null)
                {
                    var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                    AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");

                    // This event handler could get removed while the timestamp text is being added.
                    OnVideoSourceRawSample?.Invoke((uint)_frameSpacing, _testPattern.Width, _testPattern.Height, BitmapToRGB24(stampedTestPattern as Bitmap));

                    stampedTestPattern?.Dispose();
                }
            }
        }

        private void AddTimeStampAndLocation(System.Drawing.Image image, string timeStamp, string locationText)
        {
            int pixelHeight = (int)(image.Height * TEXT_SIZE_PERCENTAGE);

            Graphics g = Graphics.FromImage(image);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (StringFormat format = new StringFormat())
            {
                format.LineAlignment = StringAlignment.Center;
                format.Alignment = StringAlignment.Center;

                using (Font f = new Font("Tahoma", pixelHeight, GraphicsUnit.Pixel))
                {
                    using (var gPath = new GraphicsPath())
                    {
                        float emSize = g.DpiY * f.Size / POINTS_PER_INCH;
                        if (locationText != null)
                        {
                            gPath.AddString(locationText, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, TEXT_MARGIN_PIXELS, image.Width, pixelHeight), format);
                        }

                        gPath.AddString(timeStamp /* + " -- " + fps.ToString("0.00") + " fps" */, f.FontFamily, (int)FontStyle.Bold, emSize, new Rectangle(0, image.Height - (pixelHeight + TEXT_MARGIN_PIXELS), image.Width, pixelHeight), format);
                        g.FillPath(Brushes.White, gPath);
                        g.DrawPath(new Pen(Brushes.Black, pixelHeight * TEXT_OUTLINE_REL_THICKNESS), gPath);
                    }
                }
            }
        }

        private byte[] BitmapToRGB24(Bitmap bitmap)
        {
            BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var length = bitmapData.Stride * bitmapData.Height;

            byte[] bytes = new byte[length];

            // Copy bitmap to byte[]
            Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
            bitmap.UnlockBits(bitmapData);

            return bytes;
        }

        public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] rgb24Sample)
        {
            throw new NotImplementedException("The test pattern video source does not offer any encoding services for external sources.");
        }

        public void Dispose()
        {
            if (_sendTestPatternTimer != null)
            {
                lock (_sendTestPatternTimer)
                {
                    _sendTestPatternTimer?.Dispose();
                }
            }
            _testPattern?.Dispose();
        }
    }
}
