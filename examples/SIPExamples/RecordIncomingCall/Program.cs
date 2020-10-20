//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: Sample program of how to receive and incoming call and record it.
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using NAudio.Wave;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Windows;
using SIPSorceryMedia.Abstractions.V1;

namespace demo
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 5060;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static readonly WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);
        private static WaveFileWriter _waveFile;
        private static SIPTransport _sipTransport;

        static void Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            Log = AddConsoleLogger();

            _waveFile = new WaveFileWriter("output.mp3", _waveFormat);

            _sipTransport = new SIPTransport();
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            var userAgent = new SIPUserAgent(_sipTransport, null, true);
            userAgent.ServerCallCancelled += (uas) => Log.LogDebug("Incoming call cancelled by remote party.");
            userAgent.OnCallHungup += (dialog) => _waveFile?.Close();
            userAgent.OnIncomingCall += async (ua, req) =>
            {
                WindowsAudioEndPoint winAudioEP = new WindowsAudioEndPoint(new AudioEncoder());
                VoIPMediaSession voipMediaSession = new VoIPMediaSession(winAudioEP.ToMediaEndPoints());
                voipMediaSession.AcceptRtpFromAny = true;
                voipMediaSession.OnRtpPacketReceived += OnRtpPacketReceived;

                var uas = userAgent.AcceptCall(req);
                await userAgent.Answer(uas, voipMediaSession);
            };

            Console.WriteLine("press any key to exit...");
            Console.Read();

            // Clean up.
            _sipTransport.Shutdown();
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
