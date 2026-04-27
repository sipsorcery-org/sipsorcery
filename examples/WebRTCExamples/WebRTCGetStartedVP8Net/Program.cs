//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that attempts to send a
// test pattern to a browser peer and that uses a prototype .NET version of the
// VP8 encoder. A web socket is used for signalling.
//
// The point of this demo is that it does not require any native libraries or
// audio/video devices. This makes it a good palce to start for checking
// whether a particular platform can be used to establish WebRTC connections
// and get a media strem flowing.
//
// TODO: Not available until the VP8.NET project ports the encoder.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 12 Mar 2025	Aaron Clauson	Created, Dublin, Ireland.
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
using SIPSorcery.Media;
using SIPSorcery.Net;
using Vpx.Net;
using WebSocketSharp.Server;

namespace demo
{
    class Program
    {
        private const int WEBSOCKET_PORT = 8081;
        private const string STUN_URL = "stun:stun.cloudflare.com";

        private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

        static void Main()
        {
            Console.WriteLine("WebRTC Get Started");

            logger = AddConsoleLogger();

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
                e.Cancel = true;
                exitMre.Set();
            };

            // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
            exitMre.WaitOne();
        }

        private static Task<RTCPeerConnection> CreatePeerConnection()
        {
            RTCConfiguration config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
                //X_BindAddress = IPAddress.Any
            };
            var pc = new RTCPeerConnection(config);

