//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A WebRTC demo that serves a stylised "Max Headroom" talking
// avatar. Video is rendered with SkiaSharp (MaxHeadroomVideoSource), speech is
// synthesised locally with Piper and the mouth is lip-synced to an amplitude
// envelope of that audio (PiperTtsSpeaker). An optional local LLM (Ollama / LM
// Studio / llama.cpp) generates the replies (LocalLlmClient). Everything runs
// offline, so the avatar can be containerised and deployed to Kubernetes.
//
// You can also TALK to the avatar: the browser sends its microphone over the same
// WebRTC connection, the server decodes it and runs local, offline speech-to-text
// (WhisperSpeechRecognizer / Whisper.net), and each recognised utterance is routed
// through the same LLM->speak path as /ask. Speaking and the Say/Ask text boxes are
// parallel inputs.
//
// Endpoints:
//   POST /offer  - WebRTC SDP offer/answer exchange (called by the browser). The
//                  audio track is send/recv: the avatar voice out, the mic in.
//   POST /say    - body = text. Speaks the text verbatim.
//   POST /ask    - body = prompt. Runs the prompt through the local LLM (if
//                  configured) and speaks the reply (same path as speaking to it).
//
// Configuration (environment variables) - Piper TTS, pick ONE mode:
//   PIPER_HTTP_URL       - recommended, the synthesis endpoint of a running `piper.http_server`.
//                          The voice loads once server-side, so this is much faster than
//                          spawning Piper per utterance. For piper-tts <= 1.4.2 this is the
//                          server root (e.g. http://localhost:5000); newer builds use
//                          .../synthesize. The app POSTs JSON {"text": ...} to this URL.
//   PIPER_PATH           - child-process mode, the Piper command: a `piper` console script,
//                          or a Python interpreter ("python"/"python3"), launched as
//                          `python -m piper`. Reloads the model every utterance (slow).
//   PIPER_MODEL          - required with PIPER_PATH: a voice name (resolved under
//                          PIPER_DATA_DIR) or a full path to a .onnx voice (.onnx.json sibling).
//   PIPER_DATA_DIR       - optional, directory holding downloaded voices (Piper's --data-dir);
//                          used when PIPER_MODEL is a voice name rather than a path.
//   WHISPER_MODEL        - optional path to a ggml Whisper model for local speech-to-text.
//                          Defaults to ggml-base.en.bin in the app directory, downloaded
//                          once on first run if absent (override source with WHISPER_MODEL_URL).
//   VISEME_LEAD_MS       - optional, ms to lead the mouth ahead of the audio (default 0).
//   LLM_ENDPOINT         - optional OpenAI-compatible chat completions URL.
//                          Local:      http://localhost:11434/v1/chat/completions (Ollama)
//                          OpenRouter: https://openrouter.ai/api/v1/chat/completions
//   LLM_MODEL            - optional, e.g. llama3.2 or anthropic/claude-3.5-sonnet
//   LLM_API_KEY          - optional, required only for hosted gateways (OpenRouter/OpenAI).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace demo;

class Program
{
    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;

    // The demo drives a single connected viewer.
    private static MaxHeadroomVideoSource _videoSource;
    private static AudioExtrasSource _audioSource;
    private static PiperTtsSpeaker _speaker;
    private static WhisperSpeechRecognizer _recognizer;
    private static LocalLlmClient _llm;

    private static string _piperHttpUrl;
    private static string _piperPath;
    private static string _piperModel;
    private static string _piperDataDir;
    private static string _whisperModel;
    private static int _visemeLeadMs = 0;

