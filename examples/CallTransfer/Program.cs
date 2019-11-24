//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An exmaple of using a REFER request to transfer an established call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 11 Nov 2019	Aaron Clauson (aaron@sipsorcery.com)    Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Note on call flow and destinations being used in this sample.
//
// The steps involved in the call flow are:
// 1. Attempt a normal call to an external SIP agent (destination set in DEFAULT_DESTINATION_SIP_URI).
// 2. If the call is answered wait 10 seconds (time set by DELAY_UNTIL_TRANSFER_MILLISECONDS).
// 3. Send a REFER request that asks the remote SIP agent from step 1 to call a new destination
//    (set by TRANSFER_DESTINATION_SIP_URI) and if successful use it to replace the current call.
//
// The scenario used in development used an Asterisk/FreePBX server on 192.168.11.48.
// The first destination of sip:100@192.168.11.48 was to a softphone that was registered 
// with the Asterisk server.
// The transfer destination of sip:*60@192.168.11.48 was to a talking clock extension on 
// the same Asterisk server.
//
// The outcome of the transfer is that the softphone receives a call, answers it and then after
// a 10s delay gets the transfer request and starts listening to the talking clock.
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
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:100@192.168.11.48";   // Desintation for inital call, needs to successfully answer.
        private static readonly string TRANSFER_DESTINATION_SIP_URI = "sip:*60@192.168.11.48";  // The destination to transfer the intial call to.
        private static readonly int DELAY_UNTIL_TRANSFER_MILLISECONDS = 10000;                 // Delay after the initial call is answered before initiating the transfer.

        /// <summary>
        /// PCMU encoding for silence, http://what-when-how.com/voip/g-711-compression-voip/
        /// </summary>
        private static readonly byte PCMU_SILENCE_BYTE_ZERO = 0x7F;
        private static readonly byte PCMU_SILENCE_BYTE_ONE = 0xFF;
        private static readonly int SILENCE_SAMPLE_PERIOD = 20; // In milliseconds (PCM is 64kbit/s).

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static IPEndPoint _remoteRtpEndPoint = null;

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
            //EnableTraceLogs(sipTransport);

            // Determine the local IP address to use for our RTP end point based on the address it will be sending to.
            var lookupResult = sipTransport.GetURIEndPoint(callUri, false);
            if (lookupResult != null)
            {
                var uasSIPEndPoint = lookupResult.GetSIPEndPoint();
                IPAddress rtpAddress = NetServices.GetLocalAddressForRemote(uasSIPEndPoint.GetIPEndPoint().Address);

                Log.LogInformation($"Call URI {callUri} resolved to {uasSIPEndPoint}.");

                // Initialise an RTP session to receive the RTP packets from the remote SIP server.
                Socket rtpSocket = null;
                Socket controlSocket = null;
                NetServices.CreateRtpSocket(rtpAddress, 49000, 49100, false, out rtpSocket, out controlSocket);
                var rtpRecvSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);
                var rtpSendSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);

                // Create a client user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
                var uac = new SIPClientUserAgent(sipTransport);

                uac.CallTrying += (uac, resp) =>
                {
                    Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
                    Log.LogDebug(resp.ToString());
                };
                uac.CallRinging += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
                uac.CallFailed += (uac, err) =>
                {
                    Log.LogWarning($"{uac.CallDescriptor.To} Failed: {err}");
                    hasCallFailed = true;
                    rtpCts.Cancel();
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

                        if (resp.StatusCode >= 400)
                        {
                            // Call failed.
                            hasCallFailed = true;
                            rtpCts.Cancel();
                        }
                    }
                };

                // The only incoming request that needs to be explicitly handled for this example is if the remote end hangs up the call.
                sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
                {
                    if (sipRequest.Method == SIPMethodsEnum.BYE)
                    {
                        SIPNonInviteTransaction byeTransaction = sipTransport.CreateNonInviteTransaction(sipRequest, null);
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

                // At this point the call is established. We'll wait for a few seconds and then transfer.
                // Note the empty ContinueWith is to catch and ignore the TaskCancelationExeption generated if
                // rtpCts is set by the call failing.
                Task.Delay(DELAY_UNTIL_TRANSFER_MILLISECONDS, rtpCts.Token).ContinueWith(tsk => { }).Wait();

                if (!hasCallFailed)
                {
                    SIPRequest referRequest = GetReferRequest(uac, SIPURI.ParseSIPURI(TRANSFER_DESTINATION_SIP_URI));
                    SIPNonInviteTransaction referTx = sipTransport.CreateNonInviteTransaction(referRequest, null);

                    referTx.NonInviteTransactionFinalResponseReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) =>
                    {
                        if (sipResponse.Header.CSeqMethod == SIPMethodsEnum.REFER && sipResponse.Status == SIPResponseStatusCodesEnum.Accepted)
                        {
                            Log.LogInformation("Call transfer was accepted by remote server.");
                            isCallHungup = true;
                            rtpCts.Cancel();
                        }
                    };

                    referTx.SendReliableRequest();
                }

                // At this point the call transfer has been initiated and everything will be handled in an event handler or on the RTP
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
        /// Handling packets received on the RTP socket. One of the simplest, if not the simplest, cases is
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
            uint bufferSize = (uint)SILENCE_SAMPLE_PERIOD * 8; // PCM transmission rate is 64kbit/s.
            uint rtpSamplePeriod = (uint)(1000 / SILENCE_SAMPLE_PERIOD);
            uint rtpSendTimestamp = 0;
            uint packetSentCount = 0;
            uint bytesSentCount = 0;

            while (cts.IsCancellationRequested == false)
            {
                byte[] sample = new byte[bufferSize / 2];
                int sampleIndex = 0;

                for (int index = 0; index < bufferSize; index += 2)
                {
                    sample[sampleIndex] = PCMU_SILENCE_BYTE_ZERO;
                    sample[sampleIndex + 1] = PCMU_SILENCE_BYTE_ONE;
                }

                if (_remoteRtpEndPoint != null)
                {
                    rtpSendSession.SendAudioFrame(rtpSocket, _remoteRtpEndPoint, rtpSendTimestamp, sample);
                    rtpSendTimestamp += rtpSamplePeriod;
                    packetSentCount++;
                    bytesSentCount += (uint)sample.Length;
                }

                await Task.Delay((int)rtpSamplePeriod);
            }
        }

        /// <summary>
        /// Get the SDP payload for an INVITE request.
        /// </summary>
        /// <param name="rtpSocket">The RTP socket end point that will be used to receive and send RTP.</param>
        /// <returns>An SDP object.</returns>
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
        /// Builds the REFER request to transfer an established call.
        /// </summary>
        /// <param name="sipDialogue">A SIP dialogue object representing the established call.</param>
        /// <param name="referToUri">The SIP URI to transfer the call to.</param>
        /// <returns>A SIP REFER request.</returns>
        private static SIPRequest GetReferRequest(SIPClientUserAgent uac, SIPURI referToUri)
        {
            SIPDialogue sipDialogue = uac.SIPDialogue;

            SIPRequest referRequest = new SIPRequest(SIPMethodsEnum.REFER, sipDialogue.RemoteTarget);
            referRequest.SetSendFromHints(uac.ServerTransaction.TransactionRequest.LocalSIPEndPoint);

            SIPFromHeader referFromHeader = SIPFromHeader.ParseFromHeader(sipDialogue.LocalUserField.ToString());
            SIPToHeader referToHeader = SIPToHeader.ParseToHeader(sipDialogue.RemoteUserField.ToString());
            int cseq = sipDialogue.CSeq + 1;
            sipDialogue.CSeq++;

            SIPHeader referHeader = new SIPHeader(referFromHeader, referToHeader, cseq, sipDialogue.CallId);
            referHeader.CSeqMethod = SIPMethodsEnum.REFER;
            referRequest.Header = referHeader;
            referRequest.Header.Routes = sipDialogue.RouteSet;
            referRequest.Header.ProxySendFrom = sipDialogue.ProxySendFrom;
            referRequest.Header.Vias.PushViaHeader(SIPViaHeader.GetDefaultSIPViaHeader());
            referRequest.Header.ReferTo = referToUri.ToString();
            referRequest.Header.Contact = new List<SIPContactHeader>() { SIPContactHeader.GetDefaultSIPContactHeader() };

            return referRequest;
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
