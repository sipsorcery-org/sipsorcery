//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that servers media streams
// to a WebRTC enabled browser.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 17 Jan 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Net;
using SIPSorceryMedia;

namespace WebRTCServer
{
    class Program
    {
        private static string TEST_PATTERN_IMAGE_PATH = "media/testpattern.jpeg";
        private const float TEXT_SIZE_PERCENTAGE = 0.035f;       // height of text as a percentage of the total image height
        private const float TEXT_OUTLINE_REL_THICKNESS = 0.02f; // Black text outline thickness is set as a percentage of text height in pixels
        private const int TEXT_MARGIN_PIXELS = 5;
        private const int POINTS_PER_INCH = 72;
        private const int VP8_TIMESTAMP_SPACING = 3000;
        private const int VP8_PAYLOAD_TYPE_ID = 100;
        private const string DTLS_CERTIFICATE_PATH = "certs/loalhost.pem";
        private const string DTLS_KEY_PATH = "certs/loalhost_key.pem";

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static bool _exit = false;
        private static ConcurrentDictionary<string, WebRtcSession> _webRtcSessions = new ConcurrentDictionary<string, WebRtcSession>();

        static void Main()
        {
            Console.WriteLine("WebRTC Server");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.

            AddConsoleLogger();
        }

        private void WebRtcStartCall()
        {
            //var mediaTypes = new List<RtpMediaTypesEnum> { RtpMediaTypesEnum.Video, RtpMediaTypesEnum.Audio };


            var webRtcSession = new WebRtcSession(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH, new List<IPAddress> { IPAddress.Loopback });

            //string dtlsThumbrpint = (isEncryptionDisabled == false) ? _dtlsCertificateThumbprint : null;

            webRtcSession.OnSdpOfferReady += (sdp) => { logger.LogDebug("Offer SDP: " + sdp); };
            //webRtcSession.OnMediaPacket += webRtcSession.MediaPacketReceived;
            webRtcSession.Initialise(null);
            webRtcSession.OnClose += (reason) => { logger.LogDebug($"WebRTCSession was closed with reason {reason}."); PeerClosed(null); };
        }

        private void PeerClosed(string callID)
        {
            try
            {
                logger.LogDebug("WebRTC session for closed for call ID " + callID + ".");

                WebRtcSession closedSession = null;

                if (!_webRtcSessions.TryRemove(callID, out closedSession))
                {
                    logger.LogError("Failed to remove closed WebRTC session from dictionary.");
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebRTCDaemon.PeerClosed. " + excp);
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be ommitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }

        private static void SendTestPattern()
        {
            try
            {
                unsafe
                {
                    Bitmap testPattern = new Bitmap(TEST_PATTERN_IMAGE_PATH);

                    // Get the stride.
                    Rectangle rect = new Rectangle(0, 0, testPattern.Width, testPattern.Height);
                    System.Drawing.Imaging.BitmapData bmpData =
                        testPattern.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadWrite,
                        testPattern.PixelFormat);

                    // Get the address of the first line.
                    int stride = bmpData.Stride;

                    testPattern.UnlockBits(bmpData);

                    // Initialise the video codec and color converter.
                    SIPSorceryMedia.VPXEncoder vpxEncoder = new VPXEncoder();
                    vpxEncoder.InitEncoder((uint)testPattern.Width, (uint)testPattern.Height, (uint)stride);

                    SIPSorceryMedia.ImageConvert colorConverter = new ImageConvert();

                    byte[] sampleBuffer = null;
                    byte[] encodedBuffer = null;
                    int sampleCount = 0;
                    uint rtpTimestamp = 0;

                    while (!_exit)
                    {
                        if (_webRtcSessions.Any(x => (x.Value.IsDtlsNegotiationComplete == true || x.Value.IsEncryptionDisabled == true) &&
                             x.Value.IsConnected))
                        {
                            var stampedTestPattern = testPattern.Clone() as System.Drawing.Image;
                            AddTimeStampAndLocation(stampedTestPattern, DateTime.UtcNow.ToString("dd MMM yyyy HH:mm:ss:fff"), "Test Pattern");
                            sampleBuffer = BitmapToRGB24(stampedTestPattern as System.Drawing.Bitmap);

                            fixed (byte* p = sampleBuffer)
                            {
                                byte[] convertedFrame = null;
                                colorConverter.ConvertRGBtoYUV(p, VideoSubTypesEnum.BGR24, testPattern.Width, testPattern.Height, stride, VideoSubTypesEnum.I420, ref convertedFrame);

                                fixed (byte* q = convertedFrame)
                                {
                                    int encodeResult = vpxEncoder.Encode(q, convertedFrame.Length, 1, ref encodedBuffer);

                                    if (encodeResult != 0)
                                    {
                                        logger.LogWarning("VPX encode of video sample failed.");
                                        continue;
                                    }
                                }
                            }

                            stampedTestPattern.Dispose();
                            stampedTestPattern = null;

                            lock (_webRtcSessions)
                            {
                                foreach (var session in _webRtcSessions.Where(x => (x.Value.IsDtlsNegotiationComplete == true || x.Value.IsEncryptionDisabled == true) &&
                                        x.Value.IsConnected))
                                {
                                    try
                                    {
                                        session.Value.SendMedia(SDPMediaTypesEnum.video, rtpTimestamp, encodedBuffer);
                                    }
                                    catch (Exception sendExcp)
                                    {
                                        logger.LogWarning("Exception SendTestPattern.SendMedia. " + sendExcp.Message);
                                        session.Value.Close();
                                    }
                                }
                            }

                            encodedBuffer = null;

                            sampleCount++;
                            rtpTimestamp += VP8_TIMESTAMP_SPACING;
                        }

                        Thread.Sleep(30);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SendTestPattern. " + excp);
            }
        }

        private static byte[] BitmapToRGB24(Bitmap bitmap)
        {
            try
            {
                BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                var length = bitmapData.Stride * bitmapData.Height;

                byte[] bytes = new byte[length];

                // Copy bitmap to byte[]
                Marshal.Copy(bitmapData.Scan0, bytes, 0, length);
                bitmap.UnlockBits(bitmapData);

                return bytes;
            }
            catch (Exception)
            {
                return new byte[0];
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