            // Pass the VP8 codec directly to the test pattern source. With
            // this wiring the source calls VP8Codec.EncodeVideo on its
            // native I420 buffer once per frame and fires
            // OnVideoSourceEncodedSample with the encoded VP8 byte
            // stream; no per-frame format conversion happens.
            //
            // The previous wiring (see git history) hooked the
            // OnVideoSourceRawSample event to a Vp8NetVideoEncoderEndPoint
            // — that path forces an I420 -> BGR conversion in the source
            // and then a BGR -> I420 conversion back in the endpoint
            // before the encoder runs, allocating roughly 1.4 MB per
            // frame at 640x480 (= 42 MB/sec at 30 fps) of throw-away
            // buffers. Profiling traced visible audio/video jitter
            // spikes back to ~6 Gen 2 GCs per second under that wiring.
            // Switching to the direct-codec path eliminates that GC
            // pressure entirely (0 Gen 2 collections per 10 s observed).
            // Workaround for the burst-rate problem the foundation encoder
            // exposes on busy content (see PR notes for the full story):
            //
            //   * Q=96 produces ~16 KB keyframes on the test pattern instead
            //     of ~50 KB at the default Q=32. ~13 RTP packets per frame
            //     instead of ~42, so each frame's burst is ~3x smaller.
            //
            //   * SetFrameRate(15) halves the burst frequency from 30/s
            //     to 15/s, giving Chrome's UDP receive pipeline twice as
            //     long to drain between bursts.
            //
            // Combined: ~5x reduction in burst pressure on the receiver,
            // at the cost of visible blocking artefacts on the test
            // pattern (which is intentionally high-detail). For typical
            // webcam content the same defaults will produce smaller
            // frames and the artefacts will be much less noticeable.
            //
            // The proper fix (RTP pacing in SIPSorcery's send path, and/or
            // P-frames in VP8.Net) is a follow-up; this is the smallest
            // configuration change that makes the audio stream survive
            // beyond the few-tens-of-seconds window it was breaking at.
            var vp8Codec = new VP8Codec { BaseQIndex = 96 };
            var testPatternSource = new VideoTestPatternSource(vp8Codec);
            testPatternSource.SetFrameRate(15);
            var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music });

            MediaStreamTrack videoTrack = new MediaStreamTrack(testPatternSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(videoTrack);
            MediaStreamTrack audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);

            // ---- Diagnostic packet counters ----
            //
            // Two counters logged once per second on each stream:
            //
            //   * "src":   packets handed off by the source to pc.Send*.
            //              Increments iff the source is still producing
            //              encoded samples — answers "did our test
            //              source / encoder stop?".
            //
            //   * "rtcp":  cumulative PacketCount field of our outgoing
            //              RTCP Sender Reports, fired by SIPSorcery's
            //              RTPSession.OnSendReport. SIPSorcery emits an
            //              SR roughly every 5 s and sets PacketCount to
            //              the total number of RTP packets that have
            //              actually left this peer. If this counter
            //              keeps growing while Chrome's webrtc-internals
            //              packetsReceived stalls, packets are leaving
            //              the .NET app but Chrome is dropping them
            //              (suspected SRTP / DTLS issue). If "rtcp"
            //              stalls when "src" stalls, the issue is at or
            //              above the SIPSorcery send path.
            int audioSrcCount = 0, videoSrcCount = 0;

            testPatternSource.OnVideoSourceEncodedSample += (rtpTs, frame) =>
            {
                pc.SendVideo(rtpTs, frame);
                int n = System.Threading.Interlocked.Increment(ref videoSrcCount);
                if (n % 15 == 0)
                {
                    // Log the current RTP sequence number alongside the
                    // packet count so the wrap-around boundary
                    // (65535 -> 0) is visible in the trace.  Hypothesis
                    // being tested: audio/video stream loss correlates
                    // with the 16-bit RTP sequence number wrapping for
                    // the affected stream, suggesting an SRTP rollover-
                    // counter bug somewhere in SIPSorcery's send path
                    // (or BouncyCastle's wrapper of it).  Note the
                    // SeqNum reported here is the *next* number the
                    // track will assign (one ahead of what was just
                    // sent), so the wrap shows as the value going from
                    // ~65535 to a small number around the failure time.
                    var seq = pc.VideoStream?.LocalTrack?.SeqNum;
                    logger.LogInformation("video src: {Count} frames seq~{Seq} (~{Sec:F0}s at 15 fps)", n, seq, n / 15.0);
                }
            };

            audioSource.OnAudioSourceEncodedSample += (rtpTs, sample) =>
            {
                pc.SendAudio(rtpTs, sample);
                int n = System.Threading.Interlocked.Increment(ref audioSrcCount);
                if (n % 50 == 0)
                {
                    var seq = pc.AudioStream?.LocalTrack?.SeqNum;
                    logger.LogInformation("audio src: {Count} packets seq~{Seq} (~{Sec:F0}s at 50 pps)", n, seq, n / 50.0);
                }
            };

            // Hook outgoing RTCP Sender Reports. PacketCount is the
            // cumulative count of RTP packets the local SIPSorcery
            // send path has emitted on each stream. Compare against
            // Chrome's inbound-rtp packetsReceived to see whether
            // packets are getting lost between the .NET app and
            // Chrome. SIPSorcery emits SRs every ~5 s by default.
            pc.OnSendReport += (mediaType, compound) =>
            {
                var sr = compound?.SenderReport;
                if (sr != null)
                {
                    logger.LogInformation("rtcp SR {Media}: SSRC={SSRC,10} PacketCount={Pkts} OctetCount={Octets}",
                        mediaType, sr.SSRC, sr.PacketCount, sr.OctetCount);
                }
            };

            pc.OnVideoFormatsNegotiated += (formats) => testPatternSource.SetVideoSourceFormat(formats.First());
            pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());
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
            
            pc.onconnectionstatechange += async (state) =>
            {
                logger.LogDebug($"Peer connection state change to {state}.");

                if (state == RTCPeerConnectionState.connected)
                {
                    await audioSource.StartAudio();
                    await testPatternSource.StartVideo();
                }
                else if (state == RTCPeerConnectionState.failed)
                {
                    pc.Close("ice disconnection");
                }
                else if (state == RTCPeerConnectionState.closed)
                {
                    await testPatternSource.CloseVideo();
                    await audioSource.CloseAudio();
                }
            };

            // Diagnostics.
            pc.OnReceiveReport += (re, media, rr) => logger.LogDebug($"RTCP Receive for {media} from {re}\n{rr.GetDebugSummary()}");
            pc.OnSendReport += (media, sr) => logger.LogDebug($"RTCP Send for {media}\n{sr.GetDebugSummary()}");
            pc.GetRtpChannel().OnStunMessageReceived += (msg, ep, isRelay) => logger.LogDebug($"STUN {msg.Header.MessageType} received from {ep}.");
            pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");

            // To test closing.
            //_ = Task.Run(async () => 
            //{ 
            //    await Task.Delay(5000);

            //    audioSource.OnAudioSourceEncodedSample -= pc.SendAudio;
            //    videoEncoderEndPoint.OnVideoSourceEncodedSample -= pc.SendVideo;

            //    logger.LogDebug("Closing peer connection.");
            //    pc.Close("normal");
            //});

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
