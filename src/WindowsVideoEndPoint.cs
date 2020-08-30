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
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Windows.Codecs;

namespace SIPSorceryMedia.Windows
{
    public enum VideoSourcesEnum
    {
        None = 0,
        Webcam = 1,
        TestPattern = 2,
        ExternalBitmap = 3, // For example audio scope visualisations.
    }

    public class VideoSourceOptions
    {
        public const int DEFAULT_FRAME_RATE = 30;

        /// <summary>
        /// The type of video source to use.
        /// </summary>
        public VideoSourcesEnum VideoSource;

        /// <summary>
        /// IF using a video test pattern this is the base image source file.
        /// </summary>
        public string SourceFile;

        /// <summary>
        /// The frame rate to apply to request for the video source. May not be
        /// applied for certain sources such as a live webcam feed.
        /// </summary>
        public int SourceFramesPerSecond = DEFAULT_FRAME_RATE;

        //public IBitmapSource BitmapSource;
    }

    public class WindowsVideoEndPoint : IVideoSource, IVideoSink, IDisposable
    {
        private const string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const int TEST_PATTERN_SPACING_MILLISECONDS = 33;
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const int VIDEO_TIMESTAMP_SPACING = 3000;

        public static ILogger logger = NullLogger.Instance;

        private Bitmap _testPattern;
        private Vp8Codec _vp8Encoder;
        private Vp8Codec _vp8Decoder;
        private Timer _sendTestPatternTimer;
        private bool _forceKeyFrame = false;
        private long _presentationTimestamp = 0;
        private VideoFormat _selectedSinkFormat = new VideoFormat { Codec = VideoCodecsEnum.VP8, PayloadID = 100 };
        private VideoFormat _selectedSourceFormat = new VideoFormat { Codec = VideoCodecsEnum.VP8, PayloadID = 100 };
        private byte[] _currVideoFrame = new byte[65536];
        private int _currVideoFramePosn = 0;
        private bool _isStarted;
        private bool _isClosed;

        /// <summary>
        /// The video source and sink only accept and produce encoded samples.
        /// There are currently no C# only video codecs that can be used so no point
        /// providing raw samples to the main library.
        /// </summary>
        public bool EncodedSamplesOnly
        {
            get { return true; }
            set { }
        }

        public event SourceErrorDelegate OnVideoSourceFailure;
        public event VideoEncodedSampleDelegate OnVideoSourceEncodedSample;

        /// <summary>
        /// Not currently used.
        /// </summary>
        public event RawVideoSampleDelegate OnVideoSourceRawSample;

        /// <summary>
        /// This event is fired after the sink decodes a video frame from the remote party.
        /// </summary>
        public event VideoSinkSampleDecodedDelegate OnVideoSinkDecodedSample;

        /// <summary>
        /// Creates a new basic RTP session that captures and renders audio to/from the default system devices.
        /// </summary>
        public WindowsVideoEndPoint()
        {
            //InitialiseTestPattern();

            _vp8Decoder = new Vp8Codec();
            _vp8Decoder.InitialiseDecoder();
        }

        public MediaEndPoints ToMediaEndPoints()
        {
            return new MediaEndPoints
            {
                VideoSource = this,
                VideoSink = this
            };
        }

