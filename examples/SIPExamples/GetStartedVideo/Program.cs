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
                    unsafe
                    {
                        fixed (byte* s = sample)
                        {
                            _localVideoBmp = ShowFrame(_localVideoBmp, _form, _localVideoPicBox, s, width, height, width * 3);
                        }
                    }
                }
            };

            vp8VideoSink.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (_isFormActivated && _form.Handle != IntPtr.Zero)
                {
                    unsafe
                    {
                        fixed (byte* s = bmp)
                        {
                            _remoteVideoBmp = ShowFrame(_remoteVideoBmp, _form, _remoteVideoPicBox, s, (int)width, (int)height, stride);
                        }
                    }
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
        /// returning the bitmap so the caller can hold it for the next frame.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called on the decoder's thread, not the UI thread, and deliberately so. The sample is only
        /// valid for the duration of the event handler: the decoder converts every frame into the same
        /// buffer, so deferring the copy to the UI thread would read a frame that has already been
        /// overwritten. Only the display of the finished bitmap is marshalled to the form.
        /// </para>
        /// <para>
        /// The bitmap is allocated once and re-used until the frame size changes. Allocating one per
        /// frame costs several times more than the copy itself and, at 1080p and above, produces enough
        /// garbage to stall the application.
        /// </para>
        /// <para>
        /// Note GDI+'s Format24bppRgb is B,G,R in memory despite the name, which is why a Bgr sample
        /// can be copied into it verbatim.
        /// </para>
        /// </remarks>
        private static unsafe Bitmap ShowFrame(Bitmap displayBmp, Form form, PictureBox picBox, byte* sample, int width, int height, int stride)
        {
            if (displayBmp == null || displayBmp.Width != width || displayBmp.Height != height)
            {
                var previous = displayBmp;
                displayBmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                var created = displayBmp;

                form.BeginInvoke(new Action(() =>
                {
                    picBox.Width = width;
                    picBox.Height = height;
                    picBox.Image = created;
                    previous?.Dispose();
                }));
            }

            var bmpData = displayBmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

            try
            {
                // GDI+ rounds its stride up to a multiple of four while the decoder's is exactly
                // width * 3, so the two agree for any width that is a multiple of four. That covers
                // every standard video resolution and lets the frame go in a single copy, which
                // measures around 30% faster than a row at a time. Widths that are not a multiple
                // of four, which cropped display sizes can produce, still need the row loop.
                if (stride == bmpData.Stride)
                {
                    Buffer.MemoryCopy(sample, (byte*)bmpData.Scan0, (long)bmpData.Stride * height, (long)stride * height);
                }
                else
                {
                    for (int row = 0; row < height; row++)
                    {
                        Buffer.MemoryCopy(sample + row * stride, (byte*)bmpData.Scan0 + row * bmpData.Stride, bmpData.Stride, width * 3);
                    }
                }
            }
            finally
            {
                displayBmp.UnlockBits(bmpData);
            }

            // Control.Invalidate is not documented as thread safe, so marshal it like any other UI call.
            form.BeginInvoke(new Action(() => picBox.Invalidate()));

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
