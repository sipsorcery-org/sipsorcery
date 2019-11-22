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
// 12 Nov 2019	Aaron Clauson (aaron@sipsorcery.com)    Created, Dublin, Ireland.
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
// exten => *63,1(start),Gotoif($[ "${LEN(${extensao})}" < "5"]?collect:bye)
// exten => *63,n(collect),Read(digito,,1)
// exten => *63,n,SayDigits(${digito})
// exten => *63,n,Set(extensao=${extensao}${digito})
// exten => *63,n,GoTo(start)
// exten => *63,n(bye),Playback("vm-goodbye")
// exten => *63,n,hangup()
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
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
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:*63@192.168.11.48";   // Custom Asterisk dialplan to speak back DTMF tones.
        private static readonly int RTP_REPORTING_PERIOD_SECONDS = 5;                           // Period at which to write RTP stats.
        private static readonly int DTMF_EVENT_PAYLOAD_ID = 101;

        /// <summary>
        /// PCMU encoding for silence, http://what-when-how.com/voip/g-711-compression-voip/
        /// </summary>
        private static readonly byte PCMU_SILENCE_BYTE_ZERO = 0x7F;
        private static readonly byte PCMU_SILENCE_BYTE_ONE = 0xFF;
        private static readonly int SILENCE_SAMPLE_PERIOD = 50; // In milliseconds (PCM is 64kbit/s).

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static IPEndPoint _remoteRtpEndPoint = null;
        private static ConcurrentQueue<RTPEvent> _dtmfEvents = new ConcurrentQueue<RTPEvent>(); // Add a DTMF event to this queue to have the it sent

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
            Socket rtpSocket = null;
            Socket controlSocket = null;

            NetServices.CreateRtpSocket(localIPAddress, 49000, 49100, false, out rtpSocket, out controlSocket);
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
                GetSDP(rtpSocket.LocalEndPoint as IPEndPoint, RTPPayloadTypesEnum.PCMU).ToString(),
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
            Task.Delay(3000).Wait();

            // Add some DTMF events to the queue. These will be transmitted by the SendRtp thread.
            _dtmfEvents.Enqueue(new RTPEvent(0x05, false, RTPEvent.DEFAULT_VOLUME, 1200, DTMF_EVENT_PAYLOAD_ID));
            Task.Delay(2000, rtpCts.Token).Wait();
            _dtmfEvents.Enqueue(new RTPEvent(0x09, false, RTPEvent.DEFAULT_VOLUME, 1200, DTMF_EVENT_PAYLOAD_ID));
            Task.Delay(2000, rtpCts.Token).Wait();
            _dtmfEvents.Enqueue(new RTPEvent(0x02, false, RTPEvent.DEFAULT_VOLUME, 1200, DTMF_EVENT_PAYLOAD_ID));
            Task.Delay(2000, rtpCts.Token).Wait();

            Log.LogInformation("Exiting...");

            rtpCts.Cancel();
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

        /// <summary>
        /// Sends the sounds of silence. If the destination is on the other side of a NAT this is useful to open
        /// a pinhole and hopefully get the remote RTP stream through.
        /// </summary>
        /// <param name="rtpSocket">The socket we're using to send from.</param>
        /// <param name="rtpSendSession">Our RTP sending session.</param>
        /// <param name="cts">Cancellation token to stop the call.</param>
        private static async void SendRtp(Socket rtpSocket, RTPSession rtpSendSession, CancellationTokenSource cts)
        {
            int samplingFrequency = RTPPayloadTypes.GetSamplingFrequency((RTPPayloadTypesEnum)rtpSendSession.PayloadType);
            uint rtpTimestampStep = (uint)(samplingFrequency * SILENCE_SAMPLE_PERIOD / 1000);
            uint bufferSize = (uint)SILENCE_SAMPLE_PERIOD;
            uint rtpSendTimestamp = 0;
            uint packetSentCount = 0;
            uint bytesSentCount = 0;

            while (cts.IsCancellationRequested == false)
            {
                if (_remoteRtpEndPoint != null)
                {
                    if (!_dtmfEvents.IsEmpty)
                    {
                        // Check if there are any DTMF events to send.
                        _dtmfEvents.TryDequeue(out var rtpEvent);
                        if (rtpEvent != null)
                        {
                            await rtpSendSession.SendDtmfEvent(rtpSocket, _remoteRtpEndPoint, rtpEvent, rtpSendTimestamp, (ushort)SILENCE_SAMPLE_PERIOD, (ushort)rtpTimestampStep, cts);
                        }
                        rtpSendTimestamp += rtpEvent.TotalDuration + rtpTimestampStep;
                    }
                    else
                    {
                        // If there are no DTMF events to send we'll send silence.

                        byte[] sample = new byte[bufferSize / 2];
                        int sampleIndex = 0;

                        for (int index = 0; index < bufferSize; index += 2)
                        {
                            sample[sampleIndex] = PCMU_SILENCE_BYTE_ZERO;
                            sample[sampleIndex + 1] = PCMU_SILENCE_BYTE_ONE;
                        }

                        rtpSendSession.SendAudioFrame(rtpSocket, _remoteRtpEndPoint, rtpSendTimestamp, sample);
                        rtpSendTimestamp += rtpTimestampStep;
                        packetSentCount++;
                        bytesSentCount += (uint)sample.Length;
                    }
                }

                await Task.Delay(SILENCE_SAMPLE_PERIOD);
            }
        }

        private static SDP GetSDP(IPEndPoint rtpSocket, RTPPayloadTypesEnum audioPayloadType)
        {
            int samplingFrequency = RTPPayloadTypes.GetSamplingFrequency(audioPayloadType);

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
                MediaFormats = new List<SDPMediaFormat>() { new SDPMediaFormat((int)audioPayloadType, "PCMU", samplingFrequency) }
            };
            audioAnnouncement.Port = rtpSocket.Port;
            audioAnnouncement.ExtraAttributes.Add("a=sendrecv");
            audioAnnouncement.ExtraAttributes.Add($"a=rtpmap:{DTMF_EVENT_PAYLOAD_ID} telephone-event/{samplingFrequency}");
            audioAnnouncement.ExtraAttributes.Add($"a=fmtp:{DTMF_EVENT_PAYLOAD_ID} 0-15");
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
