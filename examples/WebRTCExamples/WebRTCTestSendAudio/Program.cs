//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that serves a sine wave 
// audio stream to a WebRTC enabled browser.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 28 Jul 2020	Aaron Clauson	Created, Dublin, Ireland.
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
using Serilog;
using SIPSorcery.Net;
using SIPSorcery.Media;
using WebSocketSharp;
using WebSocketSharp.Net.WebSockets;
using WebSocketSharp.Server;

namespace WebRTCServer
{
    public class WebRTCAudioSession : WebSocketBehavior
    {
        private const int RTP_TIMESTAMP_RATE = 8000;         // G711 uses an 8KHz for RTP timestamps clock.
        private const int AUDIO_SAMPLE_PERIOD_MILLISECONDS = 20;

        public RTCPeerConnection _peerConnection;
        private SignalGenerator _signalGenerator;
        private Timer _audioStreamTimer;

        public event Func<WebSocketContext, Task<RTCPeerConnection>> WebSocketOpened;
        public event Action<RTCPeerConnection, string> OnMessageReceived;

        public WebRTCAudioSession()
        { }

        protected override void OnMessage(MessageEventArgs e)
        {
            OnMessageReceived(_peerConnection, e.Data);
        }

        protected override async void OnOpen()
        {
            base.OnOpen();
            _peerConnection = await WebSocketOpened(this.Context);

            _peerConnection.onconnectionstatechange += (state) =>
            {

                if (state == RTCPeerConnectionState.connected)
                {
                    _signalGenerator = new SignalGenerator(8000, 1);
                    _signalGenerator.Type = SignalGeneratorType.Sin;
                    _audioStreamTimer = new Timer(SendSignalGeneratorSample, null, 0, AUDIO_SAMPLE_PERIOD_MILLISECONDS);
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    _audioStreamTimer?.Dispose();
                }
            };
        }

        protected override void OnClose(CloseEventArgs e)
        {
            base.OnClose(e);
            _peerConnection.Close("remote party close");
            _audioStreamTimer?.Dispose();
        }

        /// <summary>
        /// Sends a sample from a signal generator generated waveform.
        /// </summary>
        private void SendSignalGeneratorSample(object state)
        {
            lock (_audioStreamTimer)
            {
                int inputBufferSize = RTP_TIMESTAMP_RATE / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;
                int outputBufferSize = RTP_TIMESTAMP_RATE / 1000 * AUDIO_SAMPLE_PERIOD_MILLISECONDS;

                // Get the signal generator to generate the samples and then convert from
                // signed linear to PCM.
                float[] linear = new float[inputBufferSize];
                _signalGenerator.Read(linear, 0, inputBufferSize);
                short[] pcm = linear.Select(x => (short)(x * 32767f)).ToArray();

                byte[] encodedSample = new byte[outputBufferSize];

                for (int index = 0; index < inputBufferSize; index++)
                {
                    encodedSample[index] = MuLawEncoder.LinearToMuLawSample(pcm[index]);
                }

                _peerConnection.SendAudioFrame((uint)outputBufferSize, _peerConnection.GetSendingFormat(SDPMediaTypesEnum.audio).FormatCodec.GetHashCode(), encodedSample);
            }
        }
    }

    class Program
    {
        private const int WEBSOCKET_PORT = 8081;

        private static Microsoft.Extensions.Logging.ILogger logger = SIPSorcery.Sys.Log.Logger;

        private static WebSocketServer _webSocketServer;