        public void GotVideoRtp(IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
        {
            //logger.LogDebug($"rtp video, seqnum {seqnum}, ts {timestamp}, marker {marker}, payload {payload.Length}.");
            if (_currVideoFramePosn + payload.Length >= _currVideoFrame.Length)
            {
                // Something has gone very wrong. Clear the buffer.
                _currVideoFramePosn = 0;
            }

            // New frames must have the VP8 Payload Descriptor Start bit set.
            // The tracking of the current video frame position is to deal with a VP8 frame being split across multiple RTP packets
            // as per https://tools.ietf.org/html/rfc7741#section-4.4.
            if (_currVideoFramePosn > 0 || (payload[0] & 0x10) > 0)
            {
                RtpVP8Header vp8Header = RtpVP8Header.GetVP8Header(payload);

                Buffer.BlockCopy(payload, vp8Header.Length, _currVideoFrame, _currVideoFramePosn, payload.Length - vp8Header.Length);
                _currVideoFramePosn += payload.Length - vp8Header.Length;

                if (marker)
                {
                    DateTime startTime = DateTime.Now;

                    List<byte[]> decodedFrames = _vp8Decoder.Decode(_currVideoFrame, _currVideoFramePosn, out var width, out var height);

                    if (decodedFrames == null)
                    {
                        logger.LogWarning("VPX decode of video sample failed.");
                    }
                    else
                    {
                        foreach (var decodedFrame in decodedFrames)
                        {
                            //var rgb = _i420Converter.ConvertToBuffer(decodedFrame);
                            byte[] rgb = PixelConverter.I420toRGB(decodedFrame, (int)width, (int)height);

                            //Console.WriteLine($"VP8 decode took {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms.");

                            OnVideoSinkDecodedSample(rgb, width, height, (int)(width * 3));
                        }

                        //Console.WriteLine($"VP8 decode took {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms.");

                        //foreach (var rgb in decodedFrames)
                        //{
                        //    OnVideoSinkDecodedSample(rgb, width, height, (int)(width * 3));
                        //}
                    }

                    _currVideoFramePosn = 0;
                }
            }
            else
            {
                logger.LogWarning("Discarding RTP packet, VP8 header Start bit not set.");
                logger.LogWarning($"rtp video, seqnum {seqnum}, ts {timestamp}, marker {marker}, payload {payload.Length}.");
            }
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
            return new List<VideoFormat> { new VideoFormat { Codec = VideoCodecsEnum.VP8 } };
        }

        public void SetVideoSourceFormat(VideoFormat videoFormat)
        {
            _selectedSourceFormat = videoFormat;
        }

        public List<VideoFormat> GetVideoSinkFormats()
        {
            return new List<VideoFormat> { new VideoFormat { Codec = VideoCodecsEnum.VP8 } };
        }

        public void SetVideoSinkFormat(VideoFormat videoFormat)
        {
            _selectedSinkFormat = videoFormat;
        }

        private void InitialiseTestPattern()
        {
            _testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);
            _sendTestPatternTimer = new Timer(SendTestPattern, null, Timeout.Infinite, Timeout.Infinite);

            if (_selectedSourceFormat.Codec == VideoCodecsEnum.VP8)
            {
                _vp8Encoder = new Vp8Codec();
                _vp8Encoder.InitialiseEncoder((uint)_testPattern.Width, (uint)_testPattern.Height);

                // Can also use FFmpeg which wraps libvpx.
                //_ffmpegEncoder = new VideoEncoder(AVCodecID.AV_CODEC_ID_VP8, _testPattern.Width, _testPattern.Height, FRAMES_PER_SECOND);
            }
            //else if (VIDEO_CODEC == SDPMediaFormatsEnum.H264)
            //{
            //    _ffmpegEncoder = new VideoEncoder(AVCodecID.AV_CODEC_ID_H264, _testPattern.Width, _testPattern.Height, FRAMES_PER_SECOND);
            //    _videoFrameConverter = new VideoFrameConverter(
            //        new Size(_testPattern.Width, _testPattern.Height),
            //        AVPixelFormat.AV_PIX_FMT_BGRA,
            //        new Size(_testPattern.Width, _testPattern.Height),
            //        AVPixelFormat.AV_PIX_FMT_YUV420P);
            //}
            else
            {
                throw new ApplicationException($"Video codec {_selectedSourceFormat.Codec} is not supported.");
            }
        }

        private void SendTestPattern(object state)
        {
            lock (_sendTestPatternTimer)
            {
                unsafe
                {
                    if (!_isClosed && OnVideoSourceEncodedSample != null)
                    {
                        var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                        AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                        var sampleBuffer = PixelConverter.BitmapToRGBA(stampedTestPattern as System.Drawing.Bitmap, _testPattern.Width, _testPattern.Height);

                        byte[] encodedBuffer = null;

                        if (_selectedSourceFormat.Codec == VideoCodecsEnum.VP8)
                        {
                            byte[] i420Buffer = PixelConverter.RGBAtoYUV420Planar(sampleBuffer, _testPattern.Width, _testPattern.Height);
                            encodedBuffer = _vp8Encoder.Encode(i420Buffer, _forceKeyFrame);
                        }
                        //else if (VIDEO_CODEC == SDPMediaFormatsEnum.H264)
                        //{
                        //    var i420Frame = _videoFrameConverter.Convert(sampleBuffer);

                        //    _presentationTimestamp += VIDEO_TIMESTAMP_SPACING;

                        //    i420Frame.key_frame = _forceKeyFrame ? 1 : 0;
                        //    i420Frame.pts = _presentationTimestamp;

                        //    encodedBuffer = _ffmpegEncoder.Encode(i420Frame);
                        //}
                        else
                        {
                            _sendTestPatternTimer.Dispose();
                            throw new ApplicationException($"Video codec is not supported.");
                        }

                        if (encodedBuffer != null)
                        {
                            //Console.WriteLine($"encoded buffer: {encodedBuffer.HexStr()}");
                            OnVideoSourceEncodedSample?.Invoke(_selectedSourceFormat, VIDEO_TIMESTAMP_SPACING, encodedBuffer);
                        }

                        if (_forceKeyFrame)
                        {
                            _forceKeyFrame = false;
                        }

                        stampedTestPattern.Dispose();
                    }
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
            _vp8Encoder?.Dispose();
            _vp8Decoder?.Dispose();
        }
    }
}
