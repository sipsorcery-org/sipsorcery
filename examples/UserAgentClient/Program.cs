//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An abbreviated example program of how to use the SIPSorcery core library to place a SIP call
// and play the received audio. 
// 
// History:
// 08 Oct 2019	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2019 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Dublin, Ireland (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
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
        private static readonly string DESTINATION_SIP_URI = "sip:500@sipsorcery.com";
        private static readonly int RTP_REPORTING_PERIOD_SECONDS = 5;       // Period at which to write RTP stats.

        static void Main()
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

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            int port = SIPConstants.DEFAULT_SIP_PORT + 1000;
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, port)));
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.IPv6Loopback, port)));
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(LocalIPConfig.GetDefaultIPv4Address(), port)));
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(LocalIPConfig.GetDefaultIPv6Address(), port)));

            // Select the IP address to use for RTP based on the destination SIP URI.
            SIPURI callURI = SIPURI.ParseSIPURIRelaxed(DESTINATION_SIP_URI);
            var endPointForCall = callURI.ToSIPEndPoint() == null ? sipTransport.GetDefaultSIPEndPoint(callURI.Protocol) : sipTransport.GetDefaultSIPEndPoint(callURI.ToSIPEndPoint());

            // Initialise an RTP session to receive the RTP packets from the remote SIP server.
            Socket rtpSocket = null;
            Socket controlSocket = null;
            NetServices.CreateRtpSocket(endPointForCall.Address, 49000, 49100, false, out rtpSocket, out controlSocket);
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
                    SIPSorcery.Sys.Log.Logger.LogDebug(resp.ToString());

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
                DESTINATION_SIP_URI,
                SIPConstants.SIP_DEFAULT_FROMURI,
                null, null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                GetSDP(rtpSocket.LocalEndPoint as IPEndPoint).ToString(),
                null);

            uac.Call(callDescriptor);

            // At this point the call has been initiated and everything will be handled in an event handler or on the RTP
            // receive task. The code below is to gracefully exit.
            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += async delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                cts.Cancel();

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
                    await Task.Delay(1000);
                }

                SIPSorcery.Net.DNSManager.Stop();

                if (sipTransport != null)
                {
                    SIPSorcery.Sys.Log.Logger.LogInformation("Shutting down SIP transport...");
                    sipTransport.Shutdown();
                }
            };
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
