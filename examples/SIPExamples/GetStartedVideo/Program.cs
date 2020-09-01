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
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Windows;

namespace demo
{
    class Program
    {
        private const string AUDIO_FILE_PCMU = "media/Macroform_-_Simplicity.ulaw";
        private const string VIDEO_TEST_PATTERN_FILE = "media/testpattern.jpeg";
        private static string DESTINATION = "aaron@127.0.0.1:6060"; //"127.0.0.1:5060"; //"aaron@172.19.16.1:7060";
        private static int CALL_TIMEOUT_SECONDS = 20;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static SIPTransport _sipTransport;
        private static Form _form;
        private static PictureBox _picBox;

        //static void Main()
        //{
        //    Console.WriteLine("SIPSorcery Getting Started Video Call Demo");
        //    Console.WriteLine("Press ctrl-c to exit.");

        //    AddConsoleLogger();

        //    Console.WriteLine($"VP8 encoder version {Vp8Codec.GetCodecVersion()}.");
        //    Console.WriteLine($"VP8 encoder version string {Vp8Codec.GetCodecVersionStr()}.");

        //    Vp8Codec vp8Codec = new Vp8Codec();
        //    vp8Codec.InitialiseEncoder(640, 480);

        //    byte[] dummyI420 = new byte[640 * 480 * 2];

        //    var encoded = vp8Codec.Encode(dummyI420);

        //    Console.WriteLine($"Encoded frame size {encoded.Length}.");

        //    vp8Codec.Dispose();

        //    Console.WriteLine("Finished.");
        //}

        static void Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Video Call Demo");
            Console.WriteLine("Press ctrl-c to exit.");

            AddConsoleLogger();

            WindowsVideoEndPoint.logger = Log;

            _sipTransport = new SIPTransport();

            EnableTraceLogs(_sipTransport);

            // Open a Window to display the video feed from the WebRTC peer.
            _form = new Form();
            _form.AutoSize = true;
            _form.BackgroundImageLayout = ImageLayout.Center;
            _picBox = new PictureBox
            {
                Size = new Size(640, 480),
                Location = new Point(0, 0),
                Visible = true
            };
            _form.Controls.Add(_picBox);

            Application.EnableVisualStyles();
            ThreadPool.QueueUserWorkItem(delegate { Application.Run(_form); });

            ManualResetEvent formMre = new ManualResetEvent(false);
            _form.Activated += (object sender, EventArgs e) => formMre.Set();

            Console.WriteLine("Waiting for form activation.");
            formMre.WaitOne();

            _sipTransport.SIPTransportRequestReceived += OnSIPTransportRequestReceived;

            string executableDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);

            var userAgent = new SIPUserAgent(_sipTransport, null);
            var audioSrcOpts = new AudioSourceOptions
            {
                AudioSource = AudioSourcesEnum.Music,
                SourceFiles = new Dictionary<AudioCodecsEnum, string>
                {
                    { AudioCodecsEnum.PCMU, executableDir + "/" + AUDIO_FILE_PCMU }
                }
            };

            var audioExtrasSource = new AudioExtrasSource(new AudioEncoder(), audioSrcOpts);
            var windowsAudioEndPoint = new WindowsAudioEndPoint(new AudioEncoder(), audioExtrasSource);
            var testPatternSource = new VideoTestPatternSource();
            var windowsVideoEndPoint = new WindowsVideoEndPoint(testPatternSource);

            MediaEndPoints mediaEndPoints = new MediaEndPoints
            {
                AudioSink = windowsAudioEndPoint,
                AudioSource = audioExtrasSource,
                VideoSink = windowsVideoEndPoint,
                VideoSource = windowsVideoEndPoint,
            };

            var voipMediaSession = new VoIPMediaSession(mediaEndPoints);
            voipMediaSession.AcceptRtpFromAny = true;

            // Place the call and wait for the result.
            Task<bool> callTask = userAgent.Call(DESTINATION, null, null, voipMediaSession);
            callTask.Wait(CALL_TIMEOUT_SECONDS * 1000);

            ManualResetEvent exitMRE = new ManualResetEvent(false);

            if (callTask.Result)
            {
                Log.LogInformation("Call attempt successful.");
                windowsVideoEndPoint.OnVideoSinkDecodedSample += (byte[] bmp, uint width, uint height, int stride) =>
                {
                    _picBox.BeginInvoke(new Action(() =>
                    {
                        unsafe
                        {
                            fixed (byte* s = bmp)
                            {
                                System.Drawing.Bitmap bmpImage = new System.Drawing.Bitmap((int)width, (int)height, stride, System.Drawing.Imaging.PixelFormat.Format24bppRgb, (IntPtr)s);
                                _picBox.Image = bmpImage;
                            }
                        }
                    }));
                };
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
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
    }
}
