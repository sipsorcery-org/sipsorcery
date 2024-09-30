﻿//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program to play pre-recorded sound files on a SIP
// call.
//
// Note: See the TextToPcm example in the AzureExamples for a demonstration
// of how to generate suitable audio files.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 03 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;

namespace demo
{
    class Program
    {
        private static string DESTINATION = "aaron@127.0.0.1:5080";
        private static SIPEndPoint OUTBOUND_PROXY = null;

        private const string WELCOME_8K = "Sounds/hellowelcome8k.raw";
        private const string GOODBYE_16K = "Sounds/goodbye16k.raw";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Play Sounds Demo");

            AddConsoleLogger();
            CancellationTokenSource exitCts = new CancellationTokenSource();

            var sipTransport = new SIPTransport();
            sipTransport.EnableTraceLogs();

            var userAgent = new SIPUserAgent(sipTransport, OUTBOUND_PROXY);
            userAgent.ClientCallFailed += (uac, error, sipResponse) => Console.WriteLine($"Call failed {error}.");
            userAgent.ClientCallFailed += (uac, error, sipResponse) => exitCts.Cancel();
            userAgent.OnCallHungup += (dialog) => exitCts.Cancel();

            var windowsAudio = new WindowsAudioEndPoint(new AudioEncoder());
            //windowsAudio.RestrictFormats(format => format.Codec == AudioCodecsEnum.G722);
            var voipMediaSession = new VoIPMediaSession(windowsAudio.ToMediaEndPoints());
            voipMediaSession.AcceptRtpFromAny = true;
            //voipMediaSession.AudioExtrasSource.AudioSamplePeriodMilliseconds = 20;
            //voipMediaSession.AudioLocalTrack.Capabilities.Clear();
            //voipMediaSession.AudioLocalTrack.Capabilities.Add(
            //    new SDPAudioVideoMediaFormat(new AudioFormat(AudioCodecsEnum.L16, 118, 8000)));

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                if (userAgent != null)
                {
                    if (userAgent.IsCalling || userAgent.IsRinging)
                    {
                        Console.WriteLine("Cancelling in progress call.");
                        userAgent.Cancel();
                    }
                    else if (userAgent.IsCallActive)
                    {
                        Console.WriteLine("Hanging up established call.");
                        userAgent.Hangup();
                    }
                };

                exitCts.Cancel();
            };

            // Place the call and wait for the result.
            var callTask = userAgent.Call(DESTINATION, null, null, voipMediaSession);

            Console.WriteLine("press ctrl-c to exit...");

            bool callResult = await callTask;

            if (callResult)
            {
                Console.WriteLine($"Call to {DESTINATION} succeeded.");

                await windowsAudio.PauseAudio();
                try
                {
                    await voipMediaSession.AudioExtrasSource.StartAudio();

                    //Console.WriteLine("Sending welcome message from 8KHz sample.");
                    await voipMediaSession.AudioExtrasSource.SendAudioFromStream(new FileStream(WELCOME_8K, FileMode.Open), AudioSamplingRatesEnum.Rate8KHz);

                    await Task.Delay(200, exitCts.Token);

                    Console.WriteLine("Sending sine wave.");
                    voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.SineWave);

                    await Task.Delay(5000, exitCts.Token);

                    Console.WriteLine("Sending white noise signal.");
                    voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.WhiteNoise);
                    await Task.Delay(2000, exitCts.Token);

                    Console.WriteLine("Sending pink noise signal.");
                    voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.PinkNoise);
                    await Task.Delay(2000, exitCts.Token);

                    Console.WriteLine("Sending silence.");
                    voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.Silence);

                    await Task.Delay(2000, exitCts.Token);

                    Console.WriteLine("Playing music.");
                    voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.Music);

                    await Task.Delay(5000, exitCts.Token);

                    Console.WriteLine("Sending goodbye message from 16KHz sample.");
                    await voipMediaSession.AudioExtrasSource.SendAudioFromStream(new FileStream(GOODBYE_16K, FileMode.Open), AudioSamplingRatesEnum.Rate16KHz);

                    voipMediaSession.AudioExtrasSource.SetSource(AudioSourcesEnum.None);

                    await voipMediaSession.AudioExtrasSource.PauseAudio();

                    await Task.Delay(200, exitCts.Token);
                }
                catch (TaskCanceledException)
                { }

                // Switch to the external microphone input source.
                await windowsAudio.ResumeAudio();

                exitCts.Token.WaitHandle.WaitOne();
            }
            else
            {
                Console.WriteLine($"Call to {DESTINATION} failed.");
            }

            Console.WriteLine("Exiting...");

            if (userAgent?.IsHangingUp == true)
            {
                Console.WriteLine("Waiting 1s for the call hangup or cancel to complete...");
                await Task.Delay(1000);
            }

            // Clean up.
            sipTransport.Shutdown();
        }

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
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
