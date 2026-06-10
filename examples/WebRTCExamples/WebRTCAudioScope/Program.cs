//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: An example WebRTC server application that uses a C# based "AudioScope"
// (previously used an OpenGL version) to produce a video stream for the remote peer.
// The AudioScope is used to process the audio stream and generate a visual representation
// which is sent back in a video stream.
//
// The high level steps are:
// 1. Establish a WebRTC peer connection with a remote peer with a receive only
// audio stream and a send only video stream.
// 2. Gnerate audio packets from the audio source and process them with the AudioScope
// program to generate a visual representation of the audio samples.
// 3. Send the visual representation back to the remote peer as a video stream.
//
// The AudioScope was originally based on https://github.com/conundrumer/visual-music-workshop.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 11 Jan 2026	Aaron Clauson	Created, Dublin, Ireland.
// 08 Jun 2026  Aaron Clauson   Converted from OpenGL implementation to C# version.
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
using WebSocketSharp.Server;
using AudioScope;
using System.Numerics;
using SIPSorceryMedia.Abstractions;
using Vpx.Net;

namespace demo;

class Program
{
    private const int WEBSOCKET_PORT = 8081;

    // The scope video is rendered/encoded on its own timer rather than inside the audio packet
    // callback. Encoding a 640x480 VP8 frame takes several milliseconds; doing it synchronously on
    // the audio source's pacing thread delayed audio packets past their 20ms budget and made the
    // received audio choppy. 33ms = ~30fps (down from the 50fps implied by the audio packet rate).
    private const int SCOPE_FRAME_INTERVAL_MS = 33;

    private const int SYNTH_SAMPLE_RATE = 8000;        // Scope carrier sample rate (matches the codec).
    // The scope's analytic transform uses a 1024-point FFT at SYNTH_SAMPLE_RATE, so its bin spacing is
    // 8000/1024 = 7.8125 Hz. A tone exactly on a bin is perfectly periodic in the FFT window and
    // produces no spectral leakage, which is what keeps the trace a clean circle (off-bin tones leak
    // and draw chords). 62.5 Hz = exactly bin 8, and low enough to give ~128 points per revolution.
    private const double SYNTH_FUNDAMENTAL_HZ = 62.5;
    private static double _synthPhase = 0.0;           // Scope carrier phase accumulator (continuous across packets).
    private static double _scopeEnv = 0.0;             // Smoothed audio-loudness envelope driving the circle radius.

    private static Microsoft.Extensions.Logging.ILogger logger = NullLogger.Instance;

    private static AudioScopeRenderer _renderer;
    private static RTCPeerConnection _pc;

    private enum AudioScopeSourceEnum
    {
        Music,
        Synth,
        Microphone
    }

