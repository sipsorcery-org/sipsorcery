//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to place a SIP call
// and then place it on and off hold.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 25 Nov 2019	Aaron Clauson	Created, Dublin, Ireland.
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
        /// <summary>
        /// The different call on hold categories that we understand.
        /// </summary>
        enum HoldStatus
        {
            None,
            WePutOnHold,        // We put the remote party on hold.
            RemotePutOnHold     // The remote part put us on hold.
        };

        private static readonly string DEFAULT_DESTINATION_SIP_URI = "sip:7003@192.168.11.48";
        private static readonly string SIP_USERNAME = "7001";
        private static readonly string SIP_PASSWORD = "password";
        private static readonly int RTP_REPORTING_PERIOD_SECONDS = 5;       // Period at which to write RTP stats.

        private static readonly string RTP_ATTRIBUTE_SENDRECV = "sendrecv"; // 2-way media stream.
        private static readonly string RTP_ATTRIBUTE_SENDONLY = "sendonly"; // The SIP endpoint would only send and not receive media.
        private static readonly string RTP_ATTRIBUTE_RECVONLY = "recvonly"; // The SIP endpoint would only receive (listen mode) and not send media.
        //private static readonly string RTP_ATTRIBUTE_INACTIVE = "inactive"; // The SIP endpoint would neither send nor receive media.

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        private static SIPTransport _sipTransport;
        private static IPEndPoint _remoteRtpEndPoint;
        private static SDP _ourSDP;
        private static Socket _ourRtpSocket;
        private static HoldStatus _holdStatus;

        static void Main(string[] args)
        {
            Console.WriteLine("SIPSorcery call hold example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP trnasport and RTP stream.
            bool isCallHungup = false;
            bool hasCallFailed = false;

            AddConsoleLogger();

            // Check whether an override desination has been entered on the command line.
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
            _sipTransport = new SIPTransport();
            _sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0)));

            EnableTraceLogs(_sipTransport);

            var lookupResult = SIPDNSManager.ResolveSIPService(callUri, false);
            Log.LogDebug($"DNS lookup result for {callUri}: {lookupResult?.GetSIPEndPoint()}.");
            var dstAddress = lookupResult.GetSIPEndPoint().Address;

            IPAddress localIPAddress = NetServices.GetLocalAddressForRemote(dstAddress);

            // Initialise an RTP session to receive the RTP packets from the remote SIP server.
            _ourRtpSocket = null;
            Socket controlSocket = null;
            NetServices.CreateRtpSocket(localIPAddress, 48000, 48100, false, out _ourRtpSocket, out controlSocket);
            var rtpRecvSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);
            var rtpSendSession = new RTPSession((int)RTPPayloadTypesEnum.PCMU, null, null);

            _ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDRECV);

            // Create a client/server user agent to place a call to a remote SIP server along with event handlers for the different stages of the call.
            var userAgent = new SIPUserAgent(_sipTransport, null);

            userAgent.ClientCallTrying += (uac, resp) =>
            {
                Log.LogInformation($"{uac.CallDescriptor.To} Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
            };
            userAgent.ClientCallRinging += (uac, resp) => Log.LogInformation($"{uac.CallDescriptor.To} Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
            userAgent.ClientCallFailed += (uac, err) =>
            {
                Log.LogWarning($"{uac.CallDescriptor.To} Failed: {err}");
                hasCallFailed = true;
                exitCts.Cancel();
            };
            userAgent.ClientCallAnswered += (uac, resp) =>
            {
                if (resp.Status == SIPResponseStatusCodesEnum.Ok)
                {
                    Log.LogInformation($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");

                    // Only set the remote RTP end point if there hasn't already been a packet received on it.
                    if (_remoteRtpEndPoint == null)
                    {
                        _remoteRtpEndPoint = SDP.GetSDPRTPEndPoint(resp.Body);
                        Log.LogDebug($"Remote RTP socket {_remoteRtpEndPoint}.");
                    }
                }
                else
                {
                    Log.LogWarning($"{uac.CallDescriptor.To} Answered: {resp.StatusCode} {resp.ReasonPhrase}.");
                }
            };
            userAgent.CallHungup += () =>
            {
                Log.LogInformation($"Call hungup by remote party.");
                exitCts.Cancel();
            };
            userAgent.CallRemoteSDPChanged += RemoteSDPChanged;

            // The only incoming request that needs to be explicitly handled for this example is if the remote end hangs up the call.
            // Only new non-transaction requests need to be handled here.  
            _sipTransport.SIPTransportRequestReceived += (SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) =>
            {
                try
                {
                    if (sipRequest.Header.From != null &&
                        sipRequest.Header.From.FromTag != null &&
                        sipRequest.Header.To != null &&
                        sipRequest.Header.To.ToTag != null)
                    {
                        userAgent.InCallRequestReceivedAsync(sipRequest).Wait();
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.INVITE)
                    {
                        // New call request for the User Agent Server.
                    }
                    else if (sipRequest.Method == SIPMethodsEnum.OPTIONS)
                    {
                        SIPResponse optionsResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                        _sipTransport.SendResponse(optionsResponse);
                    }
                }
                catch (Exception excp)
                {
                    Log.LogError($"Exception processing request. {excp.Message}");
                }
            };

            // It's a good idea to start the RTP receiving socket before the call request is sent.
            // A SIP server will generally start sending RTP as soon as it has processed the incoming call request and
            // being ready to receive will stop any ICMP error response being generated.
            Task.Run(() => RecvRtp(_ourRtpSocket, rtpRecvSession, exitCts));
            Task.Run(() => SendRtp(_ourRtpSocket, rtpSendSession, exitCts));

            // Start the thread that places the call.
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(
                SIP_USERNAME,
                SIP_PASSWORD,
                callUri.ToString(),
                $"sip:{SIP_USERNAME}@localhost",
                callUri.CanonicalAddress,
                null, null, null,
                SIPCallDirection.Out,
                SDP.SDP_MIME_CONTENTTYPE,
                _ourSDP.ToString(),
                null);

            userAgent.Call(callDescriptor);

            // At this point the call has been initiated and everything will be handled in an event handler.
            Task.Run(() =>
            {
                try
                {
                    while (!exitCts.Token.WaitHandle.WaitOne(0))
                    {
                        var keyProps = Console.ReadKey();
                        if (keyProps.KeyChar == 'h')
                        {
                            // Place call on/off hold.
                            if(userAgent.IsAnswered)
                            {
                                if (_holdStatus == HoldStatus.None)
                                {
                                    Log.LogInformation("Placing the remote call party on hold.");
                                    _holdStatus = HoldStatus.WePutOnHold;
                                    _ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDONLY);
                                    userAgent.SendReInviteRequest(_ourSDP);
                                }
                                else if(_holdStatus == HoldStatus.WePutOnHold)
                                {
                                    Log.LogInformation("Removing the remote call party from hold.");
                                    _holdStatus = HoldStatus.None;
                                    _ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDRECV);
                                    userAgent.SendReInviteRequest(_ourSDP);
                                }
                                else
                                {
                                    Log.LogInformation("Sorry we're already on hold by the remote call party.");
                                }
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
                    SIPSorcery.Sys.Log.Logger.LogError($"Exception Key Press listener. {excp.Message}.");
                }
            });

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitCts.Cancel();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitCts.Token.WaitHandle.WaitOne();

            #region Cleanup.

            Log.LogInformation("Exiting...");

            _ourRtpSocket?.Close();
            controlSocket?.Close();

            if (!isCallHungup && userAgent != null)
            {
                if (userAgent.IsAnswered)
                {
                    Log.LogInformation($"Hanging up call to {userAgent?.CallDescriptor?.To}.");
                    userAgent.Hangup();
                }
                else if (!hasCallFailed)
                {
                    Log.LogInformation($"Cancelling call to {userAgent?.CallDescriptor?.To}.");
                    userAgent.Cancel();
                }

                // Give the BYE or CANCEL request time to be transmitted.
                Log.LogInformation("Waiting 1s for call to clean up...");
                Task.Delay(1000).Wait();
            }

            SIPSorcery.Net.DNSManager.Stop();

            if (_sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                _sipTransport.Shutdown();
            }

            #endregion
        }

        /// <summary>
        /// Event handler for receiving a re-INVITE request from the remote call party.
        /// </summary>
        /// <param name="sipRequest">The re-INVITE request.</param>
        /// <param name="originalSDP">The SDP from the ooriginal call request.</param>
        /// <param name="newSDP">The SDP from the re-INVITE request.</param>
        private static void RemoteSDPChanged(SIPRequest sipRequest, SDP originalSDP, SDP newSDP)
        {
            // A new SDP received can mean a few different things. The remote RTP socket has changed,
            // the call has been put on/off hold, codec changed etc. This logic could need to be fairly
            // sophisiticated to determine the course of action. What's shown here is only a simple example
            // handling some common scenarios.

            // Re-INVITEs can also be changing the RTP end point. We can update this each time.
            IPEndPoint dstRtpEndPoint = SDP.GetSDPRTPEndPoint(sipRequest.Body);
            _remoteRtpEndPoint = dstRtpEndPoint;

            // If the RTP callfow attribute has changed it's most likely due to being placed on/off hold.
            if (newSDP.Media.First().ExtraAttributes.Any(x => x.Contains($"a={RTP_ATTRIBUTE_SENDONLY}")))
            {
                Log.LogInformation("Remote call party has placed us on hold.");
                _holdStatus = HoldStatus.RemotePutOnHold;

                _ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_RECVONLY);
                var okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                okResponse.Body = _ourSDP.ToString();
                _sipTransport.SendResponse(okResponse);
            }
            else if (newSDP.Media.First().ExtraAttributes.Any(x => x.Contains($"a={RTP_ATTRIBUTE_SENDRECV}")) &&
                !originalSDP.Media.First().ExtraAttributes.Any(x => x.Contains($"a={RTP_ATTRIBUTE_SENDRECV}")))
            {
                Log.LogInformation("Remote call party has taken us off hold.");
                _holdStatus = HoldStatus.None;

                _ourSDP = GetSDP(_ourRtpSocket.LocalEndPoint as IPEndPoint, RTP_ATTRIBUTE_SENDRECV);
                var okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                okResponse.Body = _ourSDP.ToString();
                _sipTransport.SendResponse(okResponse);
            }
            else
            {
                Log.LogWarning("Not sure what the remote call party wants us to do...");

                // We'll just reply Ok and hope eveything is good.
                var okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                okResponse.Body = _ourSDP.ToString();
                _sipTransport.SendResponse(okResponse);
            }
        }

        /// <summary>
        /// Handling packets received on the RTP socket. One of the simplest, if not the simplest, cases, is
        /// PCMU audio packets. The handling can get substantially more complicated if the RTP socket is being
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

                    if (_remoteRtpEndPoint == null || !recvResult.RemoteEndPoint.Equals(_remoteRtpEndPoint))
                    {
                        _remoteRtpEndPoint = recvResult.RemoteEndPoint as IPEndPoint;
                        Log.LogDebug($"Adjusting remote RTP end point for sends adjusted to {_remoteRtpEndPoint}.");
                    }

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
            catch (SocketException sockExcp)
            {
                Log.LogWarning($"RecvRTP socket error {sockExcp.SocketErrorCode}");
            }
            catch (ObjectDisposedException) { } // This is how .Net deals with an in use socket being closed. Safe to ignore.
            catch (Exception excp)
            {
                Log.LogError($"Exception RecvRTP. {excp.Message}");
            }
        }

        private static void SendRtp(Socket rtpSocket, RTPSession rtpSendSession, CancellationTokenSource cts)
        {
            try
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
                            if (_holdStatus != HoldStatus.RemotePutOnHold)
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
                            }
                        };

                        waveInEvent.StartRecording();

                        cts.Token.WaitHandle.WaitOne();
                    }
                }
            }
            catch (SocketException sockExcp)
            {
                Log.LogWarning($"SendRTP socket error {sockExcp.SocketErrorCode}");
            }
            catch (ObjectDisposedException) { } // This is how .Net deals with an in use socket being closed. Safe to ignore.
            catch (Exception excp)
            {
                Log.LogError($"Exception SendRTP. {excp.Message}");
            }
        }

        private static SDP GetSDP(IPEndPoint rtpSocket, string rtpFlowAttribute)
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
            audioAnnouncement.ExtraAttributes.Add($"a={rtpFlowAttribute}");
            sdp.Media.Add(audioAnnouncement);

            return sdp;
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
