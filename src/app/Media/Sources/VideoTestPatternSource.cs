using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SIPSorceryMedia.Abstractions.V1;

namespace SIPSorcery.Media
{
    public class VideoTestPatternSource : IVideoSource, IDisposable
    {
        private const string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const int TEST_PATTERN_SPACING_MILLISECONDS = 33;
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;          // Height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f;     // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;

        private Bitmap _testPattern;
        private Timer _sendTestPatternTimer;
        private bool _isStarted;
        private bool _isClosed;

        public event RawVideoSampleDelegate OnVideoSourceRawSample;

        [Obsolete("This video source is not currently capable of generating encoded samples.")]
        public event VideoEncodedSampleDelegate OnVideoSourceEncodedSample;

        public VideoTestPatternSource()
        {
            InitialiseTestPattern();
        }

        public Task PauseVideo()
        {
            _sendTestPatternTimer.Change(Timeout.Infinite, Timeout.Infinite);
            return Task.CompletedTask;
        }

        public Task ResumeVideo()
        {
            _sendTestPatternTimer.Change(0, TEST_PATTERN_SPACING_MILLISECONDS);
            return Task.CompletedTask;
        }

        public Task StartVideo()
        {
            if (!_isStarted)
            {
                _isStarted = true;
                _sendTestPatternTimer.Change(0, TEST_PATTERN_SPACING_MILLISECONDS);
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

        public List<VideoFormat> GetVideoSourceFormats()
        {
            throw new NotImplementedException();
        }

        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            throw new NotImplementedException();
        }

        private void InitialiseTestPattern()
        {
            if (!File.Exists(TEST_PATTERN_IMAGE_PATH))
            {
                throw new ApplicationException($"Test pattern file could not be found, {TEST_PATTERN_IMAGE_PATH}.");
            }
            else
            {
                _testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);
                _sendTestPatternTimer = new Timer(GenerateTestPattern, null, Timeout.Infinite, Timeout.Infinite);
            }
        }

        private void GenerateTestPattern(object state)
        {
            lock (_sendTestPatternTimer)
            {
                if (!_isClosed && OnVideoSourceRawSample != null)
                {
                    var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                    AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");

                    OnVideoSourceRawSample(TEST_PATTERN_SPACING_MILLISECONDS, _testPattern.Width, _testPattern.Height, BitmapToRGB24(stampedTestPattern as Bitmap));

                    stampedTestPattern.Dispose();
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
