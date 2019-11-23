//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// act as the server for a SIP call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 09 Oct 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// This example can be used with the automated SIP test tool [SIPp] (https://github.com/SIPp/sipp)
// and its inbuilt User Agent Client scenario.
// Note: IPp doesn't support IPv6.
//
// To isntall on WSL:
// $ sudo apt install sip-tester
//
// Running tests (press the '+' key while test is running to increase the call rate):
// For UDP testing: sipp -sn uac 127.0.0.1
// For TCP testing: sipp -sn uac localhost -t t1
//-----------------------------------------------------------------------------

//-----------------------------------------------------------------------------
// Media files:
// The "Simplicity" audio used in this example is from an artist called MACROFORM
// and can be downloaded directly from: https://www.jamendo.com/track/579315/simplicity?language=en
// The use of the audio is licensed under the Creative Commons 
// https://creativecommons.org/licenses/by-nd/2.0/
// The audio is free for personal use but a license may be required for commerical use.
// If it sounds familair this particular file is also included as part of Asterisk's 
// (asterisk.org) music on hold.
//
// ffmpeg can be used to convert the mp3 file into the required format for placing directly 
// into the RTP packets. Currently this example supports two audio formats: G711.ULAW (or PCMU)
// and G722.
//
// ffmpeg -i Macroform_-_Simplicity.mp3 -ac 1 -ar 8k -ab 64k -f mulaw Macroform_-_Simplicity.ulaw
// ffmpeg -i Macroform_-_Simplicity.mp3 -ar 16k -acodec g722 Macroform_-_Simplicity.g722
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
        private static readonly string AUDIO_FILE_PCMU = @"media\Macroform_-_Simplicity.ulaw";
        //private static readonly string AUDIO_FILE_PCMU = @"media\the_simplicity.ulaw";
        //private static readonly string AUDIO_FILE_MP3 = @"media\Macroform_-_Simplicity.mp3";
        private static readonly string AUDIO_FILE_G722 = @"media\Macroform_-_Simplicity.g722";

        private static readonly int RTP_REPORTING_PERIOD_SECONDS = 5;       // Period at which to write RTP stats.
        private static int SIP_LISTEN_PORT = 5060;
        private static int SIPS_LISTEN_PORT = 5061;
        private static int RTP_PORT_START = 49000;
        private static int RTP_PORT_END = 49100;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery user agent server example.");
            Console.WriteLine("Press h to hangup a call or ctrl-c to exit.");

            EnableConsoleLogger();

            IPAddress listenAddress = IPAddress.Any;
            IPAddress listenIPv6Address = IPAddress.IPv6Any;
            if (args != null && args.Length > 0)
            {
                if (!IPAddress.TryParse(args[0], out var customListenAddress))
                {
                    Log.LogWarning($"Command line argument could not be parsed as an IP address \"{args[0]}\"");
                    listenAddress = IPAddress.Any;
                }
                else
                {
                    if (customListenAddress.AddressFamily == AddressFamily.InterNetwork) listenAddress = customListenAddress;
                    if (customListenAddress.AddressFamily == AddressFamily.InterNetworkV6) listenIPv6Address = customListenAddress;
                }
            }

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();

            // IPv4 channels.
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(listenAddress, SIP_LISTEN_PORT)));
            sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(listenAddress, SIP_LISTEN_PORT)));
            sipTransport.AddSIPChannel(new SIPTLSChannel(new X509Certificate2("localhost.pfx"), new IPEndPoint(listenAddress, SIPS_LISTEN_PORT)));

            // IPv6 channels.
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(listenIPv6Address, SIP_LISTEN_PORT)));
            sipTransport.AddSIPChannel(new SIPTCPChannel(new IPEndPoint(listenIPv6Address, SIP_LISTEN_PORT)));
            sipTransport.AddSIPChannel(new SIPTLSChannel(new X509Certificate2("localhost.pfx"), new IPEndPoint(listenIPv6Address, SIPS_LISTEN_PORT)));

            EnableTraceLogs(sipTransport);

            // To keep things a bit simpler this example only supports a single call at a time and the SIP server user agent
            // acts as a singleton
            SIPServerUserAgent uas = null;
            CancellationTokenSource rtpCts = null; // Cancellation token to stop the RTP stream.
            Socket rtpSocket = null;
            Socket controlSocket = null;

            // Because this is a server user agent the SIP transport must start listening for client user agents.
            sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                try
                {
                    if (sipRequest.Method == SIPMethodsEnum.INVITE)
                    {
                        SIPSorcery.Sys.Log.Logger.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

                        // Check there's a codec we support in the INVITE offer.
                        var offerSdp = SDP.ParseSDPDescription(sipRequest.Body);
                        IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(sipRequest.Body);
                        RTPSession rtpSession = null;
                        string audioFile = null;

                        if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.HasMediaFormat((int)RTPPayloadTypesEnum.G722)))
                        {
                            Log.LogDebug($"Using G722 RTP media type and audio file {AUDIO_FILE_G722}.");
                            rtpSession = new RTPSession((int)RTPPayloadTypesEnum.G722, null, null);
                            audioFile = AUDIO_FILE_G722;
                        }
                        else if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.HasMediaFormat((int)RTPPayloadTypesEnum.PCMU)))
                        {
                            Log.LogDebug($"Using PCMU RTP media type and audio file {AUDIO_FILE_PCMU}.");
                            rtpSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);
                            audioFile = AUDIO_FILE_PCMU;
                        }

                        if (rtpSession == null)
                        {
                            // Didn't get a match on the codecs we support.
                            SIPResponse noMatchingCodecResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptableHere, null);
                            sipTransport.SendResponse(noMatchingCodecResponse);
                        }
                        else
                        {
                            // If there's already a call in progress hang it up. Of course this is not ideal for a real softphone or server but it 
                            // means this example can be kept simpler.
                            if (uas?.IsHungup == false) uas?.Hangup(false);
                            rtpCts?.Cancel();

                            UASInviteTransaction uasTransaction = sipTransport.CreateUASTransaction(sipRequest, null);
                            uas = new SIPServerUserAgent(sipTransport, null, null, null, SIPCallDirection.In, null, null, null, uasTransaction);
                            rtpCts = new CancellationTokenSource();

                            uas.Progress(SIPResponseStatusCodesEnum.Trying, null, null, null, null);
                            uas.Progress(SIPResponseStatusCodesEnum.Ringing, null, null, null, null);

                            // Initialise an RTP session to receive the RTP packets from the remote SIP server.
                            NetServices.CreateRtpSocket(dstRtpEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, RTP_PORT_START, RTP_PORT_END, false, out rtpSocket, out controlSocket);

                            // The RTP socket is listening on IPAddress.Any but the IP address placed into the SDP needs to be one the caller can reach.
                            IPAddress rtpAddress = NetServices.GetLocalAddressForRemote(dstRtpEndPoint.Address);
                            IPEndPoint rtpEndPoint = new IPEndPoint(rtpAddress, (rtpSocket.LocalEndPoint as IPEndPoint).Port);

                            var rtpTask = Task.Run(() => SendRecvRtp(rtpSocket, rtpSession, dstRtpEndPoint, audioFile, rtpCts))
                                .ContinueWith(_ => { if (uas?.IsHungup == false) uas?.Hangup(false); });

                            uas.Answer(SDP.SDP_MIME_CONTENTTYPE, GetSDP(rtpEndPoint).ToString(), null, SIPDialogueTransferModesEnum.NotAllowed);
                        }
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.BYE)
                    {
                        SIPSorcery.Sys.Log.Logger.LogInformation("Call hungup.");
                        SIPNonInviteTransaction byeTransaction = sipTransport.CreateNonInviteTransaction(sipRequest, null);
                        SIPResponse byeResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        byeTransaction.SendFinalResponse(byeResponse);
                        uas?.Hangup(true);
                        rtpCts?.Cancel();
                        rtpSocket?.Close();
                        controlSocket?.Close();
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.SUBSCRIBE)
                    {
                        SIPResponse notAllowededResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                        sipTransport.SendResponse(notAllowededResponse);
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.OPTIONS || sipRequest.Method == SIPMethodsEnum.REGISTER)
                    {
                        SIPResponse optionsResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        sipTransport.SendResponse(optionsResponse);
                    }
                }
                catch (Exception reqExcp)
                {
                    SIPSorcery.Sys.Log.Logger.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
                }
            };

            ManualResetEvent exitMre = new ManualResetEvent(false);

            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                SIPSorcery.Sys.Log.Logger.LogInformation("Exiting...");

                Hangup(uas).Wait();

                rtpCts?.Cancel();
                rtpSocket?.Close();
                controlSocket?.Close();

                if (sipTransport != null)
                {
                    SIPSorcery.Sys.Log.Logger.LogInformation("Shutting down SIP transport...");
                    sipTransport.Shutdown();
                }

                exitMre.Set();
            };

            Task.Run(() =>
            {
                try
                {
                    while (!exitMre.WaitOne(0))
                    {
                        var keyProps = Console.ReadKey();
                        if (keyProps.KeyChar == 'h' || keyProps.KeyChar == 'q')
                        {
                            Console.WriteLine();
                            Console.WriteLine("Hangup requested by user...");

                            Hangup(uas).Wait();

                            rtpCts?.Cancel();
                            rtpSocket?.Close();
                            controlSocket?.Close();
                        }

                        if(keyProps.KeyChar == 'q')
                        {
                            SIPSorcery.Sys.Log.Logger.LogInformation("Quitting...");

                            if (sipTransport != null)
                            {
                                SIPSorcery.Sys.Log.Logger.LogInformation("Shutting down SIP transport...");
                                sipTransport.Shutdown();
                            }

                            exitMre.Set();
                        }
                    }
                }
                catch(Exception excp)
                {
                    SIPSorcery.Sys.Log.Logger.LogError($"Exception Key Press listener. {excp.Message}.");
                }
            });

            exitMre.WaitOne();
        }

        private static async Task SendRecvRtp(Socket rtpSocket, RTPSession rtpSession, IPEndPoint dstRtpEndPoint, string audioFileName, CancellationTokenSource cts)
        {
            try
            {
                SIPSorcery.Sys.Log.Logger.LogDebug($"Sending from RTP socket {rtpSocket.LocalEndPoint} to {dstRtpEndPoint}.");

                // Nothing is being done with the data being received from the client. But the remote rtp socket will
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

                        if (DateTime.Now.Subtract(lastRecvReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                        {
                            lastRecvReportAt = DateTime.Now;
                            dstRtpEndPoint = recvResult.RemoteEndPoint as IPEndPoint;

                            SIPSorcery.Sys.Log.Logger.LogDebug($"RTP recv {rtpSocket.LocalEndPoint}<-{dstRtpEndPoint} pkts {packetReceivedCount} bytes {bytesReceivedCount}");
                        }
                    }
                });

                string audioFileExt = Path.GetExtension(audioFileName).ToLower();

                switch (audioFileExt)
                {
                    case ".g722":
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
                        throw new NotImplementedException($"The {audioFileExt} file type is not understood by this example.");
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
                MediaFormats = new List<SDPMediaFormat>() { new SDPMediaFormat((int)SDPMediaFormatsEnum.PCMU, "PCMU", 8000),
                                                            new SDPMediaFormat((int)SDPMediaFormatsEnum.G722, "G722", 8000) }
            };
            audioAnnouncement.Port = rtpSocket.Port;
            audioAnnouncement.ExtraAttributes.Add("a=sendrecv");
            sdp.Media.Add(audioAnnouncement);

            return sdp;
        }

        /// <summary>
        /// Hangs up the current call.
        /// </summary>
        /// <param name="uas">The user agent server to hangup the call on.</param>
        private static async Task Hangup(SIPServerUserAgent uas)
        {
            try
            {
                if (uas?.IsHungup == false)
                {
                    uas?.Hangup(false);

                    // Give the BYE or CANCEL request time to be transmitted.
                    SIPSorcery.Sys.Log.Logger.LogInformation("Waiting 1s for call to hangup...");
                    await Task.Delay(1000);
                }
            }
            catch(Exception excp)
            {
                SIPSorcery.Sys.Log.Logger.LogError($"Exception Hangup. {excp.Message}");
            }
        }

        /// <summary>
        /// Wires up the dotnet logging infrastructure to STDOUT.
        /// </summary>
        private static void EnableConsoleLogger()
        {
            // Logging configuration. Can be ommitted if internal SIPSorcery debug and warning messages are not required.
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
