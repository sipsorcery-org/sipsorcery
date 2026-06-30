//-----------------------------------------------------------------------------
// Filename: WhisperSpeechRecognizer.cs
//
// Description: Local, offline speech-to-text using Whisper.net (a managed wrapper
// over whisper.cpp) - one of the avatar's STT engines (see SpeechRecognizer for the
// shared streaming/segmentation front-end). Each completed utterance (8kHz 16-bit
// mono PCM) is resampled to the 16kHz mono float Whisper expects and transcribed in
// one shot.
//
// Whisper.net.Runtime ships native whisper.cpp binaries for win-x64, linux-x64 and
// osx, so this runs unchanged in a Linux container / Kubernetes pod (CPU only; no
// GPU or cloud dependency). The ggml model file is resolved from WHISPER_MODEL or,
// if absent, downloaded once to the app directory.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Whisper.net;

namespace demo;

public sealed class WhisperSpeechRecognizer : SpeechRecognizer
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<WhisperSpeechRecognizer>();

    // Default model: base English. Good accuracy and fast enough for near-real-time on CPU.
    // Override the file via WHISPER_MODEL or the download source via WHISPER_MODEL_URL.
    private const string DefaultModelFile = "ggml-base.en.bin";
    private const string DefaultModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin";

    private readonly string _modelPath;
    private readonly string _modelUrl;
    private readonly string _language;

    private WhisperFactory _factory;
    private WhisperProcessor _processor;

    protected override string EngineName => "Whisper";

    public WhisperSpeechRecognizer(string modelPath = null, string language = "en")
    {
        _modelPath = string.IsNullOrWhiteSpace(modelPath) ? DefaultModelFile : modelPath;
        _modelUrl = Environment.GetEnvironmentVariable("WHISPER_MODEL_URL") ?? DefaultModelUrl;
        _language = string.IsNullOrWhiteSpace(language) ? "en" : language;
    }

    protected override async Task InitAsync()
    {
        await EnsureModelAsync().ConfigureAwait(false);

        _factory = WhisperFactory.FromPath(_modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage(_language)
            .Build();

        logger.LogInformation("Whisper model {Model} loaded (language {Lang}).", Path.GetFileName(_modelPath), _language);
    }

    protected override async Task<string> TranscribeAsync(short[] pcm8k)
    {
        var samples = To16kFloat(pcm8k);

        var sb = new StringBuilder();
        await foreach (var segment in _processor.ProcessAsync(samples, ShutdownToken).ConfigureAwait(false))
        {
            sb.Append(segment.Text);
        }
        return sb.ToString();
    }

    /// <summary>Upsamples 8kHz 16-bit PCM to the 16kHz normalised float mono Whisper requires (linear interpolation, factor 2).</summary>
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

    /// <summary>Resolves the ggml model file, downloading it once if it isn't already present.</summary>
    private async Task EnsureModelAsync()
    {
        if (File.Exists(_modelPath))
        {
            return;
        }

        logger.LogInformation("Whisper model {Path} not found; downloading from {Url} (one-off).", _modelPath, _modelUrl);

        var dir = Path.GetDirectoryName(Path.GetFullPath(_modelPath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        await using var src = await http.GetStreamAsync(_modelUrl).ConfigureAwait(false);
        var tmp = _modelPath + ".download";
        await using (var dst = File.Create(tmp))
        {
            await src.CopyToAsync(dst).ConfigureAwait(false);
        }
        File.Move(tmp, _modelPath, overwrite: true);

        logger.LogInformation("Whisper model downloaded to {Path}.", _modelPath);
    }

    protected override void DisposeEngine()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
