//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An abbreviated example program of how to use the SIPSorcery core library to place a SIP call.
// In order to add 2 way audio the default audio source (microphone) is used. If no audio source is available
// then the example will fallback to on way audio.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 26 Oct 2019	Aaron Clauson	Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
using SIPSorcery.Sys;

namespace SIPSorcery
{
    class Program
    {
        //private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:time@sipsorcery.com";  // Talking Clock.
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:echo@sipsorcery.com"; // Echo Test.
        private static readonly int RTP_REPORTING_PERIOD_SECONDS = 5;       // Period at which to write RTP stats.

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static IPEndPoint _remoteRtpEndPoint = null;

        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery client user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource rtpCts = new CancellationTokenSource(); // Cancellation token to stop the RTP stream.
            bool isCallHungup = false;
            bool hasCallFailed = false;

            // Logging configuration. Can be ommitted if internal SIPSorcery debug and warning messages are not required.
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;

            SIPURI callUri = SIPURI.ParseSIPURI(DEFAULT_DESTINATION_SIP_URI);
            if (args != null && args.Length > 0)
            {
                if (!SIPURI.TryParse(args[0]))
                {
                    Log.LogWarning($"Command line argument could not be parsed as a SIP URI {args[0]}");
                }
                else
                {
                    callUri = SIPURI.ParseSIPURIRelaxed(args[0]);
                }
            }

