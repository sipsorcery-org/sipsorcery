//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the
// SIPSorcery library to place a video call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 21 Feb 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Encoders;
using SIPSorceryMedia.FFmpeg;
using SIPSorceryMedia.Windows;

namespace demo
{
    class Program
    {
        private static string DESTINATION = "aaron@127.0.0.1:6060"; //"127.0.0.1:5060"; //"aaron@172.19.16.1:7060";
        private static int CALL_TIMEOUT_SECONDS = 20;
        private static int VIDEO_FRAME_WIDTH = 640;
        private static int VIDEO_FRAME_HEIGHT = 480;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static SIPTransport _sipTransport;
        private static Form _form;
        private static PictureBox _remoteVideoPicBox;
        private static PictureBox _localVideoPicBox;

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Video Call Demo");
            Console.WriteLine("Press ctrl-c to exit.");

            Log = AddConsoleLogger();
            ManualResetEvent exitMRE = new ManualResetEvent(false);

            _sipTransport = new SIPTransport();

            EnableTraceLogs(_sipTransport);

            // Open a window to display the video feed from the remote SIP party.
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

            Application.EnableVisualStyles();
            ThreadPool.QueueUserWorkItem(delegate { Application.Run(_form); });

            ManualResetEvent formMre = new ManualResetEvent(false);
            _form.Activated += (object sender, EventArgs e) => formMre.Set();

            Console.WriteLine("Waiting for form activation.");
            formMre.WaitOne();

            _sipTransport.SIPTransportRequestReceived += OnSIPTransportRequestReceived;

            string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            var userAgent = new SIPUserAgent(_sipTransport, null);
            userAgent.OnCallHungup += (dialog) => exitMRE.Set();
            var windowsAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder());
            windowsAudioEndPoint.RestrictFormats(format => format.Codec == AudioCodecsEnum.PCMU);
            //var windowsVideoEndPoint = new WindowsVideoEndPoint(new FFmpegVideoEncoder());
            var windowsVideoEndPoint = new WindowsVideoEndPoint(new VpxVideoEncoder());
            windowsVideoEndPoint.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);

            //windowsVideoEndPoint.OnVideoSourceError += (err) =>
            //{
            //    Log.LogError($"Video source error. {err}");
            //    if (userAgent.IsCallActive)
            //    {
            //        userAgent.Hangup();
            //    }
            //};

            // Fallback to a test pattern source if accessing the Windows webcam fails.
            var testPattern = new VideoTestPatternSource(new VpxVideoEncoder());

            MediaEndPoints mediaEndPoints = new MediaEndPoints
            {
                AudioSink = windowsAudioEndPoint,
                AudioSource = windowsAudioEndPoint,
                VideoSink = windowsVideoEndPoint,
                VideoSource = windowsVideoEndPoint,
            };

            var voipMediaSession = new VoIPMediaSession(mediaEndPoints, testPattern);
            voipMediaSession.AcceptRtpFromAny = true;

            windowsVideoEndPoint.OnVideoSourceRawSample += (uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat) =>
            {
                _form?.BeginInvoke(new Action(() =>
                {
                    unsafe
                    {
                        fixed (byte* s = sample)
                        {
                            System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap((int)width, (int)height, (int)width * 3, System.Drawing.Imaging.PixelFormat.Format24bppRgb, (IntPtr)s);
                            _localVideoPicBox.Image = bmpImage;
                        }
                    }
                }));
            };

            Console.WriteLine("Starting local video source...");
            await windowsVideoEndPoint.StartVideo().ConfigureAwait(false);

            // Place the call and wait for the result.
            Task<bool> callTask = userAgent.Call(DESTINATION, null, null, voipMediaSession);
            callTask.Wait(CALL_TIMEOUT_SECONDS * 1000);

            if (callTask.Result)
            {
                Log.LogInformation("Call attempt successful.");
                windowsVideoEndPoint.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride, VideoPixelFormatsEnum pixelFormat) =>
                {
                    _form?.BeginInvoke(new Action(() =>
                    {
                        unsafe
                        {
                            fixed (byte* s = bmp)
                            {
                                System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap((int)width, (int)height, stride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, (IntPtr)s);
                                _remoteVideoPicBox.Image = bmpImage;
                            }
                        }
                    }));
                };

                windowsAudioEndPoint.PauseAudio().Wait();
                voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.Music);
            }
            else
            {
                Log.LogWarning("Call attempt failed.");
                Console.WriteLine("Press ctrl-c to exit.");
            }

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                Log.LogInformation("Exiting...");
                exitMRE.Set();
            };
            exitMRE.WaitOne();

            if (userAgent.IsCallActive)
            {
                Log.LogInformation("Hanging up.");
                userAgent.Hangup();

                Task.Delay(1000).Wait();
            }

            // Clean up.
            _form.BeginInvoke(new Action(() => _form.Close()));
            _sipTransport.Shutdown();
        }

        private static Task OnSIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if (sipRequest.Method == SIPMethodsEnum.INFO)
            {
                var notImplResp = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotImplemented, null);
                return _sipTransport.SendResponseAsync(notImplResp);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response sent: {localEP}->{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Log.LogDebug($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Log.LogDebug($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
        }

        /// <summary>
        /// Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
