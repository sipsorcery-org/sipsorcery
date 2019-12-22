//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// place or receive two calls and then bridge them together to accomplish an
// attended transfer.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Dec 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;

namespace SIPSorcery
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 5060;
        private static WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);  // PCMU format used by both input and output streams.
        private static int INPUT_SAMPLE_PERIOD_MILLISECONDS = 20;           // This sets the frequency of the RTP packets.
        private static int TRANSFER_TIMEOUT_SECONDS = 2; //10;               // Give up on transfer if no response within this period.

        // If set will mirror SIP packets to a Homer (sipcapture.org) logging and analysis server.
        private static string HOMER_SERVER_ADDRESS = "192.168.11.49";
        private static int HOMER_SERVER_PORT = 9060;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static BufferedWaveProvider m_audioOutProvider;

        static void Main()
        {
            Console.WriteLine("SIPSorcery call hold example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.

            AddConsoleLogger();

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            EnableTraceLogs(sipTransport);

            // Create two user agents. Each gets configured to answer an incoming call.
            var userAgent1 = new SIPUserAgent(sipTransport, null);
            var userAgent2 = new SIPUserAgent(sipTransport, null);

            // Only one of the user agents can use the microphone and speaker. The one designated
            // as the active agent gets the devices.
            SIPUserAgent activeUserAgent = null;
            RTPMediaSession activeRtpSession = null;

            // Get the default speaker.
            var (audioOutEvent, audioOutProvider) = GetAudioOutputDevice();
            m_audioOutProvider = audioOutProvider;
            WaveInEvent waveInEvent = GetAudioInputDevice();

            userAgent1.OnCallHungup += () => Log.LogInformation($"UA1: Call hungup by remote party.");
            userAgent1.ServerCallCancelled += (uas) => Log.LogInformation("UA1: Incoming call cancelled by caller.");

            userAgent2.OnCallHungup += () => Log.LogInformation($"UA2: Call hungup by remote party.");
            userAgent2.ServerCallCancelled += (uas) => Log.LogInformation("UA2: Incoming call cancelled by caller.");

            userAgent2.OnTransferNotify += (sipFrag) =>
            {
                if (!string.IsNullOrEmpty(sipFrag))
                {
                    Log.LogInformation($"UA2: Transfer status update: {sipFrag.Trim()}.");
                    if (sipFrag?.Contains("SIP/2.0 200") == true)
                    {
                        // The transfer attempt got a succesful answer. Can hangup the call.
                        userAgent2.Hangup();
                        exitCts.Cancel();
                    }
                }
            };

            sipTransport.SIPTransportRequestReceived += (locelEndPoint, remoteEndPoint, sipRequest) =>
            {
                if (sipRequest.Header.From != null &&
                    sipRequest.Header.From.FromTag != null &&
                    sipRequest.Header.To != null &&
                    sipRequest.Header.To.ToTag != null)
                {
                    // This is an in-dialog request that will be handled directly by a user agent instance.
                }
                else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    if (!userAgent1.IsCallActive)
                    {
                        Log.LogInformation($"UA1: Incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                        var incomingCall = userAgent1.AcceptCall(sipRequest);

                        var rtpSession = new RTPSession((int)SDPMediaFormatsEnum.PCMU, null, null, false, AddressFamily.InterNetwork);
                        activeRtpSession = new RTPMediaSession(rtpSession);
                        userAgent1.Answer(incomingCall, activeRtpSession);

                        activeUserAgent = userAgent1;
                        activeRtpSession.OnReceivedSampleReady += PlaySample;
                        waveInEvent.StartRecording();

                        Log.LogInformation($"UA1: Answered incoming call from {sipRequest.Header.From.FriendlyDescription()} at {remoteEndPoint}.");
                    }
                    else if (!userAgent2.IsCallActive)
                    {
                        Log.LogInformation($"UA2: Incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");


                        var incomingCall = userAgent2.AcceptCall(sipRequest);
                        var rtpSession = new RTPSession((int)SDPMediaFormatsEnum.PCMU, null, null, false, AddressFamily.InterNetwork);
                        var rtpMediaSession = new RTPMediaSession(rtpSession);

                        userAgent2.Answer(incomingCall, rtpMediaSession);

                        activeRtpSession.OnReceivedSampleReady -= PlaySample;

                        activeUserAgent = userAgent2;
                        activeRtpSession = rtpMediaSession;
                        activeRtpSession.PutOnHold();
                        activeRtpSession.OnReceivedSampleReady += PlaySample;

                        Log.LogInformation($"UA2: Answered incoming call from {sipRequest.Header.From.FriendlyDescription()} at {remoteEndPoint}.");
                    }
                    else
                    {
                        // If both user agents are already on a call return a busy response.
                        Log.LogWarning($"Busy response returned for incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                        UASInviteTransaction uasTransaction = new UASInviteTransaction(sipTransport, sipRequest, null);
                        SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                        uasTransaction.SendFinalResponse(busyResponse);
                    }
                }
                else
                {
                    Log.LogDebug($"SIP {sipRequest.Method} request received but no processing has been set up for it, rejecting.");
                    SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    return sipTransport.SendResponseAsync(notAllowedResponse);
                }

                return Task.FromResult(0);
            };

            // Wire up the RTP send session to the audio input device.
            uint rtpSendTimestamp = 0;
            waveInEvent.DataAvailable += (object sender, WaveInEventArgs args) =>
            {
                byte[] sample = new byte[args.Buffer.Length / 2];
                int sampleIndex = 0;

                for (int index = 0; index < args.BytesRecorded; index += 2)
                {
                    var ulawByte = NAudio.Codecs.MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(args.Buffer, index));
                    sample[sampleIndex++] = ulawByte;
                }

                if (activeRtpSession != null)
                {
                    activeRtpSession.SendAudioFrame(rtpSendTimestamp, sample);
                    rtpSendTimestamp += (uint)sample.Length;
                }
            };

            // At this point the call has been initiated and everything will be handled in an event handler.
            Task.Run(async () =>
            {
                try
                {
                    while (!exitCts.Token.WaitHandle.WaitOne(0))
                    {
                        var keyProps = Console.ReadKey();

                        if (keyProps.KeyChar == 't')
                        {
                            if (userAgent1.IsCallActive && userAgent2.IsCallActive)
                            {
                                bool result = await userAgent2.AttendedTransfer(userAgent1.Dialogue, TimeSpan.FromSeconds(TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
                                if (!result)
                                {
                                    Log.LogWarning($"Attended transfer failed.");
                                }
                            }
                            else
                            {
                                Log.LogWarning("There need to be two active calls before the attended transfer can occur.");
                            }
                        }
                        else if (keyProps.KeyChar == 'q')
                        {
                            // Quit application.
                            exitCts.Cancel();
                        }
                    }
                }
                catch (Exception excp)
                {
                    SIPSorcery.Sys.Log.Logger.LogError($"Exception Key Press listener. {excp.Message}.");
                }
            });

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitCts.Cancel();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitCts.Token.WaitHandle.WaitOne();

            #region Cleanup.

            Log.LogInformation("Exiting...");

            userAgent1?.Hangup();
            userAgent2?.Hangup();
            waveInEvent?.StopRecording();
            audioOutEvent?.Stop();

            // Give any BYE or CANCEL requests time to be transmitted.
            Log.LogInformation("Waiting 1s for calls to be cleaned up...");
            Task.Delay(1000).Wait();

            SIPSorcery.Net.DNSManager.Stop();

            if (sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                sipTransport.Shutdown();
            }

            #endregion
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="audioOutProvider">The audio buffer for the default system audio output device.</param>
        private static void PlaySample(byte[] sample)
        {
            for (int index = 0; index < sample.Length; index++)
            {
                short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                m_audioOutProvider.AddSamples(pcmSample, 0, 2);
            }
        }

        /// <summary>
        /// Get the audio output device, e.g. speaker.
        /// Note that NAudio.Wave.WaveOut is not available for .Net Standard so no easy way to check if 
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
            UdpClient homerSIPClient = null;

            if (HOMER_SERVER_ADDRESS != null)
            {
                homerSIPClient = new UdpClient(0, AddressFamily.InterNetwork);
            }

            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");
                Log.LogDebug(req.ToString());

                if (homerSIPClient != null)
                {
                    var hepBuffer = HepPacket.GetBytes(remoteEP, localEP, DateTime.Now, 333, "myHep", req.ToString());
                    homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
                }
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");
                Log.LogDebug(req.ToString());

                if (homerSIPClient != null)
                {
                    var hepBuffer = HepPacket.GetBytes(localEP, remoteEP, DateTime.Now, 333, "myHep", req.ToString());
                    homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
                }
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");
                Log.LogDebug(resp.ToString());

                if (homerSIPClient != null)
                {
                    var hepBuffer = HepPacket.GetBytes(remoteEP, localEP, DateTime.Now, 333, "myHep", resp.ToString());
                    homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
                }
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                Log.LogDebug($"Response sent: {localEP}->{remoteEP}");
                Log.LogDebug(resp.ToString());

                if (homerSIPClient != null)
                {
                    var hepBuffer = HepPacket.GetBytes(localEP, remoteEP, DateTime.Now, 333, "myHep", resp.ToString());
                    homerSIPClient.SendAsync(hepBuffer, hepBuffer.Length, HOMER_SERVER_ADDRESS, HOMER_SERVER_PORT);
                }
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
