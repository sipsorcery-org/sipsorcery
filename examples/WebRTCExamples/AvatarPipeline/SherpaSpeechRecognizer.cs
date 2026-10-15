//-----------------------------------------------------------------------------
// Filename: SherpaSpeechRecognizer.cs
//
// Description: Local, offline, IN-PROCESS speech-to-text using sherpa-onnx - one of
// the avatar's STT engines (see SpeechRecognizer for the shared streaming/segmentation
// front-end). Runs through the same sherpa-onnx/onnxruntime stack the TTS already uses,
// so there is no separate STT runtime to carry.
//
// Model families are AUTO-DETECTED from the files in the model folder:
//   * encoder + decoder + JOINER .onnx  -> NeMo transducer (e.g. Parakeet tdt - the
//     current top open English model on ASR leaderboards; recommended).
//   * encoder + decoder .onnx           -> Whisper ONNX export (identical weights to
//     the Whisper.net engine).
// Extract any model from https://github.com/k2-fsa/sherpa-onnx/releases/tag/asr-models
// under C:\tools\sherpa-stt\ - the first folder containing .onnx files is used;
// override with SHERPA_STT_DIR.
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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SherpaOnnx;

namespace demo;

public sealed class SherpaSpeechRecognizer : SpeechRecognizer
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<SherpaSpeechRecognizer>();

    // Recognisers are created per peer connection; the model is heavy and stateless across
    // utterances - share one engine per model directory for the app's life.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, OfflineRecognizer> _engines = new();

    private readonly string _modelDir;
    private OfflineRecognizer _recognizer;

    protected override string EngineName => "SherpaOnnx";

    /// <summary>The model folder: SHERPA_STT_DIR, else the first folder under
    /// C:\tools\sherpa-stt containing .onnx files (keep one model there, or set the env var).</summary>
    public static string DefaultModelDir
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SHERPA_STT_DIR");
            if (!string.IsNullOrWhiteSpace(env))
            {
                return env;
            }
            const string root = @"C:\tools\sherpa-stt";
            return Directory.Exists(root)
                ? Directory.EnumerateDirectories(root)
                      .OrderBy(d => d)
                      .FirstOrDefault(d => Directory.EnumerateFiles(d, "*.onnx").Any())
                : null;
        }
    }

    /// <summary>True when a sherpa STT model folder is on disk (used for engine selection).</summary>
    public static bool FilesPresent()
    {
        var dir = DefaultModelDir;
        return dir != null && Directory.Exists(dir) &&
               Directory.EnumerateFiles(dir, "*encoder*.onnx").Any();
    }

    public SherpaSpeechRecognizer(string modelDir = null)
    {
        _modelDir = string.IsNullOrWhiteSpace(modelDir) ? DefaultModelDir : modelDir;
    }

    protected override Task InitAsync()
    {
        _recognizer = _engines.GetOrAdd(Path.GetFullPath(_modelDir), CreateEngine);
        return Task.CompletedTask;
    }

    private static OfflineRecognizer CreateEngine(string modelDir)
    {
        // Prefer the int8 quantised files when present: ~4x smaller and faster on CPU for a
        // negligible accuracy cost; fall back to the fp32 files.
        string Pick(string kind) =>
            Directory.EnumerateFiles(modelDir, $"*{kind}.int8.onnx").FirstOrDefault()
            ?? Directory.EnumerateFiles(modelDir, $"*{kind}.onnx").FirstOrDefault();

        var encoder = Pick("encoder") ?? throw new FileNotFoundException($"No encoder .onnx found in {modelDir}.");
        var decoder = Pick("decoder") ?? throw new FileNotFoundException($"No decoder .onnx found in {modelDir}.");
        var joiner = Pick("joiner");

        var config = new OfflineRecognizerConfig();
        if (joiner != null)
        {
            // NeMo transducer (encoder/decoder/joiner), e.g. Parakeet tdt.
            config.ModelConfig.Transducer.Encoder = encoder;
            config.ModelConfig.Transducer.Decoder = decoder;
            config.ModelConfig.Transducer.Joiner = joiner;
            config.ModelConfig.ModelType = "nemo_transducer";
        }
        else
        {
            // Whisper ONNX export (encoder/decoder).
            config.ModelConfig.Whisper.Encoder = encoder;
            config.ModelConfig.Whisper.Decoder = decoder;
            config.ModelConfig.Whisper.Language = "en";
            config.ModelConfig.Whisper.Task = "transcribe";
        }
        config.ModelConfig.Tokens = Directory.EnumerateFiles(modelDir, "*tokens.txt").First();
        config.ModelConfig.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        // "cpu" (default), "directml" (any DX12 GPU on Windows - the deployed onnxruntime
        // build already supports it) or "cuda" (needs the CUDA build of sherpa-onnx).
        // sherpa falls back to CPU itself if the requested provider is unavailable.
        config.ModelConfig.Provider = Environment.GetEnvironmentVariable("SHERPA_STT_PROVIDER") ?? "cpu";

        var recognizer = new OfflineRecognizer(config);
        logger.LogInformation("sherpa-onnx STT loaded {Model} ({Family}).",
            Path.GetFileName(Path.TrimEndingDirectorySeparator(modelDir)),
            joiner != null ? "nemo transducer" : "whisper");
        return recognizer;
    }

    /// <summary>
    /// Loads the shared engine and decodes a short silence at app start, so the first
    /// utterance of the first call doesn't pay the model load + first-decode setup.
    /// </summary>
    public static Task PreloadAsync(string modelDir = null) => Task.Run(() =>
    {
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var dir = string.IsNullOrWhiteSpace(modelDir) ? DefaultModelDir : modelDir;
            var engine = _engines.GetOrAdd(Path.GetFullPath(dir), CreateEngine);
            lock (engine)
            {
                using var stream = engine.CreateStream();
                stream.AcceptWaveform(16000, new float[16000]);   // 1s of silence.
                engine.Decode(stream);
            }
            logger.LogInformation("sherpa-onnx STT warmed up in {Ms} ms.", sw.ElapsedMilliseconds);
        }
        catch (Exception excp)
        {
            logger.LogWarning(excp, "sherpa-onnx STT warm-up failed (first utterance will be slow).");
        }
    });

    protected override Task<string> TranscribeAsync(short[] pcm8k)
    {
        return Task.Run(() =>
        {
            var samples = To16kFloat(pcm8k);
            // The engine is shared app-wide; serialise decodes on the native instance.
            lock (_recognizer)
            {
                using var stream = _recognizer.CreateStream();
                stream.AcceptWaveform(16000, samples);
                _recognizer.Decode(stream);
                return stream.Result.Text;
            }
        });
    }

    /// <summary>Upsamples 8kHz 16-bit PCM to 16kHz normalised float mono (linear interpolation, factor 2).</summary>
    private static float[] To16kFloat(short[] pcm8k)
    {
        int n = pcm8k.Length;
        var outBuf = new float[n * 2];
        const float scale = 1f / 32768f;
        for (int i = 0; i < n; i++)
        {
            short cur = pcm8k[i];
            short next = (i + 1 < n) ? pcm8k[i + 1] : cur;
            outBuf[2 * i] = cur * scale;
            outBuf[2 * i + 1] = (cur + next) * 0.5f * scale;
        }
        return outBuf;
    }

    /// <summary>Exposes init + transcription for the --stt-test round trip (no VAD front-end).</summary>
    public async Task<string> TestTranscribeAsync(short[] pcm8k)
    {
        await InitAsync().ConfigureAwait(false);
        return await TranscribeAsync(pcm8k).ConfigureAwait(false);
    }

    /// <summary>The engine is shared for the app's lifetime (recognisers are per-connection).</summary>
    protected override void DisposeEngine() { }
}
