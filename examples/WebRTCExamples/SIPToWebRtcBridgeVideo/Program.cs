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
// Note this example program forwards audio in both directions:
// softphone <-> program <-> browser
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 29 Jan 2020	Aaron Clauson	Created, Dublin, Ireland.
// 22 Oct 2020  Aaron Clauson   Enhanced to send bi-directional audio.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using WebSocketSharp.Server;

namespace SIPSorcery
{
    class Program
    {
        private static int SIP_LISTEN_PORT = 5060;
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger Log = NullLogger.Instance;

        private static RTCPeerConnection _peerConnection;
        private static RTPSession _rtpSession;

        async static Task Main()
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

            // Set up a default SIP transport.
            var sipTransport = new SIPTransport();
            sipTransport.AddSIPChannel(new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_LISTEN_PORT)));

            //EnableTraceLogs(sipTransport);

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

                var rtpSession = new RTPSession(false, false, false);
                rtpSession.AcceptRtpFromAny = true;
                MediaStreamTrack audioTrack = new MediaStreamTrack(new List<AudioFormat> { new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
                rtpSession.addTrack(audioTrack);
                MediaStreamTrack videoTrack = new MediaStreamTrack(new VideoFormat(VideoCodecsEnum.VP8, 100));
                rtpSession.addTrack(videoTrack);

                await userAgent.Answer(incomingCall, rtpSession);
                rtpSession.OnRtpPacketReceived += ForwardAudioToPeerConnection;
                rtpSession.OnVideoFrameReceived += ForwardVideoFrameToPeerConnection; // ForwardVideoFrameToSIP;

                Log.LogInformation($"Answered incoming call from {req.Header.From.FriendlyDescription()} at {req.RemoteSIPEndPoint}.");

                _rtpSession = rtpSession;
                RequestPeerConnectionKeyFrame(_peerConnection);
            };

            Console.WriteLine($"Waiting for browser web socket connection to {webSocketServer.Address}:{webSocketServer.Port}...");
            var contactURI = new SIPURI(SIPSchemesEnum.sip, sipTransport.GetSIPChannels().First().ListeningSIPEndPoint);
            Console.WriteLine($"Waiting for incoming SIP call to {contactURI}.");
            Console.WriteLine("NOTE: Once SIP and WebRTC parties are connected and if the video does not start press 'k' to request a key frame.");

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitCts.Cancel();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            await Task.Run(() => OnKeyPress(exitCts.Token));

            #region Cleanup.

            Log.LogInformation("Exiting...");

            _rtpSession?.Close("app exit");

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

        private static Task OnKeyPress(CancellationToken exit)
        {
            while (!exit.WaitHandle.WaitOne(0))
            {
                var keyProps = Console.ReadKey();

                if (keyProps.KeyChar == 'k')
                {
                    Console.WriteLine("Requesting key frame.");
                    RequestPeerConnectionKeyFrame(_peerConnection);
                    RequestSIPAgentKeyFrame(_rtpSession);
                }
                else if (keyProps.KeyChar == 'q')
                {
                    // Quit application.
                    Console.WriteLine("Quitting");
                    break;
                }
            }

            return Task.CompletedTask;
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            var pc = new RTCPeerConnection(null);

            MediaStreamTrack audioTrack = new MediaStreamTrack(new List<AudioFormat> { new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU) });
            MediaStreamTrack videoTrack = new MediaStreamTrack(new List<VideoFormat> { new VideoFormat(VideoCodecsEnum.VP8, 100) });
            pc.addTrack(audioTrack);
            pc.addTrack(videoTrack);

            pc.onconnectionstatechange += (state) =>
            {
                Log.LogDebug($"Peer connection state change to {state}.");
                RequestSIPAgentKeyFrame(_rtpSession);
            };
            pc.OnRtpPacketReceived += ForwardAudioToSIP;
            pc.OnVideoFrameReceived += ForwardVideoFrameToSIP; // ForwardVideoFrameToPeerConnection;
            _peerConnection = pc;

            return Task.FromResult(pc);
        }

        /// <summary>
        /// Forwards media from the WebRTC Peer Connection to the remote SIP user agent.
        /// </summary>
        /// <param name="remote">The remote endpoint the RTP packet was received from.</param>
        /// <param name="mediaType">The type of media.</param>
        /// <param name="rtpPacket">The RTP packet received on the SIP session.</param>
        private static void ForwardAudioToSIP(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (_rtpSession != null && !_rtpSession.IsClosed && mediaType == SDPMediaTypesEnum.audio)
            {
                _rtpSession.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
            }
        }

        /// <summary>
        /// Forwards media from the SIP session to the WebRTC session.
        /// </summary>
        /// <param name="remote">The remote endpoint the RTP packet was received from.</param>
        /// <param name="mediaType">The type of media.</param>
        /// <param name="rtpPacket">The RTP packet received on the SIP session.</param>
        private static void ForwardAudioToPeerConnection(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
        {
            if (_peerConnection != null && _peerConnection.connectionState == RTCPeerConnectionState.connected &&
                mediaType == SDPMediaTypesEnum.audio)
            {
                _peerConnection.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
            }
        }

        private static void ForwardVideoFrameToSIP(IPEndPoint remoteEP, uint timestamp, byte[] frame, VideoFormat format)
        {
            if (_rtpSession != null && !_rtpSession.IsClosed)
            {
                _rtpSession.SendVideo(3000, frame);
            }
        }

        private static void ForwardVideoFrameToPeerConnection(IPEndPoint remoteEP, uint timestamp, byte[] frame, VideoFormat format)
        {
            if (_peerConnection != null && _peerConnection.connectionState == RTCPeerConnectionState.connected)
            {
                _peerConnection.SendVideo(3000, frame);
            }
        }

        private static void RequestPeerConnectionKeyFrame(RTCPeerConnection pc)
        {
            if (pc != null && pc.connectionState == RTCPeerConnectionState.connected)
            {
                var localVideoSsrc = pc.VideoLocalTrack.Ssrc;
                var remoteVideoSsrc = pc.VideoRemoteTrack.Ssrc;

                Console.WriteLine($"Requesting key frame from peer connection for remote ssrc {remoteVideoSsrc}.");

                RTCPFeedback pli = new RTCPFeedback(localVideoSsrc, remoteVideoSsrc, PSFBFeedbackTypesEnum.PLI);
                pc.SendRtcpFeedback(SDPMediaTypesEnum.video, pli);
            }
        }

        /// <summary>
        /// Requests the SIP device to send a video key frame.
        /// </summary>
        /// <param name="rtpSession"></param>
        private static void RequestSIPAgentKeyFrame(RTPSession rtpSession)
        {
            if (rtpSession != null && !rtpSession.IsClosed)
            {
                var localVideoSsrc = rtpSession.VideoLocalTrack.Ssrc;
                var remoteVideoSsrc = rtpSession.VideoRemoteTrack.Ssrc;

                Console.WriteLine($"Requesting key frame from SIP connection for remote ssrc {remoteVideoSsrc}.");

                RTCPFeedback pli = new RTCPFeedback(localVideoSsrc, remoteVideoSsrc, PSFBFeedbackTypesEnum.PLI);
                rtpSession.SendRtcpFeedback(SDPMediaTypesEnum.video, pli);
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
