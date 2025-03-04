//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that uses OpenGL to produce
// a video stream for the remote peer. In this case OpenGL is used to process the
// received audio stream from the remote peer and generates a visual representation
// which is sent back in a video stream.
//
// The high level steps are:
// 1. Establish a WebRTC peer connection with a remote peer with a receive only
// audio stream and a send only video stream.
// 2. Receive audio packets from the remote peer and process them with the OpenGL
// program to generate a visual representation of teh received audio samples.
// 3. Send the visual representation back to the remote peer as a video stream.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 04 Jan 2025	Aaron Clauson	Created, Dublin, Ireland.
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
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Encoders;
using WebSocketSharp.Server;
using AudioScope;
using System.Numerics;
using SIPSorceryMedia.Abstractions;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;
        private const int AUDIO_PACKET_DURATION = 20; // 20ms of audio per RTP packet for PCMU & PCMA.

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static FormAudioScope _audioScopeForm;
        private static RTCPeerConnection _pc;

        static void Main()
        {
            Console.WriteLine("WebRTC OpenGL Demo - Audio Scope");

            logger = AddConsoleLogger();

            // Spin up a dedicated STA thread to run WinForms.
            Thread uiThread = new Thread(() =>
            {
                // WinForms initialization must be on an STA thread.
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                _audioScopeForm = new FormAudioScope(false);

                Application.Run(_audioScopeForm);
            });

            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) =>
            {
                // For the purposes of the demo only one peer conenction at a time is managed.
                peer.CreatePeerConnection = CreatePeerConnection;
                _pc = peer.RTCPeerConnection;
            });
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
            Console.WriteLine("Press ctrl-c to exit.");

            // Ctrl-c will gracefully exit the call at any point.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                Console.WriteLine("Exiting...");

                e.Cancel = true;

                _pc?.Close("User exit");

                _audioScopeForm.Invoke(() => _audioScopeForm.Close());

                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                //iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
                //X_BindAddress = IPAddress.Any
            };
            var pc = new RTCPeerConnection(config);

            var videoEncoderEndPoint = new VideoEncoderEndPoint();
            var audioEncoder = new AudioEncoder();

            // For the sake of the demo stick to a basic audio format with a predictable sampling rate.
            var supportedAudioFormats = new List<AudioFormat>
            {
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
                new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),
            };

            MediaStreamTrack videoTrack = new MediaStreamTrack(videoEncoderEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);
            MediaStreamTrack audioTrack = new MediaStreamTrack(supportedAudioFormats, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(audioTrack);

            videoEncoderEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;

            pc.OnVideoFormatsNegotiated += (formats) => videoEncoderEndPoint.SetVideoSourceFormat(formats.First());
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
            pc.onconnectionstatechange += (state) => logger.LogDebug($"Peer connection state change to {state}.");
            pc.onsignalingstatechange += () =>
            {
                logger.LogDebug($"Signalling state change to {pc.signalingState}.");

                if (pc.signalingState == RTCSignalingState.have_local_offer)
                {
                    logger.LogDebug($"Local SDP offer:\n{pc.localDescription.sdp}");
                }
                else if (pc.signalingState == RTCSignalingState.stable)
                {
                    logger.LogDebug($"Remote SDP offer:\n{pc.remoteDescription.sdp}");
                }
            };           

            pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                //logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}, payload {rtpPkt.Header.PayloadType}, SeqNum {rtpPkt.Header.SequenceNumber}.");

                if (media == SDPMediaTypesEnum.audio)
                {
                    var decodedSample = audioEncoder.DecodeAudio(rtpPkt.Payload, pc.AudioStream.NegotiatedFormat.ToAudioFormat());

                    var samples = decodedSample
                        .Select(s => new Complex(s / 32768f, 0f))
                        .ToArray();

                    var frame = _audioScopeForm.Invoke(() => _audioScopeForm.ProcessAudioSample(samples));

                    videoEncoderEndPoint.ExternalVideoSourceRawSample(AUDIO_PACKET_DURATION,
                        FormAudioScope.AUDIO_SCOPE_WIDTH,
                        FormAudioScope.AUDIO_SCOPE_HEIGHT,
                        frame,
                        VideoPixelFormatsEnum.Rgb);
                }
            };

            return Task.FromResult(pc);
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
