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
        //private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:*61@192.168.11.48";
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:7000@192.168.11.48";
        private static readonly string TRANSFER_DESTINATION_SIP_URI = "sip:*60@192.168.11.48";  // The destination to transfer the intial call to.
        private static readonly string SIP_USERNAME = "7001";
        private static readonly string SIP_PASSWORD = "password";
        private static WaveFormat _waveFormat = new WaveFormat(8000, 16, 1);  // PCMU format used by both input and output streams.
        private static int INPUT_SAMPLE_PERIOD_MILLISECONDS = 20;           // This sets the frequency of the RTP packets.
        private static int TRANSFER_TIMEOUT_SECONDS = 10;                    // Give up on transfer if no response within this period.

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery call hold example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            bool isCallHungup = false;
            bool hasCallFailed = false;

            AddConsoleLogger();

            // Check whether an override desination has been entered on the command line.
            SIPURI callUri = SIPURI.ParseSIPURI(DEFAULT_DESTINATION_SIP_URI);
            if (args != null && args.Length > 0)
            {
                if (!SIPURI.TryParse(args[0], out var argUri))
                {
                    Log.LogWarning($"Command line argument could not be parsed as a SIP URI {args[0]}");
                }
                else
                {
                    callUri = argUri;
                }
            }
            Log.LogInformation($"Call destination {callUri}.");

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

            //EnableTraceLogs(sipTransport);

            var lookupResult = SIPDNSManager.ResolveSIPService(callUri, false);
            Log.LogDebug($"DNS lookup result for {callUri}: {lookupResult?.GetSIPEndPoint()}.");
            var dstAddress = lookupResult.GetSIPEndPoint().Address;

            IPAddress localIPAddress = NetServices.GetLocalAddressForRemote(dstAddress);

            // Initialise an RTP session to receive the RTP packets from the remote SIP server.
            var rtpSession = new RTPSession((int)SDPMediaFormatsEnum.PCMU, null, null, true);
            var sdpOffer = rtpSession.GetSDP(localIPAddress);

            // Create a client/server user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
            var userAgent = new SIPUserAgent(sipTransport, null);

            userAgent.ClientCallTrying += (uac, resp) =>
            {
                Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
            };
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

                    // Only set the remote RTP end point if there hasn't already been a packet received on it.
                    if (rtpSession.DestinationEndPoint == null)
                    {
                        rtpSession.DestinationEndPoint = SDP.GetSDPRTPEndPoint(resp.Body);
                        Log.LogDebug($"Remote RTP socket {rtpSession.DestinationEndPoint}.");
                    }
                }
                else
                {
                    Log.LogWarning($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                }
            };
            userAgent.OnCallHungup += () =>
            {
                Log.LogInformation($"Call hungup by remote party.");
                exitCts.Cancel();
            };
            userAgent.RemotePutOnHold += () => Log.LogInformation("Remote call party has placed us on hold.");
            userAgent.RemoteTookOffHold += () => Log.LogInformation("Remote call party took us off hold.");
            userAgent.OnReinviteRequest += (uasInviteTx) =>
            {
                Log.LogDebug("Reinvite request received.");

                // Re-INVITEs can also be changing the RTP end point. We can update this each time.
                IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(uasInviteTx.TransactionRequest.Body);
                if(rtpSession.DestinationEndPoint != dstRtpEndPoint)
                {
                    Log.LogDebug($"Remote call party RTP end point changed from {rtpSession.DestinationEndPoint} to {dstRtpEndPoint}.");
                    rtpSession.DestinationEndPoint = dstRtpEndPoint;
                }
                uasInviteTx.SendFinalResponse(uasInviteTx.GetOkResponse(SDP.SDP_MIME_CONTENTTYPE, userAgent.Dialogue.SDP));
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

            // Wire up the RTP send session to the audio output device.
            WaveInEvent waveInEvent = GetAudioInputDevice();
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

                if (rtpSession.DestinationEndPoint != null)
                {
                    rtpSession.SendAudioFrame(rtpSendTimestamp, sample);
                    rtpSendTimestamp += (uint)(8000 / waveInEvent.BufferMilliseconds);
                }

                waveInEvent.StartRecording();
            };

            // Start the thread that places the call.
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
                SIP_USERNAME,
                SIP_PASSWORD,
                callUri.ToString(),
                $"sip:{SIP_USERNAME}@localhost",
                callUri.CanonicalAddress,
                null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                sdpOffer.ToString(),
                null);

            userAgent.Call(callDescriptor);

            // At this point the call has been initiated and everything will be handled in an event handler.
            Task.Run(async () =>
            {
                try
                {
                    while (!exitCts.Token.WaitHandle.WaitOne(0))
                    {
                        var keyProps = Console.ReadKey();
                        if (keyProps.KeyChar == 'h')
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
                        }
                        else if(keyProps.KeyChar == 't')
                        {
                            var transferURI = SIPURI.ParseSIPURI(TRANSFER_DESTINATION_SIP_URI);
                            bool result = await userAgent.Transfer(transferURI, TimeSpan.FromSeconds(TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
                            if(result)
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

            rtpSession?.Close();
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