        static void Main()
        {
            Console.WriteLine("WebRTC Audio Server Example Program");
            Console.WriteLine("Press ctrl-c to exit.");

            // Plumbing code to facilitate a graceful exit.
            CancellationTokenSource exitCts = new CancellationTokenSource(); // Cancellation token to stop the SIP transport and RTP stream.
            ManualResetEvent exitMre = new ManualResetEvent(false);

            AddConsoleLogger();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            _webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            _webSocketServer.AddWebSocketService<WebRTCAudioSession>("/", (session) =>
            {
                session.WebSocketOpened += SendSDPOffer;
                session.OnMessageReceived += WebSocketMessageReceived;
            });
            _webSocketServer.Start();

            Console.WriteLine($"Waiting for browser web socket connection to {_webSocketServer.Address}:{_webSocketServer.Port}...");

            // Ctrl-c will gracefully exit the call at any point.
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static async Task<RTCPeerConnection> SendSDPOffer(WebSocketContext context)
        {
            logger.LogDebug($"Web socket client connection from {context.UserEndPoint}.");

            var pc = new RTCPeerConnection(null);

            MediaStreamTrack videoTrack = new MediaStreamTrack(SDPMediaTypesEnum.audio, false, new List<SDPMediaFormat> { new SDPMediaFormat(SDPMediaFormatsEnum.PCMU) }, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);

            pc.OnReceiveReport += RtpSession_OnReceiveReport;
            pc.OnSendReport += RtpSession_OnSendReport;
            pc.OnTimeout += (mediaType) => pc.Close("remote timeout");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.disconnected || state == RTCPeerConnectionState.failed)
                {
                    pc.Close("remote disconnection");
                }
                else if(state == RTCPeerConnectionState.closed)
                {
                    pc.OnReceiveReport -= RtpSession_OnReceiveReport;
                    pc.OnSendReport -= RtpSession_OnSendReport;
                }
            };

            var offerSdp = pc.createOffer(null);
            await pc.setLocalDescription(offerSdp);

            logger.LogDebug($"Sending SDP offer to client {context.UserEndPoint}.");
            logger.LogDebug(offerSdp.sdp);

            context.WebSocket.Send(offerSdp.sdp);

            return pc;
        }

        private static void WebSocketMessageReceived(RTCPeerConnection pc, string message)
        {
            try
            {
                if (pc.remoteDescription == null)
                {
                    logger.LogDebug("Answer SDP: " + message);
                    pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = message, type = RTCSdpType.answer });
                }
                else
                {
                    logger.LogDebug("ICE Candidate: " + message);
                    pc.addIceCandidate(new RTCIceCandidateInit { candidate = message });
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception WebSocketMessageReceived. " + excp.Message);
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP sender/receiver reports.
        /// </summary>
        private static void RtpSession_OnSendReport(SDPMediaTypesEnum mediaType, RTCPCompoundPacket sentRtcpReport)
        {
            if (sentRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP sent BYE {mediaType}.");
            }
            else if (sentRtcpReport.SenderReport != null)
            {
                var sr = sentRtcpReport.SenderReport;
                logger.LogDebug($"RTCP sent SR {mediaType}, ssrc {sr.SSRC}, pkts {sr.PacketCount}, bytes {sr.OctetCount}.");
            }
            else
            {
                if (sentRtcpReport.ReceiverReport.ReceptionReports?.Count > 0)
                {
                    var rrSample = sentRtcpReport.ReceiverReport.ReceptionReports.First();
                    logger.LogDebug($"RTCP sent RR {mediaType}, ssrc {rrSample.SSRC}, seqnum {rrSample.ExtendedHighestSequenceNumber}.");
                }
                else
                {
                    logger.LogDebug($"RTCP sent RR {mediaType}, no packets sent or received.");
                }
            }
        }

        /// <summary>
        /// Diagnostic handler to print out our RTCP reports from the remote WebRTC peer.
        /// </summary>
        private static void RtpSession_OnReceiveReport(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTCPCompoundPacket recvRtcpReport)
        {
            if (recvRtcpReport.Bye != null)
            {
                logger.LogDebug($"RTCP recv BYE {mediaType}.");
            }
            else
            {
                var rr = recvRtcpReport.ReceiverReport.ReceptionReports.FirstOrDefault();
                if (rr != null)
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: SSRC {rr.SSRC}, pkts lost {rr.PacketsLost}, delay since SR {rr.DelaySinceLastSenderReport}.");
                }
                else
                {
                    logger.LogDebug($"RTCP {mediaType} Receiver Report: empty.");
                }
            }
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
                .WriteTo.Console()
                .CreateLogger();
            loggerFactory.AddSerilog(loggerConfig);
            SIPSorcery.Sys.Log.LoggerFactory = loggerFactory;
        }
    }
}
