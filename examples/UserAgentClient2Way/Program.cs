//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An abbreviated example program of how to use the SIPSorcery core library to place a SIP call.
// This example builds on the UserAgentClient example to add 2 way audio instead of only one way audio playback.
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
        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:50100@sipsorcery.com";
        private static readonly int RTP_REPORTING_PERIOD_SECONDS = 5;       // Period at which to write RTP stats.

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery client user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource cts = new CancellationTokenSource();
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
            if(args != null && args.Length > 0)
            {
                if(!SIPURI.TryParse(args[0]))
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
            //sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, port)));
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, port)));
            //sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.IPv6Any, port)));

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

            // Select the IP address to use for RTP based on the destination SIP URI.
            var endPointForCall = callUri.ToSIPEndPoint() == null ? sipTransport.GetDefaultSIPEndPoint(callUri.Protocol) : sipTransport.GetDefaultSIPEndPoint(callUri.ToSIPEndPoint());

            // Initialise an RTP session to receive the RTP packets from the remote SIP server.
            Socket rtpSocket = null;
            Socket controlSocket = null;
            // TODO (find something better): If the SIP endpoint is using 0.0.0.0 for SIP use loopback for RTP.
            IPAddress rtpAddress = IPAddress.Equals(IPAddress.Any, endPointForCall.Address) ? IPAddress.Loopback : endPointForCall.Address;
            NetServices.CreateRtpSocket(rtpAddress, 49000, 49100, false, out rtpSocket, out controlSocket);
            var rtpSendSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);

            // Create a client user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
            var uac = new SIPClientUserAgent(sipTransport);

            uac.CallTrying += (uac, resp) =>
            {
                SIPSorcery.Sys.Log.Logger.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
            };
            uac.CallRinging += (uac, resp) => SIPSorcery.Sys.Log.Logger.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
            uac.CallFailed += (uac, err) =>
            {
                SIPSorcery.Sys.Log.Logger.LogWarning($"{uac.CallDescriptor.To} Failed: {err}");
                hasCallFailed = true;
            };
            uac.CallAnswered += (uac, resp) =>
            {
                if (resp.Status == SIPResponseStatusCodesEnum.Ok)
                {
                    SIPSorcery.Sys.Log.Logger.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");

                    IPEndPoint remoteRtpEndPoint = SDP.GetSDPRTPEndPoint(resp.Body);

                    SIPSorcery.Sys.Log.Logger.LogDebug($"Sending initial RTP packet to remote RTP socket {remoteRtpEndPoint}.");

                    // Send a dummy packet to open the NAT session on the RTP path.
                    rtpSendSession.SendAudioFrame(rtpSocket, remoteRtpEndPoint, 0, new byte[] { 0x00 });
                }
                else
                {
                    SIPSorcery.Sys.Log.Logger.LogWarning($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
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
                        SIPSorcery.Sys.Log.Logger.LogInformation("Call was hungup by remote server.");
                        isCallHungup = true;
                        cts.Cancel();
                    }
                }
            };

            // It's a good idea to start the RTP receiving socket before the call request is sent.
            // A SIP server will generally start sending RTP as soon as it has processed the incoming call request and
            // being ready to receive will stop any ICMP error response being generated.
            Task.Run(() => SendRecvRtp(rtpSocket, rtpSendSession, cts));

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
                cts.Cancel();
            };

            // At this point the call has been initiated and everything will be handled in an event handler or on the RTP
            // receive task. The code below is to gracefully exit.

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            cts.Token.WaitHandle.WaitOne();

            SIPSorcery.Sys.Log.Logger.LogInformation("Exiting...");

            rtpSocket?.Close();
            controlSocket?.Close();

            if (!isCallHungup && uac != null)
            {
                if (uac.IsUACAnswered)
                {
                    SIPSorcery.Sys.Log.Logger.LogInformation($"Hanging up call to {uac.CallDescriptor.To}.");
                    uac.Hangup();
                }
                else if (!hasCallFailed)
                {
                    SIPSorcery.Sys.Log.Logger.LogInformation($"Cancelling call to {uac.CallDescriptor.To}.");
                    uac.Cancel();
                }

                // Give the BYE or CANCEL request time to be transmitted.
                SIPSorcery.Sys.Log.Logger.LogInformation("Waiting 1s for call to clean up...");
                Task.Delay(1000).Wait();
            }

            SIPSorcery.Net.DNSManager.Stop();

            if (sipTransport != null)
            {
                SIPSorcery.Sys.Log.Logger.LogInformation("Shutting down SIP transport...");
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
        private static async void SendRecvRtp(Socket rtpSocket, RTPSession rtpSendSession, CancellationTokenSource cts)
        {
            try
            {
                DateTime lastRecvReportAt = DateTime.Now;
                uint packetReceivedCount = 0;
                uint bytesReceivedCount = 0;
                uint packetSentCount = 0;
                uint bytesSentCount = 0;
                byte[] buffer = new byte[512];

                uint rtpSendTimestamp = 0;
                IPEndPoint anyEndPoint = new IPEndPoint((rtpSocket.AddressFamily == AddressFamily.InterNetworkV6) ? IPAddress.IPv6Any : IPAddress.Any, 0);

                SIPSorcery.Sys.Log.Logger.LogDebug($"Listening on RTP socket {rtpSocket.LocalEndPoint}.");

                using (var waveOutEvent = new WaveOutEvent())
                {
                    var waveProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1));
                    waveProvider.DiscardOnBufferOverflow = true;
                    waveOutEvent.Init(waveProvider);
                    waveOutEvent.Play();

                    var recvResult = await rtpSocket.ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint);

                    SIPSorcery.Sys.Log.Logger.LogDebug($"Initial RTP packet recieved from {recvResult.RemoteEndPoint}.");

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

                        // Periodically Send a dummy packet to keep any NAT session that may be on the media path open.
                        if (DateTime.Now.Subtract(lastRecvReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                        {
                            // This is typically where RTCP reports would be sent. Omitted here for brevity.
                            lastRecvReportAt = DateTime.Now;
                            var remoteRtpEndPoint = recvResult.RemoteEndPoint as IPEndPoint;
                            SIPSorcery.Sys.Log.Logger.LogDebug($"RTP recv {rtpSocket.LocalEndPoint}<-{remoteRtpEndPoint} pkts {packetReceivedCount} bytes {bytesReceivedCount}");

                            rtpSendSession.SendAudioFrame(rtpSocket, recvResult.RemoteEndPoint as IPEndPoint, rtpSendTimestamp, new byte[] { 0x00 });
                            rtpSendTimestamp += 32000; // Arbitrary and not critical. Corresponds to 40ms payload at 25pps which means 4s for 100 packets.

                            packetSentCount++;
                            bytesSentCount++;
                            SIPSorcery.Sys.Log.Logger.LogDebug($"RTP sent {rtpSocket.LocalEndPoint}->{remoteRtpEndPoint} pkts {packetSentCount} bytes {bytesSentCount}");

                        }

                        recvResult = await rtpSocket.ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint);
                    }
                }
            }
            catch (ObjectDisposedException) { } // This is how .Net deals with an in use socket being closed. Safe to ignore.
            catch (Exception excp)
            {
                SIPSorcery.Sys.Log.Logger.LogError($"Exception processing RTP. {excp}");
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
    }
}
