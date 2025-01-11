// ffplay -probesize 32 -protocol_whitelist "file,rtp,udp" -i ffplay-h264.sdp

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using FFmpeg.AutoGen;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace FFmpegEncodingTest
{
    class Program
    {
        private static string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const int FRAMES_PER_SECOND = 30;
        private const int TEST_PATTERN_SPACING_MILLISECONDS = 33;
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const int VIDEO_TIMESTAMP_SPACING = 3000;
        private const int FFPLAY_DEFAULT_VIDEO_PORT = 5024;

        private static Bitmap _testPattern;
        private static Timer _sendTestPatternTimer;
        private static FFmpegVideoEncoder _ffmpegEncoder;
        private static VideoFrameConverter _videoFrameConverter;
        private static long _presentationTimestamp = 0;

        private static event Action<SDPMediaTypesEnum, uint, byte[]> OnTestPatternSampleReady;

        static void Main(string[] args)
        {
            Console.WriteLine("FFmpeg Encoding Test Console");

            InitialiseTestPattern();

            //RoundTripNoEncodingDummyBitmap();

            for (int i = 0; i < 25; i++)
            {
                DateTime startTime = DateTime.Now;

                RoundTripNoEncodingTestPattern();

                Console.WriteLine($"encode+decode took {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms.");
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        private static void RoundTripNoEncodingTestPattern()
        {
            var testBmp = new Bitmap(TEST_PATTERN_IMAGE_PATH);
            int w = testBmp.Width;
            int h = testBmp.Height;

            var rgbToi420 = new VideoFrameConverter(
                testBmp.Size.Width, testBmp.Size.Height,
                AVPixelFormat.AV_PIX_FMT_RGB24,
                testBmp.Size.Width, testBmp.Size.Height,
                AVPixelFormat.AV_PIX_FMT_YUV420P);

            var i420Converter = new VideoFrameConverter(
                testBmp.Size.Width, testBmp.Size.Height,
                AVPixelFormat.AV_PIX_FMT_YUV420P,
                testBmp.Size.Width, testBmp.Size.Height,
                AVPixelFormat.AV_PIX_FMT_RGB24);

            BitmapData bmpData = testBmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            IntPtr bmpDataPtr = bmpData.Scan0;
            int stride = bmpData.Stride;
            byte[] bmpBuffer = new byte[Math.Abs(stride * h)];
            Marshal.Copy(bmpDataPtr, bmpBuffer, 0, bmpBuffer.Length);
            testBmp.UnlockBits(bmpData);

            Console.WriteLine($"Test pattern stride {stride}.");

            // Convert bitmap to i420.
            var i420Buffer = rgbToi420.ConvertToBuffer(bmpBuffer);

            Console.WriteLine($"Converted rgb to i420 buffer, length {i420Buffer.Length}.");

            // Convert i420 back to bmp.
            var outRgb = i420Converter.ConvertToBuffer(i420Buffer);

            Console.WriteLine($"Converted i420 to rgb buffer, length {outRgb.Length}.");

            unsafe
            {
                fixed (byte* s = outRgb)
                {
                    System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap(w, h, outRgb.Length / h, PixelFormat.Format24bppRgb, (IntPtr)s);
                    bmpImage.Save("testpattern-result.bmp");
                    bmpImage.Dispose();
                }
            }
        }

        private static void RoundTripNoEncodingDummyBitmap()
        {
            int width = 32;
            int height = 32;
            Size sz = new Size(width, height);

            var rgbToi420 = new VideoFrameConverter(
                width, height,
                AVPixelFormat.AV_PIX_FMT_RGB24,
               width, height,
                AVPixelFormat.AV_PIX_FMT_YUV420P);

            var i420Converter = new VideoFrameConverter(
                width, height,
                AVPixelFormat.AV_PIX_FMT_YUV420P,
                width, height,
                AVPixelFormat.AV_PIX_FMT_RGB24);

            // Create dummy bitmap.
            byte[] srcRgb = new byte[width * height * 3];
            for (int row = 0; row < 32; row++)
            {
                for (int col = 0; col < 32; col++)
                {
                    int index = row * width * 3 + col * 3;

                    int red = (row < 16 && col < 16) ? 255 : 0;
                    int green = (row < 16 && col > 16) ? 255 : 0;
                    int blue = (row > 16 && col < 16) ? 255 : 0;

                    srcRgb[index] = (byte)red;
                    srcRgb[index + 1] = (byte)green;
                    srcRgb[index + 2] = (byte)blue;
                }
            }

            unsafe
            {
                fixed (byte* src = srcRgb)
                {
                    System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap(width, height, srcRgb.Length / height, PixelFormat.Format24bppRgb, (IntPtr)src);
                    bmpImage.Save("test-source.bmp");
                    bmpImage.Dispose();
                }
            }

            // Convert bitmap to i420.
            var i420Buffer = rgbToi420.ConvertToBuffer(srcRgb);

            Console.WriteLine($"Converted rgb to i420 buffer, length {i420Buffer.Length}.");

            // Convert i420 back to bmp.
            var outRgb = i420Converter.ConvertToBuffer(i420Buffer);

            Console.WriteLine($"Converted i420 to rgb buffer, length {outRgb.Length}.");

            unsafe
            {
                fixed (byte* s = outRgb)
                {
                    System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap(width, height, outRgb.Length / height, PixelFormat.Format24bppRgb, (IntPtr)s);
                    bmpImage.Save("test-result.bmp");
                    bmpImage.Dispose();
                }
            }
        }

        private void StreamToFFPlay()
        {
            var videoCapabilities = new List<SDPAudioVideoMediaFormat>
                {
                    new SDPAudioVideoMediaFormat(new VideoFormat(VideoCodecsEnum.H264, Helper.H264_FORMATID, Helper.DEFAULT_VIDEO_FRAME_RATE))
                    
                };
            int payloadID = Convert.ToInt32(videoCapabilities.First().ID);

            var rtpSession = CreateRtpSession(videoCapabilities);
            //OnTestPatternSampleReady += (media, duration, payload) => rtpSession.SendVp8Frame(duration, payloadID, payload);
            OnTestPatternSampleReady += (media, duration, payload) => rtpSession.SendVideo(duration, payload);
            rtpSession.Start();

            Console.WriteLine("press any key to start...");
            Console.ReadKey();

            _sendTestPatternTimer = new Timer(SendTestPattern, null, 0, TEST_PATTERN_SPACING_MILLISECONDS);
        }

        private static void InitialiseTestPattern()
        {
            _testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);

            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL);

            _ffmpegEncoder = new FFmpegVideoEncoder();
            Console.WriteLine($"Codec name {_ffmpegEncoder.GetCodecName()}.");

            _videoFrameConverter = new VideoFrameConverter(
                _testPattern.Width, _testPattern.Height,
                AVPixelFormat.AV_PIX_FMT_BGRA,
                _testPattern.Width, _testPattern.Height,
                AVPixelFormat.AV_PIX_FMT_YUV420P);
        }

        private static RTPSession CreateRtpSession(List<SDPAudioVideoMediaFormat> videoFormats)
        {
            var rtpSession = new RTPSession(false, false, false, IPAddress.Loopback);

            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.video, false, videoFormats, MediaStreamStatusEnum.SendRecv);
            rtpSession.addTrack(videoTrack);

            rtpSession.SetDestination(SDPMediaTypesEnum.video, new IPEndPoint(IPAddress.Loopback, FFPLAY_DEFAULT_VIDEO_PORT), new IPEndPoint(IPAddress.Loopback, FFPLAY_DEFAULT_VIDEO_PORT + 1));

            return rtpSession;
        }

        private static void SendTestPattern(object state)
        {
            lock (_sendTestPatternTimer)
            {
                unsafe
                {
                    if (OnTestPatternSampleReady != null)
                    {
                        var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
                        AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                        var sampleBuffer = BitmapToRGBA(stampedTestPattern as System.Drawing.Bitmap, _testPattern.Width, _testPattern.Height);

                        var i420Frame = _videoFrameConverter.Convert(sampleBuffer);

                        _presentationTimestamp += VIDEO_TIMESTAMP_SPACING;

                        //i420Frame.key_frame = _forceKeyFrame ? 1 : 0;
                        i420Frame.pts = _presentationTimestamp;

                        byte[] encodedBuffer = _ffmpegEncoder.Encode(AVCodecID.AV_CODEC_ID_VP8, &i420Frame, FRAMES_PER_SECOND);

                        if (encodedBuffer != null)
                        {
                            OnTestPatternSampleReady?.Invoke(SDPMediaTypesEnum.video, VIDEO_TIMESTAMP_SPACING, encodedBuffer);
                        }

                        stampedTestPattern.Dispose();
                    }
                }
            }
        }

        private static void AddTimeStampAndLocation(System.Drawing.Image image, string timeStamp, string locationText)
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

        public static byte[] BitmapToRGBA(Bitmap bmp, int width, int height)
        {
            int pixelSize = 0;
            switch (bmp.PixelFormat)
            {
                case PixelFormat.Format24bppRgb:
                    pixelSize = 3;
                    break;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppPArgb:
                case PixelFormat.Format32bppRgb:
                    pixelSize = 4;
                    break;
                default:
                    throw new ArgumentException($"Bitmap pixel format {bmp.PixelFormat} was not recognised in BitmapToRGBA.");
            }

            BitmapData bmpData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, bmp.PixelFormat);
            IntPtr ptr = bmpData.Scan0;
            byte[] buffer = new byte[width * height * 4];

            int cnt = 0;
            for (int y = 0; y <= height - 1; y++)
            {
                for (int x = 0; x <= width - 1; x++)
                {
                    int pos = y * bmpData.Stride + x * pixelSize;

                    var r = Marshal.ReadByte(ptr, pos + 0);
                    var g = Marshal.ReadByte(ptr, pos + 1);
                    var b = Marshal.ReadByte(ptr, pos + 2);

                    buffer[cnt + 0] = r; // r
                    buffer[cnt + 1] = g; // g
                    buffer[cnt + 2] = b; // b
                    buffer[cnt + 3] = 0x00;         // a
                    cnt += 4;
                }
            }

            bmp.UnlockBits(bmpData);

            return buffer;
        }
    }
}
