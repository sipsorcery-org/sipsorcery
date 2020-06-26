//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A sample program to interop with the sip.js
// javascript SIP library, see https://sipjs.com/.
//
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia;

namespace demo
{
    class Program
    {
        private const string DTLS_CERTIFICATE_PATH = "certs/localhost.pem";
        private const string DTLS_KEY_PATH = "certs/localhost_key.pem";
        private const string DTLS_CERTIFICATE_FINGERPRINT = "sha-256 C6:ED:8C:9D:06:50:77:23:0A:4A:D8:42:68:29:D0:70:2F:BB:C7:72:EC:98:5C:62:07:1B:0C:5D:CB:CE:BE:CD";

        private static Microsoft.Extensions.Logging.ILogger Log = SIPSorcery.Sys.Log.Logger;

        static async Task Main()
        {
            Console.WriteLine("SIPSorcery sip.js Demo");

            AddConsoleLogger();

            var sipTransport = new SIPTransport();
            EnableTraceLogs(sipTransport);

            var sipChannel = new SIPWebSocketChannel(IPAddress.Loopback, 80);

            var wssCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2("localhost.pfx");
            var sipChannelSecure = new SIPWebSocketChannel(IPAddress.Loopback, 443, wssCertificate);

            sipTransport.AddSIPChannel(sipChannel);
            sipTransport.AddSIPChannel(sipChannelSecure);

            var userAgent = new SIPUserAgent(sipTransport, null, true);
            userAgent.OnIncomingCall += async (ua, req) =>
            {
                Log.LogDebug($"Auto-answering incoming call from {req.Header.From}.");
                var uas = userAgent.AcceptCall(req);

                RTCConfiguration pcConfiguration = new RTCConfiguration
                {
                    certificates = new List<RTCCertificate>
                {
                    new RTCCertificate
                    {
                        X_CertificatePath = DTLS_CERTIFICATE_PATH,
                        X_KeyPath = DTLS_KEY_PATH,
                        X_Fingerprint = DTLS_CERTIFICATE_FINGERPRINT
                    }
                },
                    //X_RemoteSignallingAddress = context.UserEndPoint.Address,
                    //iceServers = new List<RTCIceServer> { new RTCIceServer { urls = SIPSORCERY_STUN_SERVER } }
                };

                var peerConnection = new RTCPeerConnection(pcConfiguration);
                var dtls = new DtlsHandshake(DTLS_CERTIFICATE_PATH, DTLS_KEY_PATH);

                peerConnection.OnTimeout += (mediaType) =>
                {
                    peerConnection.Close("remote timeout");
                };

                peerConnection.oniceconnectionstatechange += async (state) =>
                {
                    Log.LogDebug($"ICE connection state change to {state}.");

                    if (state == RTCIceConnectionState.connected)
                    {
                        var remoteEndPoint = peerConnection.AudioDestinationEndPoint;
                       Log.LogInformation($"ICE connected to remote end point {remoteEndPoint}.");

                        await Task.Run(() => DoDtlsHandshake(peerConnection, dtls))
                        .ContinueWith((dtlsResult) =>
                        {
                            Log.LogDebug($"dtls handshake result {dtlsResult.Result}.");

                            if (dtlsResult.Result)
                            {
                                var remoteEP = peerConnection.AudioDestinationEndPoint;
                                peerConnection.SetDestination(SDPMediaTypesEnum.audio, remoteEP, remoteEP);
                            }
                            else
                            {
                                dtls.Shutdown();
                                peerConnection.Close("dtls handshake failed.");
                            }
                        });
                    }
                };

                peerConnection.onconnectionstatechange += (state) =>
                {
                    if (state == RTCPeerConnectionState.connected)
                    {
                        var remoteEP = peerConnection.AudioDestinationEndPoint;

                        Log.LogDebug($"DTLS connected on {remoteEP}.");

                        peerConnection.SetDestination(SDPMediaTypesEnum.audio, remoteEP, remoteEP);
                        peerConnection.SetDestination(SDPMediaTypesEnum.video, remoteEP, remoteEP);

                        peerConnection.OnReceiveReport += RtpSession_OnReceiveReport;
                        peerConnection.OnSendReport += RtpSession_OnSendReport;
                        // peerConnection.OnRtpPacketReceived += OnRtpPacketReceived;
                    }
                };

                MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendRecv);
                peerConnection.addTrack(audioTrack);
                //MediaStreamTrack videoTrack = new MediaStreamTrack("1", SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.Inactive);
                //peerConnection.addTrack(videoTrack);

                var answerResult = await userAgent.Answer(uas, peerConnection);
            };

