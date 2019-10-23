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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
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
        private static readonly string AUDIO_FILE = "the_simplicity.ulaw";
        //private static readonly string AUDIO_FILE = "the_simplicity.mp3";
        private static readonly int RTP_REPORTING_PERIOD_SECONDS = 5;       // Period at which to write RTP stats.

        static void Main()
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

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            int port = 6060; // FreePort.FindNextAvailableUDPPort(SIPConstants.DEFAULT_SIP_PORT);
            //sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, port)));
            //sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.IPv6Loopback, port)));
            //sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(LocalIPConfig.GetDefaultIPv4Address(), port)));
            //sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(LocalIPConfig.GetDefaultIPv6Address(), port)));
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, port)));
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.IPv6Any, port)));
            sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.Any, port)));
            sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(IPAddress.IPv6Any, port)));

            if(File.Exists("localhost.pfx"))
            {
                var certificate = new X509Certificate2(@"localhost.pfx", "");
                // TODO fix TLSChannel and uncomment 2 lines.
                //sipTransport.AddSIPChannel(new SIPTLSChannel(certificate, new IPEndPoint(IPAddress.Any, SIPConstants.DEFAULT_SIP_TLS_PORT)));
                //sipTransport.AddSIPChannel(new SIPTLSChannel(certificate, new IPEndPoint(IPAddress.IPv6Any, SIPConstants.DEFAULT_SIP_TLS_PORT)));
            }

            // To keep things a bit simpler this example only supports a single call at a time and the SIP server user agent
            // acts as a singleton
            SIPServerUserAgent uas = null;
            CancellationTokenSource uasCts = null;

            // Because this is a server user agent the SIP transport must start listening for client user agents.
            sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                if (sipRequest.Method == SIPMethodsEnum.INVITE)
                {
                    SIPSorcery.Sys.Log.Logger.LogInformation("Incoming call request: " + localSIPEndPoint + "<-" + remoteEndPoint + " " + sipRequest.URI.ToString() + ".");
                    SIPSorcery.Sys.Log.Logger.LogDebug(sipRequest.ToString());

                   // If there's already a call in progress hang it up. Of course this is not ideal for a real softphone or server but it 
                   // means this example can be kept a little it simpler.
                   uas?.Hangup();

                    UASInviteTransaction uasTransaction = sipTransport.CreateUASTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    uas = new SIPServerUserAgent(sipTransport, null, null, null, SIPCallDirection.In, null, null, null, uasTransaction);
                    uasCts = new CancellationTokenSource();

                    uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
                    uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

                    // Initialise an RTP session to receive the RTP packets from the remote SIP server.
                    Socket rtpSocket = null;
                    Socket controlSocket = null;
                    NetServices.CreateRtpSocket(localSIPEndPoint.Address, 49000, 49100, false, out rtpSocket, out controlSocket);

                    IPEndPoint rtpEndPoint = rtpSocket.LocalEndPoint as IPEndPoint;
                    IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(sipRequest.Body);
                    var rtpSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);

                    var rtpTask = Task.Run(() => SendRecvRtp(rtpSocket, rtpSession, dstRtpEndPoint, AUDIO_FILE, uasCts))
                        .ContinueWith(_ => { if (uas?.IsHungup == false) uas?.Hangup(); });

                    uas.Answer(SDP.SDP_MIME_CONTENTTYPE, GetSDP(rtpEndPoint).ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);
                }
                else if (sipRequest.Method == SIPMethodsEnum.BYE)
                {
                    SIPSorcery.Sys.Log.Logger.LogInformation("Call hungup.");
                    SIPNonInviteTransaction byeTransaction = sipTransport.CreateNonInviteTransaction(sipRequest, remoteEndPoint, localSIPEndPoint, null);
                    SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                    byeTransaction.SendFinalResponse(byeResponse);
                    uas?.Hangup();
                    uasCts?.Cancel();
                }
                else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                {
                    try
                    {
                        SIPSorcery.Sys.Log.Logger.LogInformation($"{localSIPEndPoint.ToString()}<-{remoteEndPoint.ToString()}: {sipRequest.StatusLine}");
                        SIPSorcery.Sys.Log.Logger.LogDebug(sipRequest.ToString());
                        SIPResponse optionsResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        sipTransport.SendResponse(optionsResponse);
                    }
                    catch(Exception optionsExcp)
                    {
                        SIPSorcery.Sys.Log.Logger.LogWarning($"Failed to send SIP OPTIONS response. {optionsExcp.Message}");
                    }
                }
            };

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                SIPSorcery.Sys.Log.Logger.LogInformation("Exiting...");
                if(uas?.IsHungup == false) uas?.Hangup();
                uasCts?.Cancel();

                if (sipTransport != null)
                {
                    SIPSorcery.Sys.Log.Logger.LogInformation("Shutting down SIP transport...");
                    sipTransport.Shutdown();
                }
            };
        }

        private static async Task SendRecvRtp(Socket rtpSocket, RTPSession rtpSession, IPEndPoint dstRtpEndPoint, string audioFileName, CancellationTokenSource cts)
        {
            try
            {
                SIPSorcery.Sys.Log.Logger.LogDebug($"Sending from RTP socket {rtpSocket.LocalEndPoint} to {dstRtpEndPoint}.");

                // Nothing is being done with the data being received from the client. But if the remote socket data socket will
                // be switched if it differs from the one in the SDP. This helps cope with NAT.
                var rtpRecvTask = Task.Run(async () =>
                {
                    DateTime lastRecvReportAt = DateTime.Now;
                    uint packetReceivedCount = 0;
                    uint bytesReceivedCount = 0;
                    byte[] buffer = new byte[512];
                    EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

                    SIPSorcery.Sys.Log.Logger.LogDebug($"Listening on RTP socket {rtpSocket.LocalEndPoint}.");

                    var recvResult = await rtpSocket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEP);

                    while (recvResult.ReceivedBytes > 0 && !cts.IsCancellationRequested)
                    {
                        RTPPacket rtpPacket = new RTPPacket(buffer.Take(recvResult.ReceivedBytes).ToArray());

                        packetReceivedCount++;
                        bytesReceivedCount += (uint)rtpPacket.Payload.Length;

                        recvResult = await rtpSocket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEP);

                        if(DateTime.Now.Subtract(lastRecvReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                        {
                            lastRecvReportAt = DateTime.Now;
                            dstRtpEndPoint = recvResult.RemoteEndPoint as IPEndPoint;

                            SIPSorcery.Sys.Log.Logger.LogDebug($"RTP recv {rtpSocket.LocalEndPoint}<-{dstRtpEndPoint} pkts {packetReceivedCount} bytes {bytesReceivedCount}");
                        }
                    }
                });

                switch (Path.GetExtension(audioFileName).ToLower())
                {
                    case ".ulaw":
                        {
                            uint timestamp = 0;
                            using (StreamReader sr = new StreamReader(audioFileName))
                            {
                                DateTime lastSendReportAt = DateTime.Now;
                                uint packetReceivedCount = 0;
                                uint bytesReceivedCount = 0;
                                byte[] buffer = new byte[320];
                                int bytesRead = sr.BaseStream.Read(buffer, 0, buffer.Length);

                                while (bytesRead > 0 && !cts.IsCancellationRequested)
                                {
                                    packetReceivedCount++;
                                    bytesReceivedCount += (uint)bytesRead;

                                    if (!dstRtpEndPoint.Address.Equals(IPAddress.Any))
                                    {
                                        rtpSession.SendAudioFrame(rtpSocket, dstRtpEndPoint, timestamp, buffer);
                                    }

                                    timestamp += (uint)buffer.Length;

                                    if (DateTime.Now.Subtract(lastSendReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                                    {
                                        lastSendReportAt = DateTime.Now;
                                        SIPSorcery.Sys.Log.Logger.LogDebug($"RTP send {rtpSocket.LocalEndPoint}->{dstRtpEndPoint} pkts {packetReceivedCount} bytes {bytesReceivedCount}");
                                    }

                                    await Task.Delay(40, cts.Token);
                                    bytesRead = sr.BaseStream.Read(buffer, 0, buffer.Length);
                                }
                            }
                        }
                        break;

                    case ".mp3":
                        {
                            DateTime lastSendReportAt = DateTime.Now;
                            uint packetReceivedCount = 0;
                            uint bytesReceivedCount = 0;
                            var pcmFormat = new WaveFormat(8000, 16, 1);
                            var ulawFormat = WaveFormat.CreateMuLawFormat(8000, 1);

                            uint timestamp = 0;

                            using (WaveFormatConversionStream pcmStm = new WaveFormatConversionStream(pcmFormat, new Mp3FileReader(audioFileName)))
                            {
                                using (WaveFormatConversionStream ulawStm = new WaveFormatConversionStream(ulawFormat, pcmStm))
                                {
                                    byte[] buffer = new byte[320];
                                    int bytesRead = ulawStm.Read(buffer, 0, buffer.Length);

                                    while (bytesRead > 0 && !cts.IsCancellationRequested)
                                    {
                                        packetReceivedCount++;
                                        bytesReceivedCount += (uint)bytesRead;

                                        byte[] sample = new byte[bytesRead];
                                        Array.Copy(buffer, sample, bytesRead);

                                        if (dstRtpEndPoint.Address != IPAddress.Any)
                                        {
                                            rtpSession.SendAudioFrame(rtpSocket, dstRtpEndPoint, timestamp, buffer);
                                        }

                                        timestamp += (uint)buffer.Length;

                                        if (DateTime.Now.Subtract(lastSendReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                                        {
                                            lastSendReportAt = DateTime.Now;
                                            SIPSorcery.Sys.Log.Logger.LogDebug($"RTP send {rtpSocket.LocalEndPoint}->{dstRtpEndPoint} pkts {packetReceivedCount} bytes {bytesReceivedCount}");
                                        }

                                        await Task.Delay(40, cts.Token);
                                        bytesRead = ulawStm.Read(buffer, 0, buffer.Length);
                                    }
                                }
                            }
                        }
                        break;

                    default:
                        throw new NotImplementedException("Only ulaw and mp3 files are understood by this example.");
                 }
            }
            catch (OperationCanceledException) { }
            catch (Exception excp)
            {
                SIPSorcery.Sys.Log.Logger.LogError($"Exception sending RTP. {excp.Message}");
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
