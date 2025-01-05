//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that receives an audio
// stream from the remote peer and generates a visual representation of the
// audio which is sent back in a vido stream.
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
using System.Collections.Concurrent;
using System.Numerics;
using SIPSorceryMedia.Abstractions;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;
        private const int AUDIO_SAMPLING_INTERVAL_MILLISECONDS = 100;

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        private static FormAudioScope _audioScopeForm;
        //private static System.Windows.Forms.Timer _renderTimer;
        //private static ConcurrentQueue<byte[]> _frameQueue = new ConcurrentQueue<byte[]>();
        //private static AutoResetEvent _frameReadyEvent = new AutoResetEvent(false);
        //private static bool _isConnected = false;
  

        static void Main()
        {
            Console.WriteLine("WebRTC Audio Scope");

            logger = AddConsoleLogger();

            // Spin up a dedicated STA thread to run WinForms
            Thread uiThread = new Thread(() =>
            {
                // WinForms initialization must be on an STA thread
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                _audioScopeForm = new FormAudioScope();
                //_audioScopeForm.OnFrameReady += (sender, frame) =>
                //{
                //    //Console.WriteLine($"OnFrameReady {Convert.ToBase64String(SHA256.HashData(frame))}.");

                //    if (_isConnected)
                //    {
                //        _frameQueue.Enqueue(frame);
                //        _frameReadyEvent.Set();
                //    }
                //};

                //_renderTimer = new System.Windows.Forms.Timer();
                //_renderTimer.Interval = AUDIO_SAMPLING_INTERVAL_MILLISECONDS;
                //_renderTimer.Tick += (s, e) => _audioScopeForm.RequestRender();
                //_renderTimer.Start();

                Application.Run(_audioScopeForm);
            });

            uiThread.SetApartmentState(ApartmentState.STA);
            uiThread.IsBackground = true;
            uiThread.Start();

            // Start web socket.
            Console.WriteLine("Starting web socket server...");
            var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
            webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) => peer.CreatePeerConnection = CreatePeerConnection);
            webSocketServer.Start();

            Console.WriteLine($"Waiting for web socket connections on {webSocketServer.Address}:{webSocketServer.Port}...");
            Console.WriteLine("Press ctrl-c to exit.");           

            // Ctrl-c will gracefully exit the call at any point.
            ManualResetEvent exitMre = new ManualResetEvent(false);
            Console.CancelKeyPress += delegate (object sender, ConsoleCancelEventArgs e)
            {
                Console.WriteLine("Exiting...");

                e.Cancel = true;

                //_renderTimer?.Dispose();
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

            MediaStreamTrack videoTrack = new MediaStreamTrack(videoEncoderEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
            pc.addTrack(videoTrack);
            MediaStreamTrack audioTrack = new MediaStreamTrack(audioEncoder.SupportedFormats, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(audioTrack);

            //testPatternSource.OnVideoSourceRawSample += videoEncoderEndPoint.ExternalVideoSourceRawSample;
            videoEncoderEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;
            //audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

            pc.OnVideoFormatsNegotiated += (formats) => videoEncoderEndPoint.SetVideoSourceFormat(formats.First());
            //pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());
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
            
            pc.onconnectionstatechange += (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    //_isConnected = true;
                    //_ = Task.Run(() => StartAudioScopeSource(videoEncoderEndPoint));
                    //await audioSource.StartAudio();
                    //await testPatternSource.StartVideo();
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    //await testPatternSource.CloseVideo();
                    //await audioSource.CloseAudio();
                }
            };

            // Diagnostics.
            //pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            //pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            //pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            pc.OnRtpPacketReceived += (IPEndPoint rep, SDPMediaTypesEnum media, RTPPacket rtpPkt) =>
            {
                //logger.LogDebug($"RTP {media} pkt received, SSRC {rtpPkt.Header.SyncSource}, payload {rtpPkt.Header.PayloadType}, SeqNum {rtpPkt.Header.SequenceNumber}.");

                if (media == SDPMediaTypesEnum.audio)
                {
                    var audioFormat = audioTrack.GetFormatForPayloadID(rtpPkt.Header.PayloadType)?.ToAudioFormat();

                    if (audioFormat != null)
                    {
                        var decodedSample = audioEncoder.DecodeAudio(rtpPkt.Payload, audioFormat.Value);
                        Console.WriteLine($"Decoded {audioFormat.Value.FormatName} audio sample of length {decodedSample.Length} shorts.");

                        var samples = decodedSample
                            .Select(s => new Complex(s / 32768f, 0f))
                            .ToArray();

                        var frame = _audioScopeForm.Invoke(() => _audioScopeForm.ProcessAudioSample(samples));

                        videoEncoderEndPoint.ExternalVideoSourceRawSample(AUDIO_SAMPLING_INTERVAL_MILLISECONDS,
                            FormAudioScope.AUDIO_SCOPE_WIDTH,
                            FormAudioScope.AUDIO_SCOPE_HEIGHT,
                            frame,
                            SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.Rgb);
                    }
                }
            };

            return Task.FromResult(pc);
        }

        //private static void StartAudioScopeSource(VideoEncoderEndPoint videoEncoder)
        //{
        //    while (true)
        //    {
        //        while (_frameQueue.TryDequeue(out var frame))
        //        {
        //            //Console.WriteLine($"Got frame of length {frame.Length} bytes");

        //            videoEncoder.ExternalVideoSourceRawSample(AUDIO_SAMPLING_INTERVAL_MILLISECONDS,
        //                FormAudioScope.AUDIO_SCOPE_WIDTH,
        //                FormAudioScope.AUDIO_SCOPE_HEIGHT,
        //                frame,
        //                SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.Rgb);
        //        }

        //        _frameReadyEvent.WaitOne();
        //    }
        //}

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
