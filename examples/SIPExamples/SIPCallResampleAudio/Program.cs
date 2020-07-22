//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Example program to place a SIP call and convert the audio.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 04 Apr 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Serilog;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace demo
{
    class Program
    {
        private static string DESTINATION = "time@sipsorcery.com";

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static int OUT_SAMPLE_RATE = 48000;
        private static int RTP_SAMPLE_RATE = SDPMediaFormatInfo.GetClockRate(SDPMediaFormatsEnum.PCMU);

        private static WaveFormat _format_s16le48k = new WaveFormat(OUT_SAMPLE_RATE, 16, 1);
        private static WaveFileWriter _waveFile;
        private static double _ratio = (double)(OUT_SAMPLE_RATE / RTP_SAMPLE_RATE);

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Convert Audio");

            AddConsoleLogger();

            //WaveFormatConversionStream converter = new WaveFormatConversionStream(_format_s16le48k, )
            _waveFile = new WaveFileWriter("output_s16le48k.mp3", _format_s16le48k);

            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            userAgent.OnCallHungup += (dialog) =>
            {
                Console.WriteLine("Call hungup.");
                _waveFile?.Close();
            };

            //EnableTraceLogs(sipTransport);

            var audioOptions = new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence };
            var audioFormats = new List<SDPMediaFormatsEnum> { SDPMediaFormatsEnum.PCMU };
            var rtpSession = new RtpAudioSession(audioOptions, audioFormats);
            rtpSession.OnRtpPacketReceived += RtpSession_OnRtpPacketReceived;

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);
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

        private static void RtpSession_OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum kind, RTPPacket pkt)
        {
            //Log.LogDebug($"{kind} RTP packet received {pkt.Header.SequenceNumber}.");

            if (kind == SDPMediaTypesEnum.audio)
            {
                var sample = pkt.Payload;

                for (int index = 0; index < sample.Length; index++)
                {
                    short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                    float s16 = pcm / 32768f;

                    for (int i = 0; i < _ratio; i++)
                    {
                        _waveFile.WriteSample(s16);
                    }
                }
            }
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
