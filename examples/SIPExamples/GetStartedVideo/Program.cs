//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the
// SIPSorcery library to send and receive a video stream.
//
// This example uses a test pattern video source and has no audio. For a
// demo that is more like a video phone, and hence more complicated, 
// see the VideoPhoneCmdLine demo.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 21 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
// 02 Feb 2021  Aaron Clauson   Simplified by switching to video test pattern only.
// 30 Sep 2024  Aaron Clauson   Can't find a SIP echo server that supports video calls :(
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using Serilog.Events;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace demo
{
    class Program
    {
        // Need to find a new echo endpoint since the sipsorcery cloud was turned off to save $$. 14 Jan 2024.
        //private static string DESTINATION = "echo@iptel.org"; // Doesn't support video.
        //private static string DESTINATION = "3333@sip2sip.info"; // Doesn't support video.
        //private static string DESTINATION = "echo@linphone.org"; // Authentication required.
        //private static string DESTINATION = "echo@onsip.com"; // Not found.
        private static string DESTINATION = "???";
        private static int VIDEO_FRAME_WIDTH = 640;
        private static int VIDEO_FRAME_HEIGHT = 480;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static Form _form;
        private static PictureBox _remoteVideoPicBox;
        private static PictureBox _localVideoPicBox;
        private static Bitmap _remoteVideoBmp;
        private static Bitmap _localVideoBmp;
        private static bool _isFormActivated;

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Video Call Demo");
            Console.WriteLine("Press ctrl-c to exit.");

            Log = AddConsoleLogger();
            ManualResetEvent exitMRE = new ManualResetEvent(false);

            var sipTransport = new SIPTransport();
            sipTransport.EnableTraceLogs();
            var userAgent = new SIPUserAgent(sipTransport, null, true);

            #region Set up a simple Windows Form with two picture boxes. 

            _form = new Form();
            _form.AutoSize = true;
            _form.BackgroundImageLayout = ImageLayout.Center;
            _localVideoPicBox = new PictureBox
            {
                Size = new Size(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT),
                Location = new Point(0, 0),
                Visible = true
            };
            _remoteVideoPicBox = new PictureBox
            {
                Size = new Size(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT),
                Location = new Point(0, VIDEO_FRAME_HEIGHT),
                Visible = true
            };
            _form.Controls.Add(_localVideoPicBox);
            _form.Controls.Add(_remoteVideoPicBox);

            #endregion

            Application.EnableVisualStyles();
            ThreadPool.QueueUserWorkItem(delegate { Application.Run(_form); });
            _form.FormClosing += (sender, e) => _isFormActivated = false;
            _form.Activated += (sender, e) => _isFormActivated = true;
            _form.FormClosed += (sender, e) => userAgent.Hangup();
            userAgent.OnCallHungup += (dialog) =>
            {
                if (_isFormActivated) { _form.Close(); }
            };

            // Video sink and source to generate and consume VP8 video streams.
            var testPattern = new VideoTestPatternSource(new VpxVideoEncoder());
            var vp8VideoSink = new VideoEncoderEndPoint();

            // Add the video sink and source to the media session.
            MediaEndPoints mediaEndPoints = new MediaEndPoints
            {
                VideoSink = vp8VideoSink,
                VideoSource = testPattern,
            };
            var voipMediaSession = new VoIPMediaSession(mediaEndPoints);
            voipMediaSession.AcceptRtpFromAny = true;

            #region Connect the video frames generate from the sink and source to the Windows form.

            testPattern.OnVideoSourceRawSample += (uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (_isFormActivated && _form.Handle != IntPtr.Zero)
                {
                    _form.BeginInvoke(new Action(() =>
                        _localVideoBmp = ShowFrame(_localVideoBmp, _localVideoPicBox, sample, width, height, width * 3)));
                }
            };

            vp8VideoSink.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (_isFormActivated && _form.Handle != IntPtr.Zero)
                {
                    _form.BeginInvoke(new Action(() =>
                        _remoteVideoBmp = ShowFrame(_remoteVideoBmp, _remoteVideoPicBox, bmp, (int)width, (int)height, stride)));
                }
            };

            #endregion

            // Place the call.
            var callResult = await userAgent.Call(DESTINATION, null, null, voipMediaSession).ConfigureAwait(false);
            Console.WriteLine($"Call result {((callResult) ? "success" : "failure")}.");

            Console.WriteLine("Press any key to hangup and exit.");
            Console.ReadLine();

            if (userAgent.IsCallActive)
            {
                _isFormActivated = false;
                userAgent.Hangup();
                await Task.Delay(1000).ConfigureAwait(false);
            }

            sipTransport.Shutdown();
        }

        /// <summary>
        /// Copies a decoded 24 bits per pixel frame into a bitmap this application owns and displays it,
        /// returning the bitmap so the caller can re-use it for the next frame.
        /// </summary>
        /// <remarks>
        /// Runs on the UI thread. The byte[] overloads hand over a managed array, which unlike
        /// RawImage.Sample stays valid after the callback returns, so the frame can simply be
        /// marshalled across and copied here. Wrapping the array in a Bitmap instead would leave the
        /// picture box pointing at memory the GC is free to move or reclaim.
        ///
        /// The bitmap is allocated once and re-used until the frame size changes. Note GDI+'s
        /// Format24bppRgb is B,G,R in memory despite the name, which is why a Bgr sample copies in
        /// verbatim.
        /// </remarks>
        private static Bitmap ShowFrame(Bitmap displayBmp, PictureBox picBox, byte[] sample, int width, int height, int stride)
        {
            if (displayBmp == null || displayBmp.Width != width || displayBmp.Height != height)
            {
                var previous = displayBmp;
                displayBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);

                picBox.Width = width;
                picBox.Height = height;
                picBox.Image = displayBmp;
                previous?.Dispose();
            }

            var bmpData = displayBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                // GDI+ pads each row to a multiple of four bytes, so the strides only agree when the
                // width is a multiple of four. That covers every standard resolution.
                if (stride == bmpData.Stride)
                {
                    Marshal.Copy(sample, 0, bmpData.Scan0, stride * height);
                }
                else
                {
                    for (int row = 0; row < height; row++)
                    {
                        Marshal.Copy(sample, row * stride, bmpData.Scan0 + row * bmpData.Stride, width * 3);
                    }
                }
            }
            finally
            {
                displayBmp.UnlockBits(bmpData);
            }

            picBox.Invalidate();

            return displayBmp;
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger(
            LogEventLevel logLevel = LogEventLevel.Debug)
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(logLevel)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
