//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPSorcery
// library to place a video call.
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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace demo
{
    class Program
    {
        private const string AUDIO_FILE_PCMU = @"media\Macroform_-_Simplicity.ulaw";
        private static string DESTINATION = "aaron@172.19.16.1:7060";

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static SIPTransport _sipTransport;

        private static Form _form;
        private static PictureBox _picBox;

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Video Call Demo");
            Console.WriteLine("Press ctrl-c to exit.");

            AddConsoleLogger();

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
            _ = Task.Run(() => Application.Run(_form));

            _sipTransport.SIPTransportRequestReceived += OnSIPTransportRequestReceived;

            var userAgent = new SIPUserAgent(_sipTransport, null);
            var audioSrcOpts = new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music, SourceFile = AUDIO_FILE_PCMU };
            //var audioSrcOpts = new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence };
            var videoSrcOpts = new VideoSourceOptions { VideoSource = VideoSourcesEnum.TestPattern };
            var rtpSession = new RtpAVSession(AddressFamily.InterNetwork, audioSrcOpts, videoSrcOpts, _picBox);

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession).ConfigureAwait(false);

            ManualResetEvent exitMRE = new ManualResetEvent(false);

            if (callResult)
            {
                // Start the audio capture and playback.
                rtpSession.Start();
                Log.LogInformation("Call attempt successful.");
            }
            else
            {
                Log.LogWarning("Call attempt failed.");
                exitMRE.Set();
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

                await Task.Delay(1000);
            }

            // Clean up.
            _form.BeginInvoke(new Action(() => _form.Close()));
            _sipTransport.Shutdown();
            SIPSorcery.Net.DNSManager.Stop();
        }

        private static Task OnSIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            if(sipRequest.Method == SIPMethodsEnum.INFO)
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
