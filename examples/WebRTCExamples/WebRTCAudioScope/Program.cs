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
using SIPSorceryMedia.FFmpeg;

namespace demo;

class Program
{
    private const int WEBSOCKET_PORT = 8081;
    private const int AUDIO_PACKET_DURATION = 20; // 20ms of audio per RTP packet for PCMU & PCMA.
    private const string LINUX_FFMPEG_LIB_PATH = "/usr/local/lib/";

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

        if (Environment.OSVersion.Platform == PlatformID.Unix)
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, LINUX_FFMPEG_LIB_PATH, logger);
        }
        else
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_VERBOSE, null, logger);
        }

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

        //var videoEncoderEndPoint = new Vp8NetVideoEncoderEndPoint();
        var videoEncoderEndPoint = new FFmpegVideoSource();
        var audioEncoder = new AudioEncoder();

        var audioSource = new AudioExtrasSource(new AudioEncoder(),
            new AudioSourceOptions { AudioSource = audioScopeSource == AudioScopeSourceEnum.Synth ? AudioSourcesEnum.Pad : AudioSourcesEnum.Music });

        // For the sake of the demo stick to a basic audio format with a predictable sampling rate.
        var supportedAudioFormats = new List<AudioFormat>
        {
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU),
            new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA),
        };

        MediaStreamTrack videoTrack = new MediaStreamTrack(videoEncoderEndPoint.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);
        MediaStreamTrack audioTrack = new MediaStreamTrack(supportedAudioFormats, MediaStreamStatusEnum.SendOnly);
        pc.addTrack(audioTrack);

        videoEncoderEndPoint.OnVideoSourceEncodedSample += pc.SendVideo;
        audioSource.OnAudioSourceEncodedSample += pc.SendAudio;

        pc.OnVideoFormatsNegotiated += (formats) => videoEncoderEndPoint.SetVideoSourceFormat(formats.First());
        pc.OnAudioFormatsNegotiated += (formats) => audioSource.SetAudioSourceFormat(formats.First());
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

        audioSource.OnAudioSourceEncodedSample += (uint durationRtpUnits, byte[] sample) =>
        {
            // Fires once per audio packet. The audio itself is produced and sent to the peer by the
            // audio source above; here we only build the scope's input and render the video frame.
            // Note: In theory the need to decode the audio sample should be avoidable if the scope can be 
            // driven directly from the raw unencoded audio samples.
            var decoded = audioEncoder.DecodeAudio(sample, pc.AudioStream.NegotiatedFormat.ToAudioFormat());

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
                // Visualise the decoded music directly.
                samples = decoded.Select(s => new Complex(s / 32768f, 0f)).ToArray();
            }

            var frame = _renderer.ProcessAudioSample(samples);

            videoEncoderEndPoint.ExternalVideoSourceRawSample(AUDIO_PACKET_DURATION,
                AudioScopeRenderer.Width,
                AudioScopeRenderer.Height,
                frame,
                VideoPixelFormatsEnum.Rgb);
        };

        pc.onconnectionstatechange += async (state) =>
        {
            logger.LogDebug($"Peer connection state change to {state}.");

            if (state == RTCPeerConnectionState.connected)
            {
                await audioSource.StartAudio();
            }
            else if (state == RTCPeerConnectionState.failed)
            {
                pc.Close("ice disconnection");
            }
            else if (state == RTCPeerConnectionState.closed)
            {
                await audioSource.CloseAudio();
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
