//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// place a SIP call and then place it on and off hold.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 25 Nov 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
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
        private static int SIP_LISTEN_PORT = 5060;
        //private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:*61@192.168.11.48";
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:7000@192.168.11.48";
        private static readonly string TRANSFER_DESTINATION_SIP_URI = "sip:*60@192.168.11.48";  // The destination to transfer the intial call to.
        private static readonly string SIP_USERNAME = "7001";
        private static readonly string SIP_PASSWORD = "password";
        private static WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);  // PCMU format used by both input and output streams.
        private static int INPUT_SAMPLE_PERIOD_MILLISECONDS = 20;           // This sets the frequency of the RTP packets.
        private static int TRANSFER_TIMEOUT_SECONDS = 10;                    // Give up on transfer if no response within this period.

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main()
        {
            Console.WriteLine("SIPSorcery call hold example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            bool isCallHungup = false;
            bool hasCallFailed = false;

            AddConsoleLogger();

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            //EnableTraceLogs(sipTransport);

            // Get the default speaker.
            var (audioOutEvent, audioOutProvider) = GetAudioOutputDevice();
            WaveInEvent waveInEvent = GetAudioInputDevice();

            // Create a client/server user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
            var userAgent = new SIPUserAgent(sipTransport, null);

            userAgent.ClientCallTrying += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
            userAgent.ClientCallRinging += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
            userAgent.ClientCallFailed += (uac, err) =>
            {
                Log.LogWarning($"{uac.CallDescriptor.To} Failed: {err}");
                hasCallFailed = true;
                exitCts.Cancel();
            };
            userAgent.ClientCallAnswered += (uac, resp) =>
            {
                if (resp.Status == SIPResponseStatusCodesEnum.Ok)
                {
                    Log.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                    PlayRemoteMedia(userAgent.RtpSession, audioOutProvider);
                }
                else
                {
                    Log.LogWarning($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                    hasCallFailed = true;
                    exitCts.Cancel();
                }
            };
            userAgent.OnCallHungup += () =>
            {
                Log.LogInformation($"Call hungup by remote party.");
                exitCts.Cancel();
            };
            userAgent.ServerCallCancelled += (uas) => Log.LogInformation("Incoming call cancelled by caller.");
            userAgent.RemotePutOnHold += () => Log.LogInformation("Remote call party has placed us on hold.");
            userAgent.RemoteTookOffHold += () => Log.LogInformation("Remote call party took us off hold.");

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
                    if (userAgent?.IsCallActive == true)
                    {
                        Log.LogWarning($"Busy response returned for incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                        // If we are already on a call return a busy response.
                        UASInviteTransaction uasTransaction = new UASInviteTransaction(sipTransport, sipRequest, null);
                        SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                        uasTransaction.SendFinalResponse(busyResponse);
                    }
                    else
                    {
                        Log.LogInformation($"Incoming call request from {remoteEndPoint}: {sipRequest.StatusLine}.");
                        var incomingCall = userAgent.AcceptCall(sipRequest);
                        userAgent.Answer(incomingCall);

                        PlayRemoteMedia(userAgent.RtpSession, audioOutProvider);
                        waveInEvent.StartRecording();

                        Log.LogInformation($"Answered incoming call from {sipRequest.Header.From.FriendlyDescription()} at {remoteEndPoint}.");
                    }
                }
                else
                {
                    Log.LogDebug($"SIP {sipRequest.Method} request received but no processing has been set up for it, rejecting.");
                    SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                    sipTransport.SendResponse(notAllowedResponse);
                }
            };

            // Wire up the RTP send session to the audio output device.
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

                if (userAgent?.RtpSession?.DestinationEndPoint != null)
                {
                    userAgent.RtpSession.SendAudioFrame(rtpSendTimestamp, sample);
                    rtpSendTimestamp += (uint)(8000 / waveInEvent.BufferMilliseconds);
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

                        if(keyProps.KeyChar == 'c')
                        {
                            if (!userAgent.IsCallActive)
                            {
                                var callDescriptor = GetCallDescriptor(DEFAULT_DESTINATION_SIP_URI);
                                userAgent.Call(callDescriptor);
                            }
                            else
                            {
                                Log.LogWarning("There is already an active call.");
                            }
                        }
                        else if (keyProps.KeyChar == 'h')
                        {
                            // Place call on/off hold.
                            if (userAgent.IsCallActive)
                            {
                                if (userAgent.OnHoldFromLocal)
                                {
                                    Log.LogInformation("Taking the remote call party off hold.");
                                    userAgent.TakeOffHold();
                                }
                                else
                                {
                                    Log.LogInformation("Placing the remote call party on hold.");
                                    userAgent.PutOnHold();
                                }
                            }
                            else
                            {
                                Log.LogWarning("There is no active call to put on hold.");
                            }
                        }
                        else if(keyProps.KeyChar == 't')
                        {
                            if (userAgent.IsCallActive)
                            {
                                var transferURI = SIPURI.ParseSIPURI(TRANSFER_DESTINATION_SIP_URI);
                                bool result = await userAgent.BlindTransfer(transferURI, TimeSpan.FromSeconds(TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
                                if (result)
                                {
                                    // If the transfer was accepted the original call will already have been hungup.
                                    // Wait a second for the transfer NOTIFY request to arrive.
                                    await Task.Delay(1000);
                                    exitCts.Cancel();
                                }
                                else
                                {
                                    Log.LogWarning($"Transfer to {TRANSFER_DESTINATION_SIP_URI} failed.");
                                }
                            }
                            else
                            {
                                Log.LogWarning("There is no active call to transfer.");
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

            userAgent?.RtpSession?.Close();
            waveInEvent?.StopRecording();
            audioOutEvent?.Stop();

            if (!isCallHungup && userAgent != null)
            {
                if (userAgent.IsCallActive)
                {
                    Log.LogInformation($"Hanging up call to {userAgent?.CallDescriptor?.To}.");
                    userAgent.Hangup();
                }
                else if (!hasCallFailed)
                {
                    Log.LogInformation($"Cancelling call to {userAgent?.CallDescriptor?.To}.");
                    userAgent.Cancel();
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

            #endregion
        }

        /// <summary>
        /// Gets the call descriptor to allow an outgoing call to be placed.
        /// </summary>
        /// <param name="callUri">The URI to place the call to.</param>
        /// <param name="rtpSession">The RTP session that will be handling the RTP/RTCP packets for the call.</param>
        /// <returns>A call descriptor.</returns>
        private static SIPCallDescriptor GetCallDescriptor(string callUri)
        {
            // Create a call descriptor to place an outgoing call.
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
                SIP_USERNAME,
                SIP_PASSWORD,
                callUri,
                $"sip:{SIP_USERNAME}@localhost",
                callUri,
                null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                null,
                null);

            return callDescriptor;
        }

        /// <summary>
        /// Wires up the active RTP session to the speaker.
        /// </summary>
        /// <param name="rtpSession">The active RTP session receiving the remote party's RTP packets.</param>
        /// <param name="audioOutProvider">The audio buffer for the default system audio output device.</param>
        private static void PlayRemoteMedia(RTPSession rtpSession, BufferedWaveProvider audioOutProvider)
        {
            rtpSession.OnReceivedSampleReady += (sample) =>
            {
                for (int index = 0; index < sample.Length; index++)
                {
                    short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(sample[index]);
                    byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                    audioOutProvider.AddSamples(pcmSample, 0, 2);
                }
            };
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
