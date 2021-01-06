//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example of how to use a custom audio codec.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Dec 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Windows;
using SIPSorceryMedia.Abstractions;
using GroovyCodecs.G729;

namespace demo
{
    class G729Codec : IAudioEncoder
    {
        private G729Decoder _g729Decoder;
        private G729Encoder _g729Encoder;

        private List<AudioFormat> _supportedFormats = new List<AudioFormat>
        {
            new AudioFormat(SDPWellKnownMediaFormatsEnum.G729)
        };

        public List<AudioFormat> SupportedFormats
        {
            get => _supportedFormats;
        }

        public G729Codec()
        {
            _g729Encoder = new G729Encoder();
            _g729Decoder = new G729Decoder();
        }

        public short[] DecodeAudio(byte[] encodedSample, AudioFormat format)
        {
            var pcm = _g729Decoder.Process(encodedSample);
            return pcm.Where((x, i) => i % 2 == 0).Select((y, i) => (short)(pcm[i * 2 + 1] << 8 | pcm[i * 2])).ToArray();
        }

        public byte[] EncodeAudio(short[] pcm, AudioFormat format)
        {
            return _g729Encoder.Process(pcm.SelectMany(x => new byte[] { (byte)(x), (byte)(x >> 8) }).ToArray());
        }
    }

    class Program
    {
        private static string DESTINATION = "time@sipsorcery.com";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Custom Audio Codec Demo");

            AddConsoleLogger();
            CancellationTokenSource exitCts = new CancellationTokenSource();

            var sipTransport = new SIPTransport();

            EnableTraceLogs(sipTransport);

            var userAgent = new SIPUserAgent(sipTransport, null);
            userAgent.ClientCallFailed += (uac, error, sipResponse) => Console.WriteLine($"Call failed {error}.");
            userAgent.OnCallHungup += (dialog) => exitCts.Cancel();

            var windowsAudio = new WindowsAudioEndPoint(new G729Codec());
            windowsAudio.RestrictFormats(x => x.Codec == AudioCodecsEnum.G729);
            var voipMediaSession = new VoIPMediaSession(windowsAudio.ToMediaEndPoints());
            voipMediaSession.AcceptRtpFromAny = true;

            // Place the call and wait for the result.
            var callTask = userAgent.Call(DESTINATION, null, null, voipMediaSession);

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

            Console.WriteLine("press ctrl-c to exit...");

            bool callResult = await callTask;

            if (callResult)
            {
                Console.WriteLine($"Call to {DESTINATION} succeeded.");
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
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Console.WriteLine($"Request received: {localEP}<-{remoteEP}");
                Console.WriteLine(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Console.WriteLine($"Request sent: {localEP}->{remoteEP}");
                Console.WriteLine(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Console.WriteLine($"Response received: {localEP}<-{remoteEP}");
                Console.WriteLine(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Console.WriteLine($"Response sent: {localEP}->{remoteEP}");
                Console.WriteLine(resp.ToString());
            };

            sipTransport.SIPRequestRetransmitTraceEvent += (tx, req, count) =>
            {
                Console.WriteLine($"Request retransmit {count} for request {req.StatusLine}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };

            sipTransport.SIPResponseRetransmitTraceEvent += (tx, resp, count) =>
            {
                Console.WriteLine($"Response retransmit {count} for response {resp.ShortDescription}, initial transmit {DateTime.Now.Subtract(tx.InitialTransmit).TotalSeconds.ToString("0.###")}s ago.");
            };
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