    static async Task Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Debug)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var factory = new SerilogLoggerFactory(Log.Logger);
        SIPSorcery.LogFactory.Set(factory);
        _logger = factory.CreateLogger<Program>();

        _logger.LogInformation("WebRTC Max Headroom Avatar Demo");

        // Quick visual sanity check: render a few frames to PNG and exit.
        if (Array.Exists(Environment.GetCommandLineArgs(), a => a == "--snapshot"))
        {
            using var preview = new MaxHeadroomVideoSource();
            foreach (var v in new[] { 0, 2, 7, 21 })
            {
                preview.SaveSnapshot($"maxheadroom_viseme{v}.png", v);
            }
            _logger.LogInformation("Wrote snapshot PNGs to {Dir}.", Environment.CurrentDirectory);
            return;
        }

        _piperHttpUrl = Environment.GetEnvironmentVariable("PIPER_HTTP_URL");
        _piperPath = Environment.GetEnvironmentVariable("PIPER_PATH");
        _piperModel = Environment.GetEnvironmentVariable("PIPER_MODEL");
        _piperDataDir = Environment.GetEnvironmentVariable("PIPER_DATA_DIR");
        _whisperModel = Environment.GetEnvironmentVariable("WHISPER_MODEL");
        if (int.TryParse(Environment.GetEnvironmentVariable("VISEME_LEAD_MS"), out var lead)) { _visemeLeadMs = lead; }

        if (!PiperConfigured())
        {
            _logger.LogWarning("Piper TTS not configured (set PIPER_HTTP_URL, or PIPER_PATH + PIPER_MODEL). The avatar will render but cannot speak or listen.");
        }

        _llm = new LocalLlmClient(
            Environment.GetEnvironmentVariable("LLM_ENDPOINT"),
            Environment.GetEnvironmentVariable("LLM_MODEL"),
            Environment.GetEnvironmentVariable("LLM_API_KEY"));
        _logger.LogInformation("Local LLM {State}.", _llm.IsConfigured ? $"configured with endpoint {_llm.Endpoint} and model {_llm.Model}" : " not configured (text will be spoken verbatim)");

        var builder = WebApplication.CreateBuilder();
        builder.Host.UseSerilog();
        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapPost("/offer", HandleOffer);

        app.MapPost("/say", async (HttpRequest request) =>
        {
            var text = await ReadBody(request);
            var notReady = SpeakerNotReady();
            if (notReady != null) { return notReady; }
            _ = _speaker.SpeakAsync(text);
            return Results.Ok();
        });

        app.MapPost("/ask", async (HttpRequest request) =>
        {
            var prompt = await ReadBody(request);
            var notReady = SpeakerNotReady();
            if (notReady != null) { return notReady; }

            var text = await AskAsync(prompt);
            return Results.Text(text);
        });

        await app.RunAsync();
    }

    /// <summary>
    /// Runs a prompt through the LLM and speaks the reply, streaming sentence-by-sentence so the
    /// avatar starts talking on the first sentence instead of waiting for the whole completion. A
    /// single background consumer speaks the sentences in order (the speaker also serialises
    /// internally) while generation continues unblocked. Returns the assembled reply text; speech
    /// finishes in the background. Shared by the /ask endpoint and the speech recogniser, so typing
    /// a prompt and speaking one drive the exact same path.
    /// </summary>
    private static async Task<string> AskAsync(string prompt)
    {
        // Capture the speaker so a mid-stream disconnect (which nulls _speaker) can't throw inside
        // the background consumer below; bail quietly if the avatar can't speak right now.
        var speaker = _speaker;
        if (speaker == null || string.IsNullOrWhiteSpace(prompt))
        {
            return string.Empty;
        }

        var sentences = Channel.CreateUnbounded<string>();
        var speakTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var sentence in sentences.Reader.ReadAllAsync())
                {
                    await speaker.SpeakAsync(sentence);
                }
            }
            catch (Exception excp)
            {
                _logger.LogError(excp, "Error speaking streamed reply.");
            }
        });

        var reply = new StringBuilder();
        await foreach (var sentence in _llm.StreamReplyAsync(prompt))
        {
            reply.Append(sentence).Append(' ');
            await sentences.Writer.WriteAsync(sentence);
        }
        sentences.Writer.Complete();

        var text = reply.ToString().Trim();
        _logger.LogInformation("LLM reply: {Reply}", text);
        return text;
    }

    private static async Task<IResult> HandleOffer(HttpRequest request)
    {
        var sdpOffer = await ReadBody(request);
        _logger.LogDebug("Received SDP offer.");

        var pc = CreatePeerConnection();

        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = sdpOffer, type = RTCSdpType.offer });
        if (result != SetDescriptionResultEnum.OK)
        {
            _logger.LogError("Failed to set remote description: {Result}", result);
            return Results.BadRequest(result.ToString());
        }

        var answer = pc.createAnswer();
        await pc.setLocalDescription(answer);

        return Results.Text(pc.localDescription.sdp.ToString());
    }

    private static RTCPeerConnection CreatePeerConnection()
    {
        var pc = new RTCPeerConnection(null);

        var videoSource = new MaxHeadroomVideoSource(new FFmpegVideoEncoder());
        var videoTrack = new MediaStreamTrack(videoSource.GetVideoSourceFormats(), MediaStreamStatusEnum.SendOnly);
        pc.addTrack(videoTrack);
        videoSource.OnVideoSourceEncodedSample += pc.SendVideo;
        pc.OnVideoFormatsNegotiated += formats => videoSource.SetVideoSourceFormat(formats.First());

        // Use Silence rather than None so audio RTP packets flow continuously between
        // prompts. A continuous audio clock keeps the browser's jitter buffer and the
        // RTCP A/V sync stable; with bursty audio (None) the lip-sync drifts ahead of
        // the voice over successive prompts.
        var audioSource = new AudioExtrasSource(new AudioEncoder(), new AudioSourceOptions { AudioSource = AudioSourcesEnum.Silence });
        // Restrict to PCMU so the call audio - and the received microphone - is a deterministic 8kHz
        // G.711 stream, which the speech recogniser consumes after decoding.
        audioSource.RestrictFormats(f => f.Codec == AudioCodecsEnum.PCMU);
        // SendRecv (not SendOnly) so the browser microphone reaches the server for speech recognition.
        var audioTrack = new MediaStreamTrack(audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
        pc.addTrack(audioTrack);
        audioSource.OnAudioSourceEncodedSample += pc.SendAudio;
        pc.OnAudioFormatsNegotiated += formats => audioSource.SetAudioSourceFormat(formats.First());

        PiperTtsSpeaker speaker = null;
        WhisperSpeechRecognizer recognizer = null;
        if (PiperConfigured())
        {
            speaker = new PiperTtsSpeaker(_piperHttpUrl, _piperPath, _piperModel, _piperDataDir, videoSource, audioSource, _visemeLeadMs);

            // Speech-to-text: decode the received microphone RTP (PCMU -> 8kHz PCM) and feed local Whisper STT.
            // Recognised utterances run through the same LLM->speak path as /ask, so typing a prompt and
            // speaking one are parallel inputs to the exact same pipeline.
            recognizer = new WhisperSpeechRecognizer(_whisperModel);
            recognizer.OnRecognized += text => _ = AskAsync(text);

            var micDecoder = new AudioEncoder();
            var pcmuFormat = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU);
            pc.OnAudioFrameReceived += frame =>
            {
                try
                {
                    recognizer.Write(micDecoder.DecodeAudio(frame.EncodedAudio, pcmuFormat));
                }
                catch (Exception excp)
                {
                    _logger.LogWarning("Failed to decode received microphone audio: {Error}", excp.Message);
                }
            };
        }

        pc.onconnectionstatechange += async (state) =>
        {
            _logger.LogDebug("Peer connection state change to {State}.", state);

            switch (state)
            {
                case RTCPeerConnectionState.connected:
                    // Register as the active call first so /say and /ask are reachable
                    // even if media start-up below hiccups.
                    _videoSource = videoSource;
                    _audioSource = audioSource;
                    _speaker = speaker;
                    _recognizer = recognizer;
                    try
                    {
                        await audioSource.StartAudio();
                        await videoSource.StartVideo();
                        if (speaker != null)
                        {
                            _ = speaker.SpeakAsync("M-m-max Headroom here. Welcome to the show!");
                        }
                        else
                        {
                            _logger.LogWarning("Connected but Piper TTS is not configured; the avatar cannot speak or listen.");
                        }
                        if (recognizer != null)
                        {
                            await recognizer.StartAsync();
                        }
                    }
                    catch (Exception excp)
                    {
                        _logger.LogError(excp, "Error starting avatar media on connect.");
                    }
                    break;

                case RTCPeerConnectionState.failed:
                    pc.Close("ice disconnection");
                    break;

                case RTCPeerConnectionState.closed:
                    await audioSource.CloseAudio();
                    await videoSource.CloseVideo();
                    videoSource.Dispose();
                    recognizer?.Dispose();
                    if (_videoSource == videoSource) { _videoSource = null; _audioSource = null; _speaker = null; _recognizer = null; }
                    break;
            }
        };

        pc.oniceconnectionstatechange += (state) => _logger.LogDebug("ICE connection state change to {State}.", state);

        return pc;
    }

    /// <summary>
    /// Returns a descriptive 400 result if the avatar can't speak yet, otherwise null.
    /// Distinguishes "no active call" from "TTS not configured" so the cause is obvious.
    /// </summary>
    private static IResult SpeakerNotReady()
    {
        if (_videoSource == null)
        {
            return Results.BadRequest("No active call. Connect a viewer in the browser first.");
        }
        if (_speaker == null)
        {
            return Results.BadRequest("Piper TTS is not configured. Set PIPER_HTTP_URL (or PIPER_PATH + PIPER_MODEL), then restart.");
        }
        return null;
    }

    /// <summary>True if Piper TTS is configured, either via the HTTP server or the child-process mode.</summary>
    private static bool PiperConfigured() =>
        !string.IsNullOrWhiteSpace(_piperHttpUrl) ||
        (!string.IsNullOrWhiteSpace(_piperPath) && !string.IsNullOrWhiteSpace(_piperModel));

    private static async Task<string> ReadBody(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }
}
