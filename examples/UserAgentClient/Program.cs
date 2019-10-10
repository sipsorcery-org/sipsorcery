//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to place an audio call to
// a SIP server and play the received audio. 
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
using System.Net;
using System.Net.Sockets;
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
            Console.WriteLine("SIPSorcery client user agent example.");
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

            // If your default DNS server supports SRV records there is no need to set a specific DNS server.
            DNSManager.SetDNSServers(new List<IPEndPoint> { IPEndPoint.Parse("8.8.8.8:53") });

            IPAddress defaultAddr = LocalIPConfig.GetDefaultIPv4Address();

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());
            int port = FreePort.FindNextAvailableUDPPort(SIPConstants.DEFAULT_SIP_PORT);
            var sipChannel = new SIPUDPChannel(new IPEndPoint(defaultAddr, port));
            sipTransport.AddSIPChannel(sipChannel);

            // Create a client user agent to place a call to a remote SIP server.
            var clientUserAgent = new SIPClientUserAgent(sipTransport);

            // Initialise an RTP session to receive the RTP packets from the remote SIP server.
            Socket rtpSocket = null;
            Socket controlSocket = null;
            NetServices.CreateRtpSocket(defaultAddr, 49002, 49100, false, out rtpSocket, out controlSocket);

            //NAudio.Wave.WaveO waveOut = new WaveOut();
            //BufferedWaveProvider waveProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1));
            //waveProvider.DiscardOnBufferOverflow = true;
            //waveProvider.BufferLength = 100000;
            //WaveOutEvent waveOutEvent = new WaveOutEvent();
            //waveOutEvent.Init(waveProvider);
            //waveOutEvent.Play();
            //m_waveOut.Init(m_waveProvider);
            //m_waveOut.Play();

            var rtpTask = Task.Run(() =>
            {
                byte[] buffer = new byte[512];
                var rtpSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);

                SIPSorcery.Sys.Log.Logger.LogDebug($"Listening on RTP socket {rtpSocket.LocalEndPoint}.");

                using (var waveOutEvent = new WaveOutEvent())
                {
                    var waveProvider = new BufferedWaveProvider(new WaveFormat(8000, 16, 1));
                    waveOutEvent.Init(waveProvider);
                    waveOutEvent.Play();

                    while (true)
                    {
                        int bytesRead = rtpSocket.Receive(buffer);
                        Console.WriteLine($"Read {bytesRead} from remote RTP socket.");

                        for (int index = 0; index < bytesRead; index++)
                        {
                            short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(buffer[index]);
                            byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                            waveProvider.AddSamples(pcmSample, 0, 2);
                        }
                    }
                }
            });

            // Event handlers for the different stages of the call.
            clientUserAgent.CallTrying += (uac, resp) => SIPSorcery.Sys.Log.Logger.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
            clientUserAgent.CallRinging += (uac, resp) => SIPSorcery.Sys.Log.Logger.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
            clientUserAgent.CallFailed += (uac, err) => SIPSorcery.Sys.Log.Logger.LogError($"{uac.CallDescriptor.To} Failed: {err}");
            clientUserAgent.CallAnswered += async (uac, resp) =>
            {
                SIPSorcery.Sys.Log.Logger.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                IPEndPoint remoteRtpEndPoint = SDP.GetSDPRTPEndPoint(resp.Body);

                SIPSorcery.Sys.Log.Logger.LogDebug($"Sending dummy packet to remote RTP socket {remoteRtpEndPoint}.");

                // Send a dummy packet to a NAT session on the RTP path.
                await rtpSocket.SendToAsync(new byte[] { 0x00 }, SocketFlags.None, remoteRtpEndPoint);

                //await Task.Delay(5000);

                //SIPSorcery.Sys.Log.Logger.LogDebug($"Hanging up call to {uac.CallDescriptor.To}.");

                //(uac as SIPClientUserAgent).Hangup();
            };

            //IPEndPoint rtpPublicEndPoint = new IPEndPoint(IPAddress.Parse("37.228.253.95").Address, (rtpSocket.LocalEndPoint as IPEndPoint).Port);
            IPEndPoint rtpPublicEndPoint = new IPEndPoint(defaultAddr, (rtpSocket.LocalEndPoint as IPEndPoint).Port);

            // Start the thread that places the call.
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
                SIPConstants.SIP_DEFAULT_USERNAME,
                null,
                //"sip:music@iptel.org",
                //"sip:music@212.79.111.155",
                //"sip:500@sipsorcery.com",
                //"sip:*061742926@sipbroker.com",
                "sip:100@192.168.0.50",
                //"sip:100@67.222.131.146",
                SIPConstants.SIP_DEFAULT_FROMURI,
                null, null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                GetSDP(rtpPublicEndPoint).ToString(),
                null);

            clientUserAgent.Call(callDescriptor);
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

            return sdp;
        }
    }
}
