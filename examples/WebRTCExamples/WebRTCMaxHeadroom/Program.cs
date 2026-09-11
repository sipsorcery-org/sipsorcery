//-----------------------------------------------------------------------------
// Filename: Program.cs
//
// Description: A WebRTC demo that serves a "Max Headroom" talking avatar - a photoreal
// Wav2Lip head (Wav2LipAvatarRenderer) or a SkiaSharp cartoon (MaxHeadroomVideoSource).
// The whole AI stack runs IN-PROCESS by default: TTS and STT via sherpa-onnx
// (SherpaTtsSpeaker / SherpaSpeechRecognizer), replies via LLamaSharp
// (LlamaSharpLlmClient) and the lip-sync model via onnxruntime. Each engine activates
// automatically when its model files are present in the conventional folders (see the
// README's "Everything in-process" section) - no env vars needed.
//
// You can also TALK to the avatar: the browser sends its microphone over the same
// WebRTC connection, the server decodes it and runs local speech-to-text, and each
// recognised utterance is routed through the same LLM->speak path as /ask. Speaking
// and the Say/Ask text boxes are parallel inputs.
//
// Endpoints:
//   POST /offer  - WebRTC SDP offer/answer exchange (called by the browser). The
//                  audio track is send/recv: the avatar voice out, the mic in.
//   POST /say    - body = text. Speaks the text verbatim.
//   POST /ask    - body = prompt. Runs the prompt through the LLM (if configured)
//                  and speaks the reply (same path as speaking to it).
//
// Configuration (environment variables, all optional):
//   SHERPA_MODEL_DIR / SHERPA_STT_DIR / SHERPA_STT_PROVIDER - sherpa TTS/STT model
//                          folder overrides and STT execution provider.
//   LLM_GGUF / LLM_GPU_LAYERS - in-process LLM model path + GPU offload layers.
//   LLM_ENDPOINT / LLM_MODEL / LLM_API_KEY - OpenAI-compatible endpoint alternative
//                          (Ollama / LM Studio / hosted gateway).
//   AVATAR_RENDERER      - wav2lip | cartoon. Defaults to wav2lip when its model
//                          files exist.
//   WAV2LIP_ONNX / NEURAL_PERSONA / NEURAL_MATTE / NEURAL_FACE_BOX / NEURAL_EYES -
//                          avatar model, persona image, matte and persona geometry.
//   VISEME_LEAD_MS       - ms to lead the mouth ahead of the audio (default 0).
//   ELEVENLABS_API_KEY (+ ELEVENLABS_VOICE_ID/MODEL/STT_MODEL/STREAMING/...) - cloud
//                          speech engines; when set they take priority for TTS + STT.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    private static readonly ConcurrentDictionary<Guid, Channel<string>> _uiEventClients = new();

    // The demo drives a single connected viewer.
    private static IAvatarRenderer _videoSource;
    private static AudioExtrasSource _audioSource;
    private static IAvatarSpeaker _speaker;
    private static ISpeechRecognizer _recognizer;
    private static ILlmClient _llm;

    private static string _sherpaModelDir;
    private static string _elevenLabsKey;
    private static string _elevenLabsVoiceId;
    private static string _elevenLabsModel;
    private static string _elevenLabsSttModel;
    private static string _elevenLabsSttRealtimeModel;
    private static bool _elevenLabsStreaming;
    private static int _visemeLeadMs = 0;
    private static bool _waitForIceGatheringToSendAnswer;
    private static string _stunUrl = string.Empty;

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

        // Windows timers default to ~15.6ms resolution, which stretches the 25fps render
        // timer to ~46ms ticks - video falls behind the (correctly paced) audio and the lip
        // sync drifts. Ask for 1ms resolution, the same fix the Python sidecar needed.
        if (OperatingSystem.IsWindows())
        {
            _ = TimeBeginPeriod(1);
        }

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

        // In-process is the default: each engine has a conventional model path and is used
        // automatically when its files are present (env vars override; see the README's
        // "Everything in-process" section).
        _sherpaModelDir = Environment.GetEnvironmentVariable("SHERPA_MODEL_DIR")
            ?? @"C:\tools\sherpa-tts\vits-piper-en_US-ryan-high";
        _elevenLabsKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
        _elevenLabsVoiceId = Environment.GetEnvironmentVariable("ELEVENLABS_VOICE_ID") ?? "21m00Tcm4TlvDq8ikWAM"; // "Rachel".
        _elevenLabsModel = Environment.GetEnvironmentVariable("ELEVENLABS_MODEL") ?? "eleven_turbo_v2_5";
        _elevenLabsSttModel = Environment.GetEnvironmentVariable("ELEVENLABS_STT_MODEL") ?? "scribe_v1";
        _elevenLabsSttRealtimeModel = Environment.GetEnvironmentVariable("ELEVENLABS_STT_REALTIME_MODEL") ?? "scribe_v2_realtime";
        _elevenLabsStreaming = string.Equals(Environment.GetEnvironmentVariable("ELEVENLABS_STREAMING"), "true", StringComparison.OrdinalIgnoreCase);
        if (int.TryParse(Environment.GetEnvironmentVariable("VISEME_LEAD_MS"), out var lead)) { _visemeLeadMs = lead; }
        bool.TryParse(Environment.GetEnvironmentVariable("WAIT_FOR_ICE_GATHERING_TO_SEND_ANSWER"), out _waitForIceGatheringToSendAnswer);
        _stunUrl = Environment.GetEnvironmentVariable("STUN_URL");

        // Synthesise a phrase to a WAV and exit - validates a TTS engine without a browser.
        var argv = Environment.GetCommandLineArgs();

        // Golden-file check of the C# mel front-end against a librosa-generated reference:
        //   --mel-test <raw-int16-pcm> <reference-csv>
        // (the reference was produced with Wav2Lip's Python audio.py; see MelSpectrogram.cs).
        int melTestIdx = Array.IndexOf(argv, "--mel-test");
        if (melTestIdx >= 0 && melTestIdx + 2 < argv.Length)
        {
            RunMelTest(argv[melTestIdx + 1], argv[melTestIdx + 2]);
            return;
        }

        // Render in-process Wav2Lip frames from raw PCM to PNGs and exit:
        //   --avatar-test <raw-int16-pcm> <out-dir>
        int avatarTestIdx = Array.IndexOf(argv, "--avatar-test");
        if (avatarTestIdx >= 0 && avatarTestIdx + 2 < argv.Length)
        {
            var bytes = File.ReadAllBytes(argv[avatarTestIdx + 1]);
            var pcm = new short[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, pcm, 0, pcm.Length * 2);
            using var renderer = new Wav2LipAvatarRenderer(encoder: null);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            renderer.TestRenderFrames(pcm, argv[avatarTestIdx + 2]);
            _logger.LogInformation("Rendered 50 in-process frames in {Ms} ms ({PerFrame:F1} ms/frame) to {Dir}.",
                sw.ElapsedMilliseconds, sw.ElapsedMilliseconds / 50.0, argv[avatarTestIdx + 2]);
            return;
        }
        int ttsTestIdx = Array.IndexOf(argv, "--tts-test");
        if (ttsTestIdx >= 0)
        {
            var text = ttsTestIdx + 1 < argv.Length ? argv[ttsTestIdx + 1] : "Max Headroom here, live and in stereo.";
            await RunTtsTest(text);
            return;
        }

        // TTS -> STT round trip and exit: synthesises a phrase with sherpa TTS then transcribes
        // it with the sherpa STT engine - validates both without a browser (--stt-test).
        int sttTestIdx = Array.IndexOf(argv, "--stt-test");
        if (sttTestIdx >= 0)
        {
            var text = sttTestIdx + 1 < argv.Length ? argv[sttTestIdx + 1] : "The quick brown fox jumps over the lazy dog.";
            await RunSttTest(text);
            return;
        }

        if (!TtsConfigured())
        {
            _logger.LogWarning("No TTS configured (download a voice folder to C:\\tools\\sherpa-tts or set SHERPA_MODEL_DIR / ELEVENLABS_API_KEY). The avatar will render but cannot speak or listen.");
        }

        _llm = CreateLlm();
        _logger.LogInformation("Local LLM {State}.", _llm.IsConfigured ? $"configured with {_llm.Description}" : " not configured (text will be spoken verbatim)");

        // Generate one in-character reply and exit - validates the LLM without a browser.
        int llmTestIdx = Array.IndexOf(argv, "--llm-test");
        if (llmTestIdx >= 0)
        {
            var prompt = llmTestIdx + 1 < argv.Length ? argv[llmTestIdx + 1] : "What do you think of television these days?";
            // Deliberately race the warm-up against the question - the production shape that
            // crashed llama.cpp before inference was serialised inside the client.
            var warmup = _llm.WarmUpAsync();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var reply = await _llm.GenerateReplyAsync(prompt);
            _logger.LogInformation("LLM reply in {Ms} ms: {Reply}", sw.ElapsedMilliseconds, reply);
            await warmup;
            (_llm as IDisposable)?.Dispose();
            return;
        }

        // Warm the in-process engines in the background so the first call/reply/utterance
        // doesn't pay their one-time costs (weights page-in, DirectML kernel compilation,
        // first-synthesis setup). Fire-and-forget: the app serves while they warm.
        _ = _llm.WarmUpAsync();
        if (SherpaConfigured())
        {
            _ = SherpaTtsSpeaker.PreloadAsync(_sherpaModelDir);
        }
        if (SherpaSpeechRecognizer.FilesPresent())
        {
            _ = SherpaSpeechRecognizer.PreloadAsync();
        }
        var rendererKind = Environment.GetEnvironmentVariable("AVATAR_RENDERER");
        if (string.Equals(rendererKind, "wav2lip", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(rendererKind) && Wav2LipAvatarRenderer.FilesPresent()))
        {
            _ = Wav2LipAvatarRenderer.PreloadAsync();
        }

        var builder = WebApplication.CreateBuilder();
        builder.Host.UseSerilog();
        var app = builder.Build();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));
        app.MapGet("/events", StreamUiEvents);
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

        // A streaming speaker consumes the LLM token stream directly over one WebSocket; tee the
        // sentences into a builder so we can still return the assembled reply text.
        if (speaker is IStreamingAvatarSpeaker streaming)
        {
            var streamed = new StringBuilder();
            async IAsyncEnumerable<string> Tee()
            {
                await foreach (var sentence in _llm.StreamReplyAsync(prompt))
                {
                    streamed.Append(sentence).Append(' ');
                    yield return sentence;
                }
            }

            await streaming.SpeakStreamAsync(Tee());
            var streamedText = streamed.ToString().Trim();
            _logger.LogInformation("LLM reply: {Reply}", streamedText);
            return streamedText;
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

    /// <summary>
    /// Handles microphone input separately from typed /ask requests so the browser activity
    /// drawer can show the STT result followed by the reply generated for that utterance.
    /// </summary>
    private static async Task HandleRecognizedSpeechAsync(string text)
    {
        PublishUiEvent("stt", text);
        try
        {
            var reply = await AskAsync(text);
            if (!string.IsNullOrWhiteSpace(reply))
            {
                PublishUiEvent("llm", reply);
            }
        }
        catch (Exception excp)
        {
            _logger.LogError(excp, "Error handling recognized speech.");
        }
    }

    /// <summary>Streams speech activity to each open browser using server-sent events.</summary>
    private static async Task StreamUiEvents(HttpContext context)
    {
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Append("X-Accel-Buffering", "no");

        var clientId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        _uiEventClients[clientId] = channel;

        try
        {
            await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            await foreach (var message in channel.Reader.ReadAllAsync(context.RequestAborted))
            {
                await context.Response.WriteAsync($"data: {message}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The browser closed or refreshed the page.
        }
        finally
        {
            _uiEventClients.TryRemove(clientId, out _);
            channel.Writer.TryComplete();
        }
    }

    private static void PublishUiEvent(string type, string text)
    {
        var message = JsonSerializer.Serialize(new { type, text });
        foreach (var client in _uiEventClients.Values)
        {
            client.Writer.TryWrite(message);
        }
    }

    private static async Task<IResult> HandleOffer(HttpRequest request)
    {
        var sdpOffer = await ReadBody(request);
        _logger.LogDebug("Received SDP offer:\n{offer}", sdpOffer);

        var pc = CreatePeerConnection();

        var result = pc.setRemoteDescription(new RTCSessionDescriptionInit { sdp = sdpOffer, type = RTCSdpType.offer });
        if (result != SetDescriptionResultEnum.OK)
        {
            _logger.LogError("Failed to set remote description: {Result}", result);
            return Results.BadRequest(result.ToString());
        }

        var answer = pc.createAnswer(new RTCAnswerOptions { X_WaitForIceGatheringToComplete= _waitForIceGatheringToSendAnswer });
        _logger.LogDebug("Created SDP answer (wait for ICE gathering was {WaitForIceGathering}):\n{answer}", _waitForIceGatheringToSendAnswer, answer.sdp);
        await pc.setLocalDescription(answer);

        return Results.Text(pc.localDescription.sdp.ToString());
    }

    private static RTCPeerConnection CreatePeerConnection()
    {
        var config = new RTCConfiguration
        {
            X_ICEIncludeAllInterfaceAddresses = true
        };

        if (!string.IsNullOrWhiteSpace(_stunUrl))
        {
            config.iceServers = new List<RTCIceServer>();
            config.iceServers.Add(_stunUrl.ParseStunServer());
        }

        var pc = new RTCPeerConnection(config);

        IAvatarRenderer videoSource = CreateRenderer(new FFmpegVideoEncoder());
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

        IAvatarSpeaker speaker = CreateSpeaker(videoSource, audioSource);
        ISpeechRecognizer recognizer = null;
        if (speaker != null)
        {
            // Speech-to-text: decode the received microphone RTP (PCMU -> 8kHz PCM) and feed the STT engine.
            // Recognised utterances run through the same LLM->speak path as /ask, so typing a prompt and
            // speaking one are parallel inputs to the exact same pipeline.
            recognizer = CreateRecognizer();
            if (recognizer != null)
            {
                recognizer.OnRecognized += text => _ = HandleRecognizedSpeechAsync(text);

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
                            _logger.LogWarning("Connected but no TTS is configured; the avatar cannot speak or listen.");
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
            return Results.BadRequest("No TTS configured. Download a voice folder to C:\\tools\\sherpa-tts or set SHERPA_MODEL_DIR / ELEVENLABS_API_KEY, then restart.");
        }
        return null;
    }

    /// <summary>
    /// Builds the avatar renderer (the swappable IAvatarRenderer): the in-process Wav2Lip head
    /// when its model files are present, else the SkiaSharp cartoon. Nothing else in the
    /// pipeline changes - the speaker and peer-connection wiring only see IAvatarRenderer.
    /// </summary>
    private static IAvatarRenderer CreateRenderer(IVideoEncoder encoder)
    {
        var kind = Environment.GetEnvironmentVariable("AVATAR_RENDERER");

        // Default: the in-process Wav2Lip renderer whenever its model + persona files are
        // present (the cartoon needs nothing, so it is the fallback). AVATAR_RENDERER
        // overrides: wav2lip | cartoon.
        if (string.IsNullOrWhiteSpace(kind))
        {
            kind = Wav2LipAvatarRenderer.FilesPresent() ? "wav2lip" : "cartoon";
            _logger.LogInformation("AVATAR_RENDERER not set; defaulting to {Kind}.", kind);
        }

        if (string.Equals(kind, "wav2lip", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Using the IN-PROCESS Wav2Lip avatar renderer.");
            return new Wav2LipAvatarRenderer(encoder);
        }
        return new MaxHeadroomVideoSource(encoder);
    }

    /// <summary>
    /// Builds the TTS speaker from configuration: ElevenLabs (cloud) takes priority if an API key
    /// is set, otherwise the local in-process sherpa-onnx engine.
    /// Returns null if no TTS is configured.
    /// </summary>
    private static IAvatarSpeaker CreateSpeaker(IAvatarRenderer renderer, AudioExtrasSource audio)
    {
        if (!string.IsNullOrWhiteSpace(_elevenLabsKey))
        {
            return _elevenLabsStreaming
                ? new ElevenLabsStreamingTtsSpeaker(_elevenLabsKey, _elevenLabsVoiceId, _elevenLabsModel, renderer, audio, _visemeLeadMs)
                : new ElevenLabsTtsSpeaker(_elevenLabsKey, _elevenLabsVoiceId, _elevenLabsModel, renderer, audio, _visemeLeadMs);
        }
        if (SherpaConfigured())
        {
            return new SherpaTtsSpeaker(_sherpaModelDir, renderer, audio, _visemeLeadMs);
        }
        return null;
    }

    /// <summary>
    /// Synthesises <paramref name="text"/> with the configured local in-process TTS and writes
    /// a WAV next to the app - a quick engine check with no WebRTC involved (--tts-test).
    /// </summary>
    private static async Task RunTtsTest(string text)
    {
        if (!SherpaConfigured())
        {
            _logger.LogError("--tts-test requires SHERPA_MODEL_DIR pointing at an extracted vits-piper voice.");
            return;
        }

        using var speaker = new SherpaTtsSpeaker(_sherpaModelDir, renderer: null, audio: null);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var (samples, rate) = await speaker.TestSynthesiseAsync(text);
        sw.Stop();

        var path = Path.Combine(Environment.CurrentDirectory, "tts_test.wav");
        WriteWav(path, samples, rate);
        _logger.LogInformation("Synthesised {Ms} ms of audio in {Elapsed} ms -> {Path}",
            samples.Length * 1000 / Math.Max(1, rate), sw.ElapsedMilliseconds, path);
    }

    /// <summary>
    /// Compares the C# MelSpectrogram against a Python-generated reference: raw int16 PCM in,
    /// CSV of the librosa mel out. Reports max/mean absolute difference (--mel-test).
    /// </summary>
    private static void RunMelTest(string pcmPath, string csvPath)
    {
        var bytes = File.ReadAllBytes(pcmPath);
        var pcm = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, pcm, 0, pcm.Length * 2);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var mel = new MelSpectrogram().Compute(pcm);
        sw.Stop();

        var lines = File.ReadAllLines(csvPath);
        int rows = lines.Length;
        int cols = lines[0].Split(',').Length;
        _logger.LogInformation("C# mel [{M},{T}] in {Ms} ms; reference [{RM},{RT}].",
            mel.GetLength(0), mel.GetLength(1), sw.ElapsedMilliseconds, rows, cols);

        if (mel.GetLength(0) != rows || mel.GetLength(1) != cols)
        {
            _logger.LogError("SHAPE MISMATCH - the STFT framing does not match librosa.");
            return;
        }

        double maxDiff = 0, sumDiff = 0;
        for (int m = 0; m < rows; m++)
        {
            var parts = lines[m].Split(',');
            for (int t = 0; t < cols; t++)
            {
                double diff = Math.Abs(mel[m, t] - double.Parse(parts[t], System.Globalization.CultureInfo.InvariantCulture));
                if (diff > maxDiff) { maxDiff = diff; }
                sumDiff += diff;
            }
        }
        _logger.LogInformation("Mel diff vs Python reference: max {Max:E3}, mean {Mean:E3} (range is [-4,4]).",
            maxDiff, sumDiff / (rows * cols));
    }

    /// <summary>
    /// Round-trips text through the in-process pipeline the live call uses: sherpa TTS
    /// synthesises it, the PCM is downsampled to the mic path's 8kHz, and the configured
    /// STT engine transcribes it back (--stt-test).
    /// </summary>
    private static async Task RunSttTest(string text)
    {
        if (!SherpaConfigured() || !SherpaSpeechRecognizer.FilesPresent())
        {
            _logger.LogError("--stt-test needs both the sherpa TTS voice and STT model folders (see the README).");
            return;
        }

        using var speaker = new SherpaTtsSpeaker(_sherpaModelDir, renderer: null, audio: null);
        var (samples, rate) = await speaker.TestSynthesiseAsync(text);

        // Down to the 8kHz the mic path delivers (matches the live PCMU audio track).
        var pcm8k = new short[(int)((long)samples.Length * 8000 / rate)];
        double step = rate / 8000.0;
        for (int i = 0; i < pcm8k.Length; i++)
        {
            double pos = i * step;
            int i0 = (int)pos;
            int i1 = Math.Min(i0 + 1, samples.Length - 1);
            pcm8k[i] = (short)(samples[i0] + (samples[i1] - samples[i0]) * (pos - i0));
        }

        var recognizer = new SherpaSpeechRecognizer();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var transcript = await recognizer.TestTranscribeAsync(pcm8k);
        _logger.LogInformation("STT round trip in {Ms} ms.\n  said:  {Said}\n  heard: {Heard}",
            sw.ElapsedMilliseconds, text, transcript.Trim());
    }

    /// <summary>Minimal 16-bit mono PCM WAV writer for the --tts-test output.</summary>
    private static void WriteWav(string path, short[] samples, int sampleRate)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        int dataLen = samples.Length * sizeof(short);
        bw.Write("RIFF"u8); bw.Write(36 + dataLen); bw.Write("WAVE"u8);
        bw.Write("fmt "u8); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
        bw.Write(sampleRate); bw.Write(sampleRate * sizeof(short)); bw.Write((short)sizeof(short)); bw.Write((short)16);
        bw.Write("data"u8); bw.Write(dataLen);
        foreach (var s in samples) { bw.Write(s); }
    }

    /// <summary>
    /// Builds the STT recogniser from configuration: ElevenLabs (cloud) if an API key is set -
    /// realtime WebSocket when ELEVENLABS_STREAMING is on, otherwise the batch scribe API - and
    /// the local in-process sherpa-onnx engine when its model folder is present. Returns null
    /// when neither is available (the avatar then speaks but cannot listen).
    /// </summary>
    private static ISpeechRecognizer CreateRecognizer()
    {
        if (!string.IsNullOrWhiteSpace(_elevenLabsKey))
        {
            return _elevenLabsStreaming
                ? new ElevenLabsStreamingSpeechRecognizer(_elevenLabsKey, _elevenLabsSttRealtimeModel)
                : new ElevenLabsSpeechRecognizer(_elevenLabsKey, _elevenLabsSttModel);
        }
        if (SherpaSpeechRecognizer.FilesPresent())
        {
            return new SherpaSpeechRecognizer();
        }
        _logger.LogWarning("No STT configured (download a model folder to C:\\tools\\sherpa-stt or set SHERPA_STT_DIR / ELEVENLABS_API_KEY). The avatar can speak but not listen.");
        return null;
    }

    /// <summary>
    /// Builds the LLM client: in-process LLamaSharp when LLM_GGUF points at a model file,
    /// otherwise the HTTP client for an OpenAI-compatible endpoint (Ollama / LM Studio /
    /// hosted gateway) - which is also the "not configured" fallback that echoes prompts.
    /// </summary>
    private static ILlmClient CreateLlm()
    {
        // In-process by default: use the conventional GGUF location when present.
        var gguf = Environment.GetEnvironmentVariable("LLM_GGUF");
        if (string.IsNullOrWhiteSpace(gguf))
        {
            var conventional = @"C:\tools\llm";
            gguf = Directory.Exists(conventional)
                ? Directory.EnumerateFiles(conventional, "*.gguf").FirstOrDefault()
                : null;
        }
        if (!string.IsNullOrWhiteSpace(gguf) && File.Exists(gguf))
        {
            int.TryParse(Environment.GetEnvironmentVariable("LLM_GPU_LAYERS"), out var gpuLayers);
            return new LlamaSharpLlmClient(gguf, gpuLayers);
        }
        return new LocalLlmClient(
            Environment.GetEnvironmentVariable("LLM_ENDPOINT"),
            Environment.GetEnvironmentVariable("LLM_MODEL"),
            Environment.GetEnvironmentVariable("LLM_API_KEY"));
    }

    /// <summary>True if any TTS engine is configured (ElevenLabs or sherpa-onnx).</summary>
    private static bool TtsConfigured() =>
        !string.IsNullOrWhiteSpace(_elevenLabsKey) || SherpaConfigured();

    /// <summary>True if the in-process sherpa-onnx TTS is configured (a voice model directory).</summary>
    private static bool SherpaConfigured() =>
        !string.IsNullOrWhiteSpace(_sherpaModelDir) && Directory.Exists(_sherpaModelDir);

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    private static async Task<string> ReadBody(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }
}

public static class StunServerExtensions
{
    public static RTCIceServer ParseStunServer(this string stunServer)
    {
        var fields = stunServer.Split(';');

        return new RTCIceServer
        {
            urls = fields[0],
            username = fields.Length > 1 ? fields[1] : null,
            credential = fields.Length > 2 ? fields[2] : null,
            credentialType = RTCIceCredentialType.password
        };
    }
}