            Log.LogInformation($"Call destination {callUri}.");

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            int port = SIPConstants.DEFAULT_SIP_PORT + 1000;
            IPAddress localAddress = sipTransport.GetLocalAddress(IPAddress.Parse("8.8.8.8"));
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(localAddress, port)));
            //sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, port)));
            //sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.IPv6Any, port)));

            //EnableTraceLogs(sipTransport);

            // Select the IP address to use for RTP based on the destination SIP URI.
            var endPointForCall = callUri.ToSIPEndPoint() == null ? sipTransport.GetDefaultSIPEndPoint(callUri.Protocol) : sipTransport.GetDefaultSIPEndPoint(callUri.ToSIPEndPoint());

            // Initialise an RTP session to receive the RTP packets from the remote SIP server.
            Socket rtpSocket = null;
            Socket controlSocket = null;
            // TODO (find something better): If the SIP endpoint is using 0.0.0.0 for SIP use loopback for RTP.
            IPAddress rtpAddress = localAddress;
            NetServices.CreateRtpSocket(rtpAddress, 49000, 49100, false, out rtpSocket, out controlSocket);
            var rtpRecvSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);
            var rtpSendSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);

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

                    _remoteRtpEndPoint = SDP.GetSDPRTPEndPoint(resp.Body);

                    Log.LogDebug($"Remote RTP socket {_remoteRtpEndPoint}.");
                }
                else
                {
                    Log.LogWarning($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                }
            };

            // The only incoming request that needs to be explicitly handled for this example is if the remote end hangs up the call.
            sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPNonInviteTransaction byeTransaction = sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);

                    if (uac.IsUACAnswered)
                    {
                        Log.LogInformation("Call was hungup by remote server.");
                        isCallHungup = true;
                        rtpCts.Cancel();
                    }
                }
            };

            // It's a good idea to start the RTP receiving socket before the call request is sent.
            // A SIP server will generally start sending RTP as soon as it has processed the incoming call request and
            // being ready to receive will stop any ICMP error response being generated.
            Task.Run(() => RecvRtp(rtpSocket, rtpRecvSession, rtpCts));
            Task.Run(() => SendRtp(rtpSocket, rtpSendSession, rtpCts));

            // Start the thread that places the call.
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
                SIPConstants.SIP_DEFAULT_USERNAME,
                null,
                callUri.ToString(),
                SIPConstants.SIP_DEFAULT_FROMURI,
                null, null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                GetSDP(rtpSocket.LocalEndPoint as IPEndPoint).ToString(),
                null);

            uac.Call(callDescriptor);

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                rtpCts.Cancel();
            };

            // At this point the call has been initiated and everything will be handled in an event handler or on the RTP
            // receive task. The code below is to gracefully exit.

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            rtpCts.Token.WaitHandle.WaitOne();

            Log.LogInformation("Exiting...");

            rtpSocket?.Close();
            controlSocket?.Close();

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
        /// Handling packets received on the RTP socket. One of the simplest, if not the simplest, cases, is
        /// PCMU audio packets. THe handling can get substantially more complicated if the RTP socket is being
        /// used to multiplex different protocols. This is what WebRTC does with STUN, RTP and RTCP.
        /// </summary>
        /// <param name="rtpSocket">The raw RTP socket.</param>
        /// <param name="rtpSendSession">The session infor for the RTP pakcets being sent.</param>
        private static async void RecvRtp(Socket rtpSocket, RTPSession rtpRecvSession, CancellationTokenSource cts)
        {
            try
            {
                DateTime lastRecvReportAt = DateTime.Now;
                uint packetReceivedCount = 0;
                uint bytesReceivedCount = 0;
                byte[] buffer = new byte[512];

                IPEndPoint anyEndPoint = new IPEndPoint((rtpSocket.AddressFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any, 0);

                Log.LogDebug($"Listening on RTP socket {rtpSocket.LocalEndPoint}.");

                using (var waveOutEvent = new WaveOutEvent())
                {
                    var waveProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1));
                    waveProvider.DiscardOnBufferOverflow = true;
                    waveOutEvent.Init(waveProvider);
                    waveOutEvent.Play();

                    var recvResult = await rtpSocket.ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint);

                    Log.LogDebug($"Initial RTP packet recieved from {recvResult.RemoteEndPoint}.");

                    while (recvResult.ReceivedBytes > 0 && !cts.IsCancellationRequested)
                    {
                        var rtpPacket = new RTPPacket(buffer.Take(recvResult.ReceivedBytes).ToArray());

                        packetReceivedCount++;
                        bytesReceivedCount += (uint)rtpPacket.Payload.Length;

                        for (int index = 0; index < rtpPacket.Payload.Length; index++)
                        {
                            short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(rtpPacket.Payload[index]);
                            byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                            waveProvider.AddSamples(pcmSample, 0, 2);
                        }

                        recvResult = await rtpSocket.ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint);

                        if (DateTime.Now.Subtract(lastRecvReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                        {
                            // This is typically where RTCP receiver (SR) reports would be sent. Omitted here for brevity.
                            lastRecvReportAt = DateTime.Now;
                            var remoteRtpEndPoint = recvResult.RemoteEndPoint as IPEndPoint;
                            Log.LogDebug($"RTP recv report {rtpSocket.LocalEndPoint}<-{remoteRtpEndPoint} pkts {packetReceivedCount} bytes {bytesReceivedCount}");
                        }
                    }
                }
            }
            catch (ObjectDisposedException) { } // This is how .Net deals with an in use socket being closed. Safe to ignore.
            catch (Exception excp)
            {
                Log.LogError($"Exception processing RTP. {excp}");
            }
        }

        private static void SendRtp(Socket rtpSocket, RTPSession rtpSendSession, CancellationTokenSource cts)
        {
            WaveFormat waveFormat = new WaveFormat(8000, 16, 1);   // The format that both the input and output audio streams will use, i.e. PCMU.

            // Set up the input device that will provide audio samples that can be encoded, packaged into RTP and sent to
            // the remote end of the call.
            if (WaveInEvent.DeviceCount == 0)
            {
                Log.LogWarning("No audio input devices available. No audio will be sent.");
            }
            else
            {
                DateTime lastSendReportAt = DateTime.Now;
                uint rtpSendTimestamp = 0;
                uint packetSentCount = 0;
                uint bytesSentCount = 0;

                // Device used to get audio sample from, e.g. microphone.
                using (WaveInEvent waveInEvent = new WaveInEvent())
                {
                    waveInEvent.BufferMilliseconds = 20;    // This sets the frequency of the RTP packets.
                    waveInEvent.NumberOfBuffers = 1;
                    waveInEvent.DeviceNumber = 0;
                    waveInEvent.WaveFormat = waveFormat;
                    waveInEvent.DataAvailable += (object sender, WaveInEventArgs args) =>
                    {
                        byte[] sample = new byte[args.Buffer.Length / 2];
                        int sampleIndex = 0;

                        for (int index = 0; index < args.BytesRecorded; index += 2)
                        {
                            var ulawByte = NAudio.Codecs.MuLawEncoder.LinearToMuLawSample(BitConverter.ToInt16(args.Buffer, index));
                            sample[sampleIndex++] = ulawByte;
                        }

                        if (_remoteRtpEndPoint != null)
                        {
                            rtpSendSession.SendAudioFrame(rtpSocket, _remoteRtpEndPoint, rtpSendTimestamp, sample);
                            rtpSendTimestamp += (uint)(8000 / waveInEvent.BufferMilliseconds);
                            packetSentCount++;
                            bytesSentCount += (uint)sample.Length;
                        }

                        if (DateTime.Now.Subtract(lastSendReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                        {
                            // This is typically where RTCP sender (SR) reports would be sent. Omitted here for brevity.
                            lastSendReportAt = DateTime.Now;
                            var remoteRtpEndPoint = _remoteRtpEndPoint as IPEndPoint;
                            Log.LogDebug($"RTP send report {rtpSocket.LocalEndPoint}->{remoteRtpEndPoint} pkts {packetSentCount} bytes {bytesSentCount}");
                        }
                    };

                    waveInEvent.StartRecording();

                    cts.Token.WaitHandle.WaitOne();
                }
            }
        }

        private static SDP GetSDP(IPEndPoint rtpSocket)
        {
            var sdp = new SDP()
            {
                SessionId = Crypto.GetRandomInt(5).ToString(),
                Address = rtpSocket.Address.ToString(),
                SessionName = "sipsorcery",
                Timing = "0 0",
                Connection = new SDPConnectionInformation(rtpSocket.Address.ToString()),
            };

            var audioAnnouncement = new SDPMediaAnnouncement()
            {
                Media = SDPMediaTypesEnum.audio,
                MediaFormats = new List<SDPMediaFormat>() { new SDPMediaFormat((int)SDPMediaFormatsEnum.PCMU, "PCMU", 8000) }
            };
            audioAnnouncement.Port = rtpSocket.Port;
            audioAnnouncement.ExtraAttributes.Add("a=sendrecv");
            sdp.Media.Add(audioAnnouncement);

            return sdp;
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
