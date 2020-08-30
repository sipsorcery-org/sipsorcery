// ffplay -probesize 32 -protocol_whitelist "file,rtp,udp" -i ffplay-vp8.sdp

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using SIPSorceryMedia.Windows.Codecs;
using SIPSorcery.Net;
using System.IO;

namespace Vpx
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
        private static Vp8Codec _vp8Encoder;
        private static Vp8Codec _vp8Decoder;
        private static long _presentationTimestamp = 0;

        private static event Action<SDPMediaTypesEnum, uint, byte[]> OnTestPatternSampleReady;

        static void Main(string[] args)
        {
            Console.WriteLine("VPX Encoding Test Console");

            Initialise();

            //StreamToFFPlay();

            //RoundTripNoEncoding();

            for (int i = 0; i < 25; i++)
            {
                DateTime startTime = DateTime.Now;
                RoundTripTestPattern();
                Console.WriteLine($"encode+decode took {DateTime.Now.Subtract(startTime).TotalMilliseconds}ms.");
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadLine();
        }

        private static void Initialise()
        {
            _testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);
            _vp8Encoder = new Vp8Codec();
            _vp8Encoder.InitialiseEncoder((uint)_testPattern.Width, (uint)_testPattern.Height);
            _vp8Decoder = new Vp8Codec();
            _vp8Decoder.InitialiseDecoder();
        }

        private static void RoundTripNoEncoding()
        {
            int width = 32;
            int height = 32;

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

            //Console.WriteLine(srcRgb.HexStr());

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
            byte[] i420Buffer = PixelConverter.RGBtoI420(srcRgb, width, height);

            Console.WriteLine($"Converted rgb to i420.");

            byte[] rgbResult = PixelConverter.I420toRGB(i420Buffer, width, height);

            unsafe
            {
                fixed (byte* s = rgbResult)
                {
                    System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap(width, height, rgbResult.Length / height, PixelFormat.Format24bppRgb, (IntPtr)s);
                    bmpImage.Save("test-result.bmp");
                    bmpImage.Dispose();
                }
            }
        }

        private static void RoundTripTestPatternNoEncoding()
        {
            var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
            int tWidth = _testPattern.Width;
            int tHeight = _testPattern.Height;
            AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
            var sampleBuffer = PixelConverter.BitmapToRGBA(stampedTestPattern as System.Drawing.Bitmap, _testPattern.Width, _testPattern.Height);
            byte[] i420 = PixelConverter.RGBAtoYUV420Planar(sampleBuffer, _testPattern.Width, _testPattern.Height);

            byte[] rgb = PixelConverter.I420toRGB(i420, tWidth, tHeight);

            unsafe
            {
                fixed (byte* s = rgb)
                {
                    System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap(tWidth, tHeight, rgb.Length / tHeight, PixelFormat.Format24bppRgb, (IntPtr)s);
                    bmpImage.Save("roundtrip.bmp");
                    bmpImage.Dispose();
                }
            }
        }

        private static void RoundTripTestPattern()
        {
            var stampedTestPattern = _testPattern.Clone() as System.Drawing.Image;
            int tWidth = _testPattern.Width;
            int tHeight = _testPattern.Height;
            AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
            var sampleBuffer = PixelConverter.BitmapToRGBA(stampedTestPattern as System.Drawing.Bitmap, _testPattern.Width, _testPattern.Height);
            byte[] i420 = PixelConverter.RGBAtoYUV420Planar(sampleBuffer, _testPattern.Width, _testPattern.Height);

            var encodedBuffer = _vp8Encoder.Encode(i420, false);

            Console.WriteLine($"VP8 encoded buffer length {encodedBuffer.Length}.");

            List<byte[]> i420Frames = _vp8Decoder.Decode(encodedBuffer, encodedBuffer.Length, out var dWidth, out var dHeight);

            Console.WriteLine($"VP8 decoded frames count {i420Frames.Count}, first frame length {i420Frames.First().Length}, width {dWidth}, height {dHeight}.");

            byte[] rgb = i420Frames.First();

            unsafe
            {
                fixed (byte* s = rgb)
                {
                    System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap((int)dWidth, (int)dHeight, rgb.Length / (int)dHeight, PixelFormat.Format24bppRgb, (IntPtr)s);
                    bmpImage.Save("encodedroundtrip.bmp");
                    bmpImage.Dispose();
                }
            }
        }

        private static void StreamToFFPlay()
        {
            var videoCapabilities = new List<SDPMediaFormat>
                {
                    new SDPMediaFormat(SDPMediaFormatsEnum.VP8)
                };
            int payloadID = Convert.ToInt32(videoCapabilities.First().FormatID);

            var rtpSession = CreateRtpSession(videoCapabilities);
            OnTestPatternSampleReady += (media, duration, payload) => rtpSession.SendVp8Frame(duration, payloadID, payload);
            rtpSession.Start();

            Console.WriteLine("press any key to start...");
            Console.ReadKey();

            _sendTestPatternTimer = new Timer(SendTestPattern, null, 0, TEST_PATTERN_SPACING_MILLISECONDS);
        }

        private static RTPSession CreateRtpSession(List<SDPMediaFormat> videoFormats)
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
                        var sampleBuffer = PixelConverter.BitmapToRGBA(stampedTestPattern as System.Drawing.Bitmap, _testPattern.Width, _testPattern.Height);

                        byte[] i420Buffer = PixelConverter.RGBAtoYUV420Planar(sampleBuffer, _testPattern.Width, _testPattern.Height);
                        var encodedBuffer = _vp8Encoder.Encode(i420Buffer, false);

                        _presentationTimestamp += VIDEO_TIMESTAMP_SPACING;

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
    }
}
