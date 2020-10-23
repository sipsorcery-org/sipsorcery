//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A sample program to interop with the sip.js
// javascript SIP library, see https://sipjs.com/.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2020	Aaron Clauson	Created, Dublin, Ireland.
// 23 Oct 2020  Aaron Clauson   Simplified with latest main library updates.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;

namespace demo
{
    class Program
    {
        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("SIPSorcery sip.js Demo");

            Log = AddConsoleLogger();

            var sipTransport = new SIPTransport();
            EnableTraceLogs(sipTransport);

            var sipChannel = new SIPWebSocketChannel(IPAddress.Loopback, 8081);
            sipTransport.AddSIPChannel(sipChannel);

            var userAgent = new SIPUserAgent(sipTransport, null, true);
            userAgent.OnIncomingCall += async (ua, req) =>
            {
                Log.LogDebug($"Auto-answering incoming call from {req.Header.From}.");
                var uas = userAgent.AcceptCall(req);

                var peerConnection = new RTCPeerConnection(null);

                peerConnection.onconnectionstatechange += (state) =>
                {
                    Log.LogDebug($"Peer connection state change to {state}.");

                    if (state == RTCPeerConnectionState.failed)
                    {
                        peerConnection.Close("ice disconnection");
                    }
                    else if (state == RTCPeerConnectionState.connected)
                    {
                        peerConnection.OnRtpPacketReceived += OnRtpPacketReceived;
                    }
                    else if (state == RTCPeerConnectionState.closed)
                    {
                        peerConnection.OnRtpPacketReceived -= OnRtpPacketReceived;
                    }
                };

                MediaStreamTrack audioTrack = new MediaStreamTrack(new List<AudioFormat> { new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendRecv);
                peerConnection.addTrack(audioTrack);
                //MediaStreamTrack videoTrack = new MediaStreamTrack("1", SDPMediaTypesEnum.video, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.VP8) }, MediaStreamStatusEnum.Inactive);
                //peerConnection.addTrack(videoTrack);

                var answerResult = await userAgent.Answer(uas, peerConnection);
            };

            Console.Write("press any key to exit...");
            Console.Read();

            sipTransport.Shutdown();
        }

        private static void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum media, RTPPacket pkt)
        {
            Log.LogDebug($"RTP {media} packet received, ssrc {pkt.Header.SyncSource}, seqnum {pkt.Header.SequenceNumber}, timestamp {pkt.Header.Timestamp}.");
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

        /// <summary>
        ///  Adds a console logger. Can be omitted if internal SIPSorcery debug and warning messages are not required.
        /// </summary>
        private static Microsoft.Extensions.Logging.ILogger AddConsoleLogger()
        {
            var seriLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(seriLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
