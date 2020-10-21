//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example program of how to use the SIPSorcery core library to 
// act as an audio bridge between a SIP client and a WebRTC peer.
//
// Usage:
// 1. Start this program. It will listen for a SIP call on 0.0.0.0:5060
//    and for a web socket connection on 0.0.0.0:8081,
// 2. Place a call from a softphone to this program, e.g. sip:1@127.0.0.1.
//    It will be automatically answered.
// 3. Open the included webrtcsip.html file in Chrome/Firefox on the same
//    machine as the one running the program. Click the "start" button and
//    shortly thereafter audio from the softphone should play in the browser.
//
// Note this example program forwards audio in one direction only:
// softphone -> program -> browser
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Jan 2019	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions.V1;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace SIPSorcery
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 5060;
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static RTCPeerConnection _peerConnection;

        static void Main()
        {
            Console.WriteLine("SIPSorcery SIP to WebRTC example.");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.

            Log = AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {webSocketServer.Address}:{webSocketServer.Port}...");

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            //EnableTraceLogs(sipTransport);

            RTPSession rtpSession = null;

            // Create a SIP user agent to receive a call from a remote SIP client.
            // Wire up event handlers for the different stages of the call.
            var userAgent = new SIPUserAgent(sipTransport, null, true);

            // We're only answering SIP calls, not placing them.
            userAgent.OnCallHungup += (dialog) =>
            {
                Log.LogInformation($"Call hungup by remote party.");
                exitCts.Cancel();
            };
            userAgent.ServerCallCancelled += (uas) => Log.LogInformation("Incoming call cancelled by caller.");
            userAgent.OnIncomingCall += async (ua, req) =>
            {
                Log.LogInformation($"Incoming call request from {req.RemoteSIPEndPoint}: {req.StatusLine}.");
                var incomingCall = userAgent.AcceptCall(req);

                rtpSession = new RTPSession(false, false, false);
                rtpSession.AcceptRtpFromAny = true;
                MediaStreamTrack audioTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
                rtpSession.addTrack(audioTrack);

                await userAgent.Answer(incomingCall, rtpSession);
                rtpSession.OnRtpPacketReceived += (ep, mediaType, rtpPacket) => ForwardMedia(mediaType, rtpPacket);

                Log.LogInformation($"Answered incoming call from {req.Header.From.FriendlyDescription()} at {req.RemoteSIPEndPoint}.");
            };

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

            rtpSession?.Close("app exit");

            if (userAgent != null)
            {
                if (userAgent.IsCallActive)
                {
                    Log.LogInformation($"Hanging up call to {userAgent?.CallDescriptor?.To}.");
                    userAgent.Hangup();
                }

                // Give the BYE or CANCEL request time to be transmitted.
                Log.LogInformation("Waiting 1s for call to clean up...");
                Task.Delay(1000).Wait();
            }

            if (sipTransport != null)
            {
                Log.LogInformation("Shutting down SIP transport...");
                sipTransport.Shutdown();
            }

            #endregion
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            var pc = new RTCPeerConnection(null);

            MediaStreamTrack track = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, 
                new List<SDPAudioVideoMediaFormat> { new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
            pc.addTrack(track);
            pc.onconnectionstatechange += (state) => Log.LogDebug($"Peer connection state change to {state}.");

            _peerConnection = pc;

            return Task.FromResult(pc);
        }

        /// <summary>
        /// Forwards media from the SIP session to the WebRTC session.
        /// </summary>
        /// <param name="mediaType">The type of media.</param>
        /// <param name="rtpPacket">The RTP packet received on the SIP session.</param>
        private static void ForwardMedia(SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (_peerConnection != null && mediaType == SDPMediaTypesEnum.audio)
            {
                _peerConnection.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
            }
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
            var serilogLogger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .MinimumLevel.Is(Serilog.Events.LogEventLevel.Debug)
                .WriteTo.Console()
                .CreateLogger();
            var factory = new SerilogLoggerFactory(serilogLogger);
            SIPSorcery.LogFactory.Set(factory);
            return factory.CreateLogger<Program>();
        }
    }
}
