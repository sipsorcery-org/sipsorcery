//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example of how top send DTMF tones in band (with specific RTP
// packets) as specified in RFC2833.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Nov 2019	Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Note on call flow and destinations being used in this sample.
//
// This example calls a destination (represented by DEFAULT_DESTINATION_SIP_URI)
// and expects a SIP agent capable of receiving DTMF with RFC2833 to be listening.
// What the receiving SIP does with the received DTMF is up to it. A good example
// is to playback the presses via speech synthesis. The dialplan below is an
// example of how to do that with Asterisk.
//
// Example Aterisk dialplan snippet to repeat back any DTMF tones received:
//
// exten => *63,1(start),Gotoif($[ "${LEN(${extensao})}" < "3"]?collect:bye)
// exten => *63,n(collect),Read(digito,,1)
// exten => *63,n,SayDigits(${digito})
// exten => *63,n,Set(extensao=${extensao}${digito})
// exten => *63,n,GoTo(start)
// exten => *63,n(bye),Playback("vm-goodbye")
// exten => *63,n,hangup()
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    class Program
    {
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:*63@192.168.11.48";   // Custom Asterisk dialplan to speak back DTMF tones.

        private static WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);  // PCMU format used by both input and output streams.

        /// <summary>
        /// PCMU encoding for silence, http://what-when-how.com/voip/g-711-compression-voip/
        /// </summary>
        private static readonly byte PCMU_SILENCE_BYTE_ZERO = 0x7F;
        private static readonly byte PCMU_SILENCE_BYTE_ONE = 0xFF;
        private static readonly int SILENCE_SAMPLE_PERIOD = 50; // In milliseconds (PCM is 64kbit/s).

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main()
        {
            Console.WriteLine("SIPSorcery client user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource rtpCts = new CancellationTokenSource(); // Cancellation token to stop the RTP stream.
            bool isCallHungup = false;
            bool hasCallFailed = false;

            AddConsoleLogger();

            SIPURI callUri = SIPURI.ParseSIPURI(DEFAULT_DESTINATION_SIP_URI);
            Log.LogInformation($"Call destination {callUri}.");

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

            // Un/comment this line to see/hide each SIP message sent and received.
            EnableTraceLogs(sipTransport);

            // Note this relies on the callURI host being an IP address. If it's a hostname a DNS lookup is required.
            IPAddress localIPAddress = NetServices.GetLocalAddressForRemote(callUri.ToSIPEndPoint().Address);

            // Initialise an RTP session to receive the RTP packets from the remote SIP server.
            var rtpSession = new RTPSession((int)SDPMediaFormatsEnum.PCMU, null, null, true, localIPAddress.AddressFamily);
            var offerSDP = rtpSession.GetSDP(localIPAddress);

            // Create a client user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
            var uac = new SIPClientUserAgent(sipTransport);

            uac.CallTrying += (uac, resp) =>
            {
                Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
            };
            uac.CallRinging += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
            uac.CallFailed += (uac, err) =>
            {
                Log.LogWarning($"{uac.CallDescriptor.To} Failed: {err}");
                hasCallFailed = true;
            };
            uac.CallAnswered += (uac, resp) =>
            {
                if (resp.Status == SIPResponseStatusCodesEnum.Ok)
                {
                    Log.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                    rtpSession.DestinationEndPoint = SDP.GetSDPRTPEndPoint(resp.Body);
                    Log.LogDebug($"Remote RTP socket {rtpSession.DestinationEndPoint}.");
                }
                else
                {
                    Log.LogWarning($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                }
            };

            // The only incoming request that needs to be explicitly handled for this example is if the remote end hangs up the call.
            sipTransport.SIPTransportRequestReceived += async (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPResponse okResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    await sipTransport.SendResponseAsync(okResponse);

                    if (uac.IsUACAnswered)
                    {
                        Log.LogInformation("Call was hungup by remote server.");
                        isCallHungup = true;
                        rtpCts.Cancel();
                    }
                }
            };

            // Wire up the RTP receive session to the default speaker.
            var (audioOutEvent, audioOutProvider) = GetAudioOutputDevice();
            rtpSession.OnReceivedSampleReady += (sample) => 
            {
                for (int index = 0; index < sample.Length; index++)
                {
                    short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                    byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                    audioOutProvider.AddSamples(pcmSample, 0, 2);
                }
            };

            // Send audio packets (in this case silence) to the callee.
            Task.Run(() => SendSilence(rtpSession, rtpCts));

            // Start the thread that places the call.
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
                SIPConstants.SIP_DEFAULT_USERNAME,
                null,
                callUri.ToString(),
                SIPConstants.SIP_DEFAULT_FROMURI,
                null, null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                offerSDP.ToString(),
                null);

            uac.Call(callDescriptor);

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                rtpCts.Cancel();
            };

            // Give the call some time to answer.
            Task.Delay(3000).Wait();

            // Send some DTMF key presses via RTP events.
            var dtmf5 = new RTPEvent(0x05, false, RTPEvent.DEFAULT_VOLUME, 1200, RTPSession.DTMF_EVENT_PAYLOAD_ID);
            rtpSession.SendDtmfEvent(dtmf5, rtpCts).Wait();
            Task.Delay(2000, rtpCts.Token).Wait();

            var dtmf9 = new RTPEvent(0x09, false, RTPEvent.DEFAULT_VOLUME, 1200, RTPSession.DTMF_EVENT_PAYLOAD_ID);
            rtpSession.SendDtmfEvent(dtmf9, rtpCts).Wait();
            Task.Delay(2000, rtpCts.Token).Wait();

            var dtmf2 = new RTPEvent(0x02, false, RTPEvent.DEFAULT_VOLUME, 1200, RTPSession.DTMF_EVENT_PAYLOAD_ID);
            rtpSession.SendDtmfEvent(dtmf2, rtpCts).Wait();
            Task.Delay(2000, rtpCts.Token).ContinueWith((task) => { }).Wait(); // Don't care about the exception if the cancellation token is set.

            Log.LogInformation("Exiting...");

            rtpCts.Cancel();
            audioOutEvent?.Stop();
            rtpSession.Close();

            if (!isCallHungup && uac != null)
            {
                if (uac.IsUACAnswered)
                {
                    Log.LogInformation($"Hanging up call to {uac.CallDescriptor.To}.");
                    uac.Hangup();
                }
                else if (!hasCallFailed)
                {
                    Log.LogInformation($"Cancelling call to {uac.CallDescriptor.To}.");
                    uac.Cancel();
                }

                // Give the BYE or CANCEL request time to be transmitted.
                Log.LogInformation("Waiting 1s for call to clean up...");
                Task.Delay(1000).Wait();
            }

            SIPSorcery.Net.DNSManager.Stop();

            if (sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                sipTransport.Shutdown();
            }
        }

        /// <summary>
        /// Get the audio output device, e.g. speaker.
        /// Note that NAUdio.Wave.WaveOut is not available for .Net Standard so no easy way to check if 
        /// there's a speaker.
        /// </summary>
        private static (WaveOutEvent, BufferedWaveProvider) GetAudioOutputDevice()
        {
            WaveOutEvent waveOutEvent = new WaveOutEvent();
            var waveProvider = new BufferedWaveProvider(_waveFormat);
            waveProvider.DiscardOnBufferOverflow = true;
            waveOutEvent.Init(waveProvider);
            waveOutEvent.Play();

            return (waveOutEvent, waveProvider);
        }

        /// <summary>
        /// Sends the sounds of silence. If the destination is on the other side of a NAT this is useful to open
        /// a pinhole and hopefully get the remote RTP stream through.
        /// </summary>
        /// <param name="rtpChannel">The RTP channel we're sending from.</param>
        /// <param name="rtpSendSession">Our RTP sending session.</param>
        /// <param name="cts">Cancellation token to stop the call.</param>
        private static async void SendSilence(RTPSession rtpSession, CancellationTokenSource cts)
        {
            int samplingFrequency = rtpSession.MediaFormat.GetClockRate();
            uint rtpTimestampStep = (uint)(samplingFrequency * SILENCE_SAMPLE_PERIOD / 1000);
            uint bufferSize = (uint)SILENCE_SAMPLE_PERIOD;
            uint rtpSampleTimestamp = 0;

            while (cts.IsCancellationRequested == false)
            {
                if (rtpSession.DestinationEndPoint != null)
                {
                    byte[] sample = new byte[bufferSize / 2];
                    int sampleIndex = 0;

                    for (int index = 0; index < bufferSize; index += 2)
                    {
                        sample[sampleIndex] = PCMU_SILENCE_BYTE_ZERO;
                        sample[sampleIndex + 1] = PCMU_SILENCE_BYTE_ONE;
                    }

                    rtpSession.SendAudioFrame(rtpSampleTimestamp, sample);
                    rtpSampleTimestamp += rtpTimestampStep;
                }

                await Task.Delay(SILENCE_SAMPLE_PERIOD);
            }
        }

        /// <summary>
        ///  Adds a console logger. Can be ommitted if internal SIPSorcery debug and warning messages are not required.
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
