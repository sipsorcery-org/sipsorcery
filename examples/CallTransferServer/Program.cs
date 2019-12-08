//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example of using a REFER request to transfer a received call.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Nov 2019	Aaron Clauson   Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
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
        // The HOMER constants are to allow logging/analysis on a HOMER server (see sipcapture.org).
        private static readonly string HOMER_SERVER_ENDPOINT = null; //"192.168.11.49:9060";
        private static readonly uint HOMER_AGENT_ID = 333;
        private static readonly string HOMER_SERVER_PASSWORD = "myHep";

        private static readonly string AUDIO_FILE_PCMU = @"media\Macroform_-_Simplicity.ulaw";
        private static readonly string TRANSFER_DESTINATION_SIP_URI = "sip:*60@192.168.11.48";  // The destination to transfer the accepted call to.
        private static readonly int TRANSFER_TIMEOUT_SECONDS = 5;                               // If transfer isn't accepted after this time assume it's failed.
        private static readonly int RTP_REPORTING_PERIOD_SECONDS = 5;       // Period at which to write RTP stats.
        private static int SIP_LISTEN_PORT = 5060;
        private static int RTP_PORT_START = 49000;
        private static int RTP_PORT_END = 49100;

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static void Main()
        {
            Console.WriteLine("SIPSorcery client user agent example.");
            Console.WriteLine("Press ctrl-c to exit.");

            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.

            AddConsoleLogger();

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            // Un/comment this line to see/hide each SIP message sent and received.
            IPEndPoint homerSvrEndPoint = !String.IsNullOrEmpty(HOMER_SERVER_ENDPOINT) ? IPEndPoint.Parse(HOMER_SERVER_ENDPOINT) : null;
            EnableTraceLogs(sipTransport, homerSvrEndPoint);

            // To keep things a bit simpler this example only supports a single call at a time and the SIP server user agent
            // acts as a singleton
            SIPUserAgent userAgent = new SIPUserAgent(sipTransport, null);
            userAgent.OnCallHungup += () =>
            {
                exitCts.Cancel();
            };
            CancellationTokenSource rtpCts = null; // Cancellation token to stop the RTP stream.
            Socket rtpSocket = null;
            Socket controlSocket = null;

            // Because this is a server user agent the SIP transport must start listening for client user agents.
            sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                try
                {
                    if (sipRequest.Header.From != null &&
                        sipRequest.Header.From.FromTag != null &&
                        sipRequest.Header.To != null &&
                        sipRequest.Header.To.ToTag != null)
                    {
                        userAgent.DialogRequestReceivedAsync(sipRequest).Wait();
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                    {
                        // If there's already a call in progress we return busy.
                        if (userAgent?.IsCallActive == true)
                        {
                            UASInviteTransaction uasTransaction = new UASInviteTransaction(sipTransport, sipRequest, null);
                            SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                            uasTransaction.SendFinalResponse(busyResponse);
                        }
                        else
                        {
                            Log.LogInformation($"Incoming call request: {localSIPEndPoint}<-{remoteEndPoint} {sipRequest.URI}.");

                            // Check there's a codec we support in the INVITE offer.
                            var offerSdp = SDP.ParseSDPDescription(sipRequest.Body);
                            IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(sipRequest.Body);
                            RTPSession rtpSession = null;
                            string audioFile = null;

                            if (offerSdp.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.HasMediaFormat((int)SDPMediaFormatsEnum.PCMU)))
                            {
                                Log.LogDebug($"Using PCMU RTP media type and audio file {AUDIO_FILE_PCMU}.");
                                rtpSession = new RTPSession((int)SDPMediaFormatsEnum.PCMU, null, null);
                                audioFile = AUDIO_FILE_PCMU;
                            }

                            if (rtpSession == null)
                            {
                                // Didn't get a match on the codecs we support.
                                UASInviteTransaction uasTransaction = new UASInviteTransaction(sipTransport, sipRequest, null);
                                SIPResponse noMatchingCodecResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.NotAcceptableHere, null);
                                uasTransaction.SendFinalResponse(noMatchingCodecResponse);
                            }
                            else
                            {
                                var uas = userAgent.AcceptCall(sipRequest);

                                rtpCts = new CancellationTokenSource();

                                // The RTP socket is listening on IPAddress.Any but the IP address placed into the SDP needs to be one the caller can reach.
                                IPAddress rtpAddress = NetServices.GetLocalAddressForRemote(dstRtpEndPoint.Address);
                                // Initialise an RTP session to receive the RTP packets from the remote SIP server.
                                NetServices.CreateRtpSocket(rtpAddress, RTP_PORT_START, RTP_PORT_END, false, out rtpSocket, out controlSocket);

                                var rtpRecvSession = new RTPSession((int)SDPMediaFormatsEnum.PCMU, null, null);
                                var rtpSendSession = new RTPSession((int)SDPMediaFormatsEnum.PCMU, null, null);
                                rtpSendSession.DestinationEndPoint = dstRtpEndPoint;
                                rtpRecvSession.OnReceiveFromEndPointChanged += (oldEP, newEP) =>
                                {
                                    Log.LogDebug($"RTP destination end point changed from {oldEP} to {newEP}.");
                                    rtpSendSession.DestinationEndPoint = newEP;
                                };

                                Task.Run(() => RecvRtp(rtpSocket, rtpRecvSession, rtpCts));
                                Task.Run(() => SendRtp(rtpSocket, rtpSendSession, rtpCts));

                                userAgent.Answer(uas, GetSDP(rtpSocket.LocalEndPoint as IPEndPoint));
                            }
                        }
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.SUBSCRIBE)
                    {
                        SIPResponse notAllowededResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                        sipTransport.SendResponse(notAllowededResponse);
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.OPTIONS || sipRequest.Method == SIPMethodsEnum.REGISTER)
                    {
                        SIPResponse optionsResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        sipTransport.SendResponse(optionsResponse);
                    }
                }
                catch (Exception reqExcp)
                {
                    Log.LogWarning($"Exception handling {sipRequest.Method}. {reqExcp.Message}");
                }
            };

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitCts.Cancel();
            };

            // At this point the call has been initiated and everything will be handled in an event handler.
            Task.Run(async () =>
            {
                try
                {
                    while (!exitCts.Token.WaitHandle.WaitOne(0))
                    {
                        var keyProps = Console.ReadKey();
                        if (keyProps.KeyChar == 't')
                        {
                            // Initiate a transfer.
                            bool transferResult = await userAgent.Transfer(SIPURI.ParseSIPURI(TRANSFER_DESTINATION_SIP_URI), new TimeSpan(0, 0, TRANSFER_TIMEOUT_SECONDS), exitCts.Token);
                            if (transferResult)
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
                    Log.LogError($"Exception Key Press listener. {excp.Message}.");
                }
            });

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitCts.Token.WaitHandle.WaitOne();

            Log.LogInformation("Exiting...");

            rtpCts?.Cancel();
            rtpSocket?.Close();
            controlSocket?.Close();

            if (userAgent?.IsCallActive == true)
            {
                Log.LogInformation($"Hanging up call to {userAgent?.CallDescriptor?.To}.");
                userAgent.Hangup();

                // Give the final request time to be transmitted.
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
                        var rtpPacket = rtpRecvSession.RtpReceive(buffer, 0, recvResult.ReceivedBytes, recvResult.RemoteEndPoint as IPEndPoint);

                        packetReceivedCount++;
                        bytesReceivedCount += (uint)rtpPacket.Payload.Length;

                        for (int index = 0; index < rtpPacket.Payload.Length; index++)
                        {
                            short pcm = NAudio.Codecs.MuLawDecoder.MuLawToLinearSample(rtpPacket.Payload[index]);
                            byte[] pcmSample = new byte[] { (byte)(pcm & 0xFF), (byte)(pcm >> 8) };
                            waveProvider.AddSamples(pcmSample, 0, 2);
                        }

                        if (DateTime.Now.Subtract(lastRecvReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                        {
                            // This is typically where RTCP receiver (RR) reports would be sent. Omitted here for brevity.
                            lastRecvReportAt = DateTime.Now;
                            var remoteRtpEndPoint = recvResult.RemoteEndPoint as IPEndPoint;
                            Log.LogDebug($"RTP recv report {rtpSocket.LocalEndPoint}<-{remoteRtpEndPoint} pkts {packetReceivedCount} bytes {bytesReceivedCount}");
                        }

                        recvResult = await rtpSocket.ReceiveFromAsync(buffer, SocketFlags.None, anyEndPoint);
                    }
                }
            }
            catch (TaskCanceledException) // Gets thrown when the task is deliberately. Can safely ignore.   
            { }
            catch (ObjectDisposedException) // This is how .Net deals with an in use socket being closed. Safe to ignore.
            { }
            catch (SocketException) // This will be thrown if the remote socket closes at the same time we tried to send. Safe to ignore.
            { }
            catch (Exception excp)
            {
                Log.LogError($"Exception RecvRTP. {excp}");
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
            try
            {
                while (cts.IsCancellationRequested == false)
                {
                    uint timestamp = 0;
                    using (StreamReader sr = new StreamReader(AUDIO_FILE_PCMU))
                    {
                        DateTime lastSendReportAt = DateTime.Now;
                        uint packetsSentCount = 0;
                        uint bytesSentCount = 0;
                        byte[] buffer = new byte[320];
                        int bytesRead = sr.BaseStream.Read(buffer, 0, buffer.Length);

                        while (bytesRead > 0 && !cts.IsCancellationRequested)
                        {
                            if (rtpSendSession.DestinationEndPoint != null)
                            {
                                packetsSentCount++;
                                bytesSentCount += (uint)bytesRead;
                                rtpSendSession.SendAudioFrame(rtpSocket, rtpSendSession.DestinationEndPoint, timestamp, buffer);
                            }

                            timestamp += (uint)buffer.Length;

                            if (DateTime.Now.Subtract(lastSendReportAt).TotalSeconds > RTP_REPORTING_PERIOD_SECONDS)
                            {
                                lastSendReportAt = DateTime.Now;
                                Log.LogDebug($"RTP send report {rtpSocket.LocalEndPoint}->{rtpSendSession.DestinationEndPoint} pkts {packetsSentCount} bytes {bytesSentCount}");
                            }

                            await Task.Delay(40, cts.Token);
                            bytesRead = sr.BaseStream.Read(buffer, 0, buffer.Length);
                        }
                    }
                }
            }
            catch (TaskCanceledException) // Gets thrown when the task is deliberately. Can safely ignore.   
            { }
            catch (ObjectDisposedException) // Gets thrown when the RTP socket is closed. Can safely ignore.
            { }
            catch (SocketException) // This will be thrown if the remote socket closes at the same time we tried to send. Safe to ignore.
            { }
            catch (Exception excp)
            {
                Log.LogError($"Exception SendRTP. {excp}");
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
            audioAnnouncement.MediaStreamStatus = MediaStreamStatusEnum.SendRecv;
            sdp.Media.Add(audioAnnouncement);

            return sdp;
        }

        /// <summary>
        /// Enable detailed SIP log messages and optionally logging to a HOMER server.
        /// </summary>
        /// <param name="sipTransport">The transport layer to display trace logs for.</param>
        /// <param name="homerSvrEP">Optional end point for a HOMER logging/diagnostics server.</param>
        private static void EnableTraceLogs(SIPTransport sipTransport, IPEndPoint homerSvrEP)
        {
            UdpClient homerClient = null;
            if (homerSvrEP != null)
            {
                homerClient = new UdpClient(0, AddressFamily.InterNetwork);
            }

            sipTransport.SIPRequestInTraceEvent += (localEP, remoteEP, req) =>
            {
                if (homerClient != null)
                {
                    var buffer = HepPacket.GetBytes(remoteEP, localEP, DateTime.Now, HOMER_AGENT_ID, HOMER_SERVER_PASSWORD, req.ToString());
                    homerClient.SendAsync(buffer, buffer.Length, homerSvrEP);
                }

                Log.LogDebug($"Request received: {localEP}<-{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPRequestOutTraceEvent += (localEP, remoteEP, req) =>
            {
                if (homerClient != null)
                {
                    // Adding a little delay to get the call flow right. It takes us longer to get the HEP packet through than the softphone.
                    var buffer = HepPacket.GetBytes(localEP, remoteEP, DateTime.Now.Subtract(TimeSpan.FromMilliseconds(100)), HOMER_AGENT_ID, HOMER_SERVER_PASSWORD, req.ToString());
                    homerClient.SendAsync(buffer, buffer.Length, homerSvrEP);
                }

                Log.LogDebug($"Request sent: {localEP}->{remoteEP}");
                Log.LogDebug(req.ToString());
            };

            sipTransport.SIPResponseInTraceEvent += (localEP, remoteEP, resp) =>
            {
                if (homerClient != null)
                {
                    var buffer = HepPacket.GetBytes(remoteEP, localEP, DateTime.Now, HOMER_AGENT_ID, HOMER_SERVER_PASSWORD, resp.ToString());
                    homerClient.SendAsync(buffer, buffer.Length, homerSvrEP);
                }

                Log.LogDebug($"Response received: {localEP}<-{remoteEP}");
                Log.LogDebug(resp.ToString());
            };

            sipTransport.SIPResponseOutTraceEvent += (localEP, remoteEP, resp) =>
            {
                if (homerClient != null)
                {
                    // Adding a little delay to get the call flow right. It takes us longer to get the HEP packet through than the softphone.
                    var buffer = HepPacket.GetBytes(localEP, remoteEP, DateTime.Now.Subtract(TimeSpan.FromMilliseconds(100)), HOMER_AGENT_ID, HOMER_SERVER_PASSWORD, resp.ToString());
                    homerClient.SendAsync(buffer, buffer.Length, homerSvrEP);
                }

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
