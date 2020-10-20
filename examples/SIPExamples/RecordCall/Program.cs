//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Sample program of how to place and record a call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 26 Mar 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;
using SIPSorceryMedia.Windows;
using NAudio.Wave;

namespace demo
{
    class Program
    {
        private static string DESTINATION = "time@sipsorcery.com";
        //private static string DESTINATION = "*66@192.168.0.48";

        private static readonly WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);
        private static WaveFileWriter _waveFile;

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            AddConsoleLogger();

            _waveFile = new WaveFileWriter("output.mp3", _waveFormat);

            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            userAgent.ClientCallFailed += (uac, err, resp) =>
            {
                Console.WriteLine($"Call failed {err}");
                _waveFile?.Close();
            };
            userAgent.OnCallHungup += (dialog) => _waveFile?.Close();

            WindowsAudioEndPoint winAudioEP = new WindowsAudioEndPoint(new AudioEncoder());
            VoIPMediaSession voipSession = new VoIPMediaSession(winAudioEP.ToMediaEndPoints());
            voipSession.OnRtpPacketReceived += OnRtpPacketReceived;

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                if (userAgent.IsCallActive)
                {
                    Console.WriteLine("Hanging up.");
                    userAgent.Hangup();
                }
                else
                {
                    Console.WriteLine("Cancelling call");
                    userAgent.Cancel();
                }
            };

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, voipSession);
            Console.WriteLine($"Call result {((callResult) ? "success" : "failure")}.");

            Console.WriteLine("press any key to exit...");
            Console.Read();

            if (userAgent.IsCallActive)
            {
                Console.WriteLine("Hanging up.");
                userAgent.Hangup();
            }

            // Clean up.
            sipTransport.Shutdown();
        }

        private static void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (mediaType == SDPMediaTypesEnum.audio)
            {
                var sample = rtpPacket.Payload;

                for (int index = 0; index < sample.Length; index++)
                {
                    if (rtpPacket.Header.PayloadType == (int)SDPWellKnownMediaFormatsEnum.PCMA)
                    {
                        short pcm = NAudio.Codecs.ALawDecoder.ALawToLinearSample(sample[index]);
                        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                        _waveFile.Write(pcmSample, 0, 2);
                    }
                    else
                    {
                        short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                        byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                        _waveFile.Write(pcmSample, 0, 2);
                    }
                }
            }
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
