//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A getting started program to demonstrate how to use the SIPSorcery
// library to place a call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
// 31 Dec 2019  Aaron Clauson   Changed from an OPTIONS example to a call example.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using NAudio.Wave;
using Serilog;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Net;

namespace demo
{
    class Program
    {
        private static WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);  // PCMU format used by both input and output streams.
        private static int INPUT_SAMPLE_PERIOD_MILLISECONDS = 20;           // This sets the frequency of the RTP packets.
        private static string DESTINATION = "time@sipsorcery.com";

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery Getting Started Demo");

            AddConsoleLogger();

            var sipTransport = new SIPTransport();
            var userAgent = new SIPUserAgent(sipTransport, null);
            var rtpSession = new RTPMediaSession(SDPMediaTypesEnum.audio, (int)SDPMediaFormatsEnum.PCMU, AddressFamily.InterNetwork);

            // Connect audio devices to RTP session.
            WaveInEvent microphone = GetAudioInputDevice();
            var speaker = GetAudioOutputDevice();
            ConnectAudioDevicesToRtp(rtpSession, microphone, speaker);

            // Place the call and wait for the result.
            bool callResult = await userAgent.Call(DESTINATION, null, null, rtpSession);

            if(callResult)
            {
                Console.WriteLine("Call attempt successful.");
                microphone.StartRecording();
            }
            else
            {
                Console.WriteLine("Call attempt failed.");
            }
          
            Console.WriteLine("press any key to exit...");
            Console.Read();

            if (userAgent.IsCallActive)
            {
                Console.WriteLine("Hanging up.");
                userAgent.Hangup();
            }

            // Clean up.
            microphone.StopRecording();
            sipTransport.Shutdown();
            SIPSorcery.Net.DNSManager.Stop();
        }

        /// <summary>
        /// Connects the RTP packets we receive to the speaker and sends RTP packets for microphone samples.
        /// </summary>
        /// <param name="rtpSession">The RTP session to use for sending and receiving.</param>
        /// <param name="microphone">The default system  audio input device found.</param>
        /// <param name="speaker">The default system audio output device.</param>
        private static void ConnectAudioDevicesToRtp(RTPMediaSession rtpSession, WaveInEvent microphone, BufferedWaveProvider speaker)
        {
            // Wire up the RTP send session to the audio input device.
            uint rtpSendTimestamp = 0;
            microphone.DataAvailable += (object sender, WaveInEventArgs args) =>
            {
                byte[] sample = new byte[args.Buffer.Length / 2];
                int sampleIndex = 0;

                for (int index = 0; index < args.BytesRecorded; index += 2)
                {
                    var ulawByte = NAudio.Codecs.MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(args.Buffer, index));
                    sample[sampleIndex++] = ulawByte;
                }

                if (rtpSession.DestinationEndPoint != null)
                {
                    rtpSession.SendAudioFrame(rtpSendTimestamp, sample);
                    rtpSendTimestamp += (uint)(8000 / microphone.BufferMilliseconds);
                }
            };

            // Wire up the RTP receive session to the audio output device.
            rtpSession.OnRtpPacketReceived += (rtpPacket) =>
            {
                var sample = rtpPacket.Payload;
                for (int index = 0; index < sample.Length; index++)
                {
                    short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                    byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                    speaker.AddSamples(pcmSample, 0, 2);
                }
            };
        }

        /// <summary>
        /// Get the audio output device, e.g. speaker.
        /// Note that NAudio.Wave.WaveOut is not available for .Net Standard so no easy way to check if 
        /// there's a speaker.
        /// </summary>
        private static BufferedWaveProvider GetAudioOutputDevice()
        {
            WaveOutEvent waveOutEvent = new WaveOutEvent();
            var waveProvider = new BufferedWaveProvider(_waveFormat);
            waveProvider.DiscardOnBufferOverflow = true;
            waveOutEvent.Init(waveProvider);
            waveOutEvent.Play();

            return waveProvider;
        }

        /// <summary>
        /// Get the audio input device, e.g. microphone. The input device that will provide 
        /// audio samples that can be encoded, packaged into RTP and sent to the remote call party.
        /// </summary>
        private static WaveInEvent GetAudioInputDevice()
        {
            if (WaveInEvent.DeviceCount == 0)
            {
                throw new ApplicationException("No audio input devices available. No audio will be sent.");
            }
            else
            {
                WaveInEvent waveInEvent = new WaveInEvent();
                WaveFormat waveFormat = _waveFormat;
                waveInEvent.BufferMilliseconds = INPUT_SAMPLE_PERIOD_MILLISECONDS;
                waveInEvent.NumberOfBuffers = 1;
                waveInEvent.DeviceNumber = 0;
                waveInEvent.WaveFormat = waveFormat;

                return waveInEvent;
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
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Information)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}