            Console.Write("press any key to exit...");
            Console.Read();

            sipTransport.Shutdown();
        }

        private static void OnRtpPacketReceived(SDPMediaTypesEnum media, RTPPacket pkt)
        {
            Log.LogDebug($"RTP {media} packet received, ssrc {pkt.Header.SyncSource}, seqnum {pkt.Header.SequenceNumber}, timestamp {pkt.Header.Timestamp}.");
        }

        /// <summary>
        /// Hands the socket handle to the DTLS context and waits for the handshake to complete.
        /// </summary>
        /// <param name="webRtcSession">The WebRTC session to perform the DTLS handshake on.</param>
        private static bool DoDtlsHandshake(RTCPeerConnection peerConnection, DtlsHandshake dtls)
        {
            Log.LogDebug("DoDtlsHandshake started.");

            if (!File.Exists(DTLS_CERTIFICATE_PATH))
            {
                throw new ApplicationException($"The DTLS certificate file could not be found at {DTLS_CERTIFICATE_PATH}.");
            }
            else if (!File.Exists(DTLS_KEY_PATH))
            {
                throw new ApplicationException($"The DTLS key file could not be found at {DTLS_KEY_PATH}.");
            }

            byte[] clientFingerprint = null;
            var dtlsResult = dtls.DoHandshakeAsServer((ulong)peerConnection.GetRtpChannel(SDPMediaTypesEnum.audio).RtpSocket.Handle, ref clientFingerprint);

            Log.LogDebug($"DtlsContext initialisation result {dtlsResult}.");

            if (dtls.IsHandshakeComplete())
            {
                Log.LogDebug("DTLS negotiation complete.");

                var srtpSendContext = new Srtp(dtls, false);
                var srtpReceiveContext = new Srtp(dtls, true);

                peerConnection.SetSecurityContext(
                    srtpSendContext.ProtectRTP,
                    srtpReceiveContext.UnprotectRTP,
                    srtpSendContext.ProtectRTCP,
                    srtpReceiveContext.UnprotectRTCP);

                return true;
            }
            else
            {
                Log.LogWarning("DTLS handshake failed.");

                dtls.Shutdown();
                return false;
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.Bye != null)
            {
                Log.LogDebug($"RTCP sent BYE {mediaType}.");
            }
            else if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                Log.LogDebug($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                if (sentRtcpReport.ReceiverReport.ReceptionReports?.Count > 0)
                {
                    var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                    Log.LogDebug($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
                }
                else
                {
                    Log.LogDebug($"RTCP sent RR {mediaType}, no packets sent or received.");
                }
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            if (recvRtcpReport.Bye != null)
            {
                Log.LogDebug($"RTCP recv BYE {mediaType}.");
            }
            else
            {
                var rr = recvRtcpReport.ReceiverReport?.ReceptionReports?.FirstOrDefault();
                if (rr != null)
                {
                    Log.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
                }
                else
                {
                    Log.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
                }
            }
        }

        /// <summary>
        /// Enable detailed SIP log messages.
        /// </summary>
        private static void EnableTraceLogs(SIPTransport sipTransport)
        {
            // Logging configuration. Can be omitted if internal SIPSorcery debug and warning messages are not required.
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;

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

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static void AddConsoleLogger()
        {
            var loggerFactory = new Microsoft.Extensions.Logging.LoggerFactory();
            var loggerConfig = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console(theme: AnsiConsoleTheme.Code)
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}