    static void Main()
    {
        Console.WriteLine("WebRTC OpenGL Source Demo - Audio Scope");

        logger = AddConsoleLogger();

        // The scope renders straight to an RGB buffer on the CPU - no window or GL context required.
        _renderer = new AudioScopeRenderer();

        // Start web socket.
        Console.WriteLine("Starting web socket server...");
        var webSocketServer = new WebSocketServer(IPAddress.Any, WEBSOCKET_PORT);
        webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/", (peer) =>
        {
            // For the purposes of the demo only one peer conenction at a time is managed.
            peer.CreatePeerConnection = () => CreatePeerConnection(AudioScopeSourceEnum.Music);
            _pc = peer.RTCPeerConnection;
        });
        webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/synth", (peer) =>
        {
            // For the purposes of the demo only one peer conenction at a time is managed.
            peer.CreatePeerConnection = () => CreatePeerConnection(AudioScopeSourceEnum.Synth);
            _pc = peer.RTCPeerConnection;
        });
        webSocketServer.AddWebSocketService<WebRTCWebSocketPeer>("/mic", (peer) =>
        {
            // For the purposes of the demo only one peer connection at a time is managed.
            // The "reverse" scope: the browser sends its microphone audio to us and we render a
            // scope of that received audio back as the video stream.
            peer.CreatePeerConnection = () => CreatePeerConnection(AudioScopeSourceEnum.Microphone);
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

            _renderer?.Dispose();

            exitMre.Set();
        };

        // Wait for a signal saying the call failed, was cancelled with ctrl-c or completed.
        exitMre.WaitOne();
    }

    private static Task<RTCPeerConnection> CreatePeerConnection(AudioScopeSourceEnum audioScopeSource)
    {
        RTCConfiguration config = new RTCConfiguration
        {
            //iceServers = new List<RTCIceServer> { new RTCIceServer { urls = STUN_URL } },
            //X_BindAddress = IPAddress.Any
        };
        var pc = new RTCPeerConnection(config);

        // Managed VP8 encoder. The audio scope is high-motion content (every frame
        // changes) at 640x480, so every encoded frame is large and spans ~10 RTP
        // packets. There is no RTCP PLI -> keyframe-request recovery in the pipeline
        // yet, so a single lost packet would otherwise corrupt every inter frame until
        // the next keyframe. A short GOP bounds that to a few frames; for this content
        // keyframes are actually smaller than inter frames, so a short interval costs
        // little bitrate. Lower to 1 (every frame a keyframe) for maximum loss
        // resilience, or raise BaseQIndex to shrink frames further.
        var videoEndPoint = new Vp8NetVideoEncoderEndPoint
        {
            KeyframeIntervalFrames = 1
        };
        var audioEncoder = new AudioEncoder();

        bool isMicrophone = audioScopeSource == AudioScopeSourceEnum.Microphone;

        // For the sake of the demo stick to a basic audio format with a predictable sampling rate.
        var supportedAudioFormats = new List<AudioFormat>
        {
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),
        };

        // The scope's video is always produced by this app and sent to the peer.
        MediaStreamTrack videoTrack = new MediaStreamTrack(videoEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);
        videoEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += (formats) => videoEndPoint.SetVideoSourceFormat(formats.First());

        // The most recent block of decoded PCM samples from the audio path. The audio callbacks
        // only write this (cheap); the scope timer below reads it and does the expensive
        // render + VP8 encode + send on its own thread so the audio packet cadence is never
        // delayed by video work.
        short[] latestPcm = null;
        object pcmLock = new object();
        int renderInProgress = 0;
        Timer scopeTimer = null;

        // Renders one audio scope video frame from a block of decoded PCM samples.
        void RenderScope(short[] decoded)
        {
            Complex[] samples;

            if (audioScopeSource == AudioScopeSourceEnum.Synth)
            {
                // The pad chord would trace a wandering rosette rather than a circle, so the scope is
                // driven by its own clean, bin-aligned carrier instead. The carrier amplitude follows
                // the pad's loudness (a smoothed RMS envelope), so the circle still breathes with the
                // audio while staying a smooth circle.
                double sumSq = 0.0;
                foreach (var s in decoded)
                {
                    double n = s / 32768.0;
                    sumSq += n * n;
                }
                double rms = Math.Sqrt(sumSq / Math.Max(1, decoded.Length));
                _scopeEnv += 0.25 * (rms - _scopeEnv);          // smooth across packets
                double radius = 0.12 + 1.0 * _scopeEnv;         // map loudness to circle radius

                samples = new Complex[decoded.Length];
                for (int i = 0; i < samples.Length; i++)
                {
                    _synthPhase += 2.0 * Math.PI * SYNTH_FUNDAMENTAL_HZ / SYNTH_SAMPLE_RATE;
                    if (_synthPhase > 2.0 * Math.PI)
                    {
                        _synthPhase -= 2.0 * Math.PI;
                    }

                    samples[i] = new Complex(radius * Math.Sin(_synthPhase), 0.0);
                }
            }
            else
            {
                // Visualise the decoded audio (music or microphone) directly.
                samples = decoded.Select(s => new Complex(s / 32768f, 0f)).ToArray();
            }

            var frame = _renderer.ProcessAudioSample(samples);

            videoEndPoint.ExternalVideoSourceRawSample(SCOPE_FRAME_INTERVAL_MS,
                AudioScopeRenderer.Width,
                AudioScopeRenderer.Height,
                frame,
                VideoPixelFormatsEnum.Rgb);
        }

        // Fires every SCOPE_FRAME_INTERVAL_MS on a thread-pool thread. Renders the scope from the
        // most recent audio samples. If the previous render/encode is still running the tick is
        // skipped (frame dropped) rather than queued, so a slow encode can never build a backlog.
        void OnScopeTimer(object _)
        {
            if (Interlocked.CompareExchange(ref renderInProgress, 1, 0) != 0)
            {
                return;
            }

            try
            {
                short[] pcm;
                lock (pcmLock)
                {
                    pcm = latestPcm;
                }

                if (pcm != null)
                {
                    RenderScope(pcm);
                }
            }
            catch (Exception excp)
            {
                logger.LogError(excp, "Exception rendering audio scope frame. {ErrorMessage}", excp.Message);
            }
            finally
            {
                Interlocked.Exchange(ref renderInProgress, 0);
            }
        }

        // For music/synth this app generates the audio; for microphone the audio comes from the peer.
        AudioExtrasSource audioSource = null;

        if (isMicrophone)
        {
            // "Reverse" scope: the browser sends its microphone audio to us (receive only audio m-line)
            // and we render a scope of that received audio back as the video stream.
            MediaStreamTrack audioTrack = new MediaStreamTrack(supportedAudioFormats, MediaStreamStatusEnum.RecvOnly);
            pc.addTrack(audioTrack);

            pc.OnAudioFrameReceived += (EncodedAudioFrame frame) =>
            {
                // Decode the received microphone audio and stash it for the scope timer. The
                // render/encode happens on the timer thread, not here.
                var decoded = audioEncoder.DecodeAudio(frame.EncodedAudio, frame.AudioFormat);
                lock (pcmLock)
                {
                    latestPcm = decoded;
                }
            };
        }
        else
        {
            // Music / Synth: generate the audio, send it to the peer, and drive the scope from it.
            audioSource = new AudioExtrasSource(new AudioEncoder(),
                new AudioSourceOptions { AudioSource = audioScopeSource == AudioScopeSourceEnum.Synth ? AudioSourcesEnum.Pad : AudioSourcesEnum.Music });

            MediaStreamTrack audioTrack = new MediaStreamTrack(supportedAudioFormats, MediaStreamStatusEnum.SendOnly);
            pc.addTrack(audioTrack);

            audioSource.OnAudioSourceEncodedSample += pc.SendAudio;
            pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());

            audioSource.OnAudioSourceEncodedSample += (uint durationRtpUnits, byte[] sample) =>
            {
                // Fires once per audio packet on the audio source's pacing thread. Keep this cheap:
                // decode and stash only. The expensive scope render + VP8 encode runs on the scope
                // timer thread so audio packets are never delayed by video work (which made the
                // received audio choppy when done inline here).
                // Note: In theory the need to decode the audio sample should be avoidable if the scope can be
                // driven directly from the raw unencoded audio samples.
                var decoded = audioEncoder.DecodeAudio(sample, pc.AudioStream.NegotiatedFormat.ToAudioFormat());
                lock (pcmLock)
                {
                    latestPcm = decoded;
                }
            };
        }

        pc.oniceconnectionstatechange += (state) => logger.LogDebug($"ICE connection state change to {state}.");
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
                // Only the music/synth modes have a local audio source to start.
                if (audioSource != null)
                {
                    await audioSource.StartAudio();
                }

                // Start the scope video clock now the connection is up.
                scopeTimer ??= new Timer(OnScopeTimer, null, 0, SCOPE_FRAME_INTERVAL_MS);
            }
            else if (state == RTCPeerConnectionState.failed)
            {
                scopeTimer?.Dispose();
                scopeTimer = null;
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                scopeTimer?.Dispose();
                scopeTimer = null;

                if (audioSource != null)
                {
                    await audioSource.CloseAudio();
                }
            }
        };

        return Task.FromResult(pc);
    }

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
