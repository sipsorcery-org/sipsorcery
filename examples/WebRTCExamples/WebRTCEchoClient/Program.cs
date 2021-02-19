//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A WebRTC peer that acts as a peer for an WebRTC echo server. 
// The echo server is a peer that reflects any media sent to it.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 10 Feb 2021	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;

namespace demo
{
    class Program
    {
        //private const string SIGNALING_SERVER = "https://sipsorcery.cloud/janus/echo/offer";
        //private const string SIGNALING_SERVER = "https://sipsorcery.cloud/sipsorcery/echo/offer";
        //private const string SIGNALING_SERVER = "https://sipsorcery.cloud/aiortc/echo/offer";

        //private const string SIGNALING_SERVER = "http://localhost:5002/offer";
        private const string SIGNALING_SERVER = "http://localhost:8080/offer";

        private static int VIDEO_FRAME_WIDTH = 640;
        private static int VIDEO_FRAME_HEIGHT = 480;

        private static Microsoft.Extensions.Logging.ILogger logger = null;

        private static Form _form;
        private static PictureBox _sourceVideoPicBox;
        private static PictureBox _echoVideoPicBox;
        private static bool _isFormActivated;

        static async Task Main()
        {
            Console.WriteLine("WebRTC Echo Test Client");

            logger = AddConsoleLogger();

            CancellationTokenSource cts = new CancellationTokenSource();

            #region Set up a simple Windows Form with two picture boxes. 

            _form = new Form();
            _form.AutoSize = true;
            _form.BackgroundImageLayout = ImageLayout.Center;
            _sourceVideoPicBox = new PictureBox
            {
                Size = new Size(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT),
                Location = new Point(0, 0),
                Visible = true
            };
            _echoVideoPicBox = new PictureBox
            {
                Size = new Size(VIDEO_FRAME_WIDTH, VIDEO_FRAME_HEIGHT),
                Location = new Point(0, VIDEO_FRAME_HEIGHT),
                Visible = true
            };
            _form.Controls.Add(_sourceVideoPicBox);
            _form.Controls.Add(_echoVideoPicBox);

            Application.EnableVisualStyles();
            ThreadPool.QueueUserWorkItem(delegate { Application.Run(_form); });
            _form.FormClosing += (sender, e) => _isFormActivated = false;
            _form.Activated += (sender, e) => _isFormActivated = true;
            //_form.FormClosed += (sender, e) => // TODO.

            #endregion

            // Video sink and source to generate and consume VP8 video streams.
            var testPattern = new VideoTestPatternSource(new VpxVideoEncoder());
            var vp8VideoSink = new VideoEncoderEndPoint();

            #region Connect the video frames generated from the sink and source to the Windows form.

            testPattern.OnVideoSourceRawSample += (uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (_isFormActivated)
                {
                    _form?.BeginInvoke(new Action(() =>
                    {
                        if (_form.Handle != IntPtr.Zero)
                        {
                            unsafe
                            {
                                fixed (byte* s = sample)
                                {
                                    var bmpImage = new Bitmap(width, height, width * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, (IntPtr)s);
                                    _sourceVideoPicBox.Image = bmpImage;
                                }
                            }
                        }
                    }));
                }
            };

            vp8VideoSink.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
            {
                if (_isFormActivated)
                {
                    _form?.BeginInvoke(new Action(() =>
                    {
                        if (_form.Handle != IntPtr.Zero)
                        {
                            unsafe
                            {
                                fixed (byte* s = bmp)
                                {
                                    var bmpImage = new Bitmap((int)width, (int)height, stride, PixelFormat.Format24bppRgb, (IntPtr)s);
                                    _echoVideoPicBox.Image = bmpImage;
                                }
                            }
                        }
                    }));
                }
            };

            #endregion

            await testPattern.StartVideo().ConfigureAwait(false);

            var pc = await CreatePeerConnection(testPattern, vp8VideoSink).ConfigureAwait(false);

            Console.WriteLine($"Sending offer to {SIGNALING_SERVER}.");

            var signaler = new HttpClient();

            var offer = pc.createOffer(null);
            await pc.setLocalDescription(offer).ConfigureAwait(false);

            var content = new StringContent(offer.toJSON(), Encoding.UTF8, "application/json");
            var response = await signaler.PostAsync($"{SIGNALING_SERVER}", content).ConfigureAwait(false);
            var answerStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (RTCSessionDescriptionInit.TryParse(answerStr, out var answerInit))
            {
                var setAnswerResult = pc.setRemoteDescription(answerInit);
                if (setAnswerResult != SetDescriptionResultEnum.OK)
                {
                    Console.WriteLine($"Set remote description failed {setAnswerResult}.");
                }
            }
            else
            {
                Console.WriteLine("Failed to parse SDP answer from signaling server.");
            }

            Console.WriteLine("Press any key to exit.");
            Console.ReadLine();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection(IVideoSource videoSource, IVideoSink videoSink)
        {
            var pc = new RTCPeerConnection(null);

            MediaStreamTrack videoTrack = new MediaStreamTrack(videoSink.GetVideoSinkFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(videoTrack);

            videoSource.OnVideoSourceEncodedSample += pc.SendVideo;
            pc.OnVideoFrameReceived += videoSink.GotVideoFrame;
            pc.OnVideoFormatsNegotiated += (formats) =>
            {
                videoSink.SetVideoSinkFormat(formats.First());
                videoSource.SetVideoSourceFormat(formats.First());
            };

            pc.OnTimeout += (mediaType) => logger.LogDebug($"Timeout on media {mediaType}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state changed to {state}.");
            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection connected changed to {state}.");

                if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed)
                {
                    await videoSource.CloseVideo().ConfigureAwait(false);
                }
            };

            return Task.FromResult(pc);
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
