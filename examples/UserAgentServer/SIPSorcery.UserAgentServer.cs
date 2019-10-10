//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to act as the server for a SIP call.
// 
// History:
// 09 Oct 2019	Aaron Clauson	Created.
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
        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery client user agent server example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Logging configuration. Can be ommitted if internal SIPSorcery debug and warning messages are not required.
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;

            CancellationTokenSource cts = new CancellationTokenSource();

            // If your default DNS server supports SRV records there is no need to set a specific DNS server.
            DNSManager.SetDNSServers(new List<IPEndPoint> { IPEndPoint.Parse("8.8.8.8:53") });

            IPAddress defaultAddr = LocalIPConfig.GetDefaultIPv4Address();

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());
            int port = FreePort.FindNextAvailableUDPPort(SIPConstants.DEFAULT_SIP_PORT);
            var sipChannel = new SIPUDPChannel(new IPEndPoint(defaultAddr, port));
            sipTransport.AddSIPChannel(sipChannel);

            bool isHungup = false;

            // Because this is a server user agent the SIP transport must start listening for client user agents.
            sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    isHungup = false;

                    SIPSorcery.Sys.Log.Logger.LogInformation("Incoming call request: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                    UASInviteTransaction uasTransaction = sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    var uas = new SIPServerUserAgent(sipTransport, null, null, null, SIPCallDirection.In, null, null, null, uasTransaction);
                    //uas.CallCancelled += UASCallCancelled;

                    uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
                    uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

                    // Initialise an RTP session to receive the RTP packets from the remote SIP server.
                    Socket rtpSocket = null;
                    Socket controlSocket = null;
                    NetServices.CreateRtpSocket(defaultAddr, 49000, 49100, false, out rtpSocket, out controlSocket);

                    IPEndPoint rtpEndPoint = new IPEndPoint(defaultAddr, (rtpSocket.LocalEndPoint as IPEndPoint).Port);
                    IPEndPoint dstRtpEndPoint = null;

                    var rtpSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);

                    var rtpRecvTask = Task.Run(() =>
                    {
                        byte[] buffer = new byte[2048];

                        SIPSorcery.Sys.Log.Logger.LogDebug($"Listening on RTP socket {rtpSocket.LocalEndPoint}.");

                        while (uas.IsCancelled == false && isHungup == false)
                        {
                            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                            int bytesRead = rtpSocket.ReceiveFrom(buffer, ref remoteEP);
                            Console.WriteLine($"Read {bytesRead} from remote RTP socket {remoteEP}.");
                            dstRtpEndPoint = remoteEP as IPEndPoint;
                        }
                    });

                    var rtpSendTask = Task.Run(async () =>
                    {
                        SIPSorcery.Sys.Log.Logger.LogDebug($"Sending from RTP socket {rtpSocket.LocalEndPoint}.");

                        while (uas.IsCancelled == false && isHungup == false && dstRtpEndPoint == null)
                        {
                            await Task.Delay(500, cts.Token);
                        }

                        if (dstRtpEndPoint != null)
                        {
                            var pcmFormat = new WaveFormat(8000, 16, 1);
                            var ulawFormat = WaveFormat.CreateMuLawFormat(8000, 1);

                            using (WaveFormatConversionStream pcmStm = new WaveFormatConversionStream(pcmFormat, new Mp3FileReader("whitelight.mp3")))
                            {
                                using (WaveFormatConversionStream ulawStm = new WaveFormatConversionStream(ulawFormat, pcmStm))
                                {
                                    byte[] buffer = new byte[160];
                                    int bytesRead = ulawStm.Read(buffer, 0, 160);

                                    while (bytesRead > 0 && uas.IsCancelled == false && isHungup == false)
                                    {
                                        byte[] sample = new byte[bytesRead];
                                        Array.Copy(buffer, sample, bytesRead);
                                        rtpSession.SendAudioFrame(rtpSocket, dstRtpEndPoint, 0, buffer);
                                        await Task.Delay(20, cts.Token);    // 50ms audio packets.

                                        bytesRead = ulawStm.Read(buffer, 0, 160);
                                    }
                                }
                            }
                        }
                    });

                    uas.Answer(SDP.SDP_MIME_CONTENTTYPE, GetSDP(rtpEndPoint).ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);
                }
                else if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    isHungup = true;

                    SIPSorcery.Sys.Log.Logger.LogInformation("Call hungup.");
                    SIPNonInviteTransaction byeTransaction = sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);
                }
            };
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
            sdp.Media.Add(audioAnnouncement);

            //sdp.AddExtra("a=sendrecv");

            return sdp;
        }
    }
}
