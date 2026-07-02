//-----------------------------------------------------------------------------
// Filename: PiperTtsSpeaker.cs
//
// Description: Local, offline text-to-speech using Piper (https://github.com/OHF-Voice/piper1-gpl)
// - one of the avatar's TTS engines (see LipSyncTtsSpeaker for the shared playback /
// lip-sync pipeline). It needs no cloud service, so the avatar can run in a Linux
// container / Kubernetes pod.
//
// Piper (the piper1-gpl rewrite) is a Python package. This engine supports two modes:
//
//  * HTTP server (recommended, set PIPER_HTTP_URL): Piper runs once as
//    `python -m piper.http_server` with the model already loaded; each utterance is a
//    POST {"text": ...} that returns a WAV. This avoids the per-call model reload and
//    is markedly faster.
//  * Child process (fallback): `python -m piper ... --output-raw -- <text>` is spawned
//    per utterance, reading raw PCM from stdout. Simple, but reloads the model every
//    time, so it is the slowest option.
//
// Either way the resulting 16-bit mono PCM (at the voice's sample rate) is handed to the
// base class, which resamples to 16kHz, plays it, and drives the mouth.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;

namespace demo;

public sealed class PiperTtsSpeaker : LipSyncTtsSpeaker
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<PiperTtsSpeaker>();

    private readonly string _httpUrl;
    private readonly string _piperPath;
    private readonly string _model;
    private readonly string _dataDir;
    private readonly int _modelSampleRate;

    protected override string EngineName => "Piper";

    /// <param name="httpUrl">Base URL of a running `piper.http_server` (e.g. http://localhost:5000). When
    /// set, synthesis goes over HTTP and the model loads once server-side; the process args below are unused.</param>
    /// <param name="piperPath">The Piper command for child-process mode. Either a `piper` console script, or a
    /// Python interpreter ("python"/"python3") in which case it is launched as `python -m piper`.</param>
    /// <param name="model">A voice name (resolved under <paramref name="dataDir"/>) or a full path to a .onnx voice.</param>
    /// <param name="dataDir">Optional directory holding downloaded voices (Piper's --data-dir).</param>
    public PiperTtsSpeaker(string httpUrl, string piperPath, string model, string dataDir,
        IAvatarRenderer renderer, AudioExtrasSource audio, int visemeLeadMs = 150)
        : base(renderer, audio, visemeLeadMs)
    {
        _httpUrl = string.IsNullOrWhiteSpace(httpUrl) ? null : httpUrl.TrimEnd('/');
        _piperPath = piperPath;
        _model = model;
        _dataDir = dataDir;
        // Only needed for child-process raw output; the HTTP server returns a WAV with its own header.
        _modelSampleRate = _httpUrl != null ? 0 : ReadModelSampleRate(model, dataDir);
    }

    protected override Task<(short[] samples, int sampleRate)> SynthesiseAsync(string text) =>
        _httpUrl != null ? SynthesiseHttpAsync(text) : SynthesiseProcessAsync(text);

    /// <summary>
    /// POSTs the text (JSON {"text": ...}) to a running `piper.http_server` and decodes the returned WAV.
    /// The model is already loaded server-side, so this avoids the per-utterance reload of process mode.
    /// PIPER_HTTP_URL must be the synthesis endpoint: the server root for piper-tts &lt;= 1.4.2
    /// (e.g. http://localhost:5000), or .../synthesize on newer builds that moved the route.
    /// </summary>
    private async Task<(short[] samples, int sampleRate)> SynthesiseHttpAsync(string text)
    {
        var json = JsonSerializer.Serialize(new { text });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync(_httpUrl, content).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Piper HTTP server returned {Status} for \"{Text}\". Check PIPER_HTTP_URL points at the synthesis endpoint (server root for piper-tts <= 1.4.2).", (int)response.StatusCode, text);
            return (Array.Empty<short>(), TargetRate);
        }

        var wav = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        return ParseWav(wav);
    }

    /// <summary>
    /// Runs Piper (OHF-Voice/piper1-gpl) as a child process and reads raw 16-bit mono PCM from its
    /// stdout. Invoked as:  [python -m] piper -m &lt;voice&gt; [--data-dir &lt;dir&gt;] --output-raw -- &lt;text&gt;.
    /// Text is passed as the documented positional argument rather than via stdin.
    /// </summary>
    private async Task<(short[] samples, int sampleRate)> SynthesiseProcessAsync(string text)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _piperPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // When PIPER_PATH points at a Python interpreter, launch the module form `python -m piper`
        // (the documented invocation); a `piper` console script is run directly.
        if (IsPython(_piperPath))
        {
            psi.ArgumentList.Add("-m");
            psi.ArgumentList.Add("piper");
        }

        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add(_model);
        if (!string.IsNullOrWhiteSpace(_dataDir))
        {
            psi.ArgumentList.Add("--data-dir");
            psi.ArgumentList.Add(_dataDir);
        }
        psi.ArgumentList.Add("--output-raw"); // stream raw PCM to stdout.
        psi.ArgumentList.Add("--");           // end of options; the text follows.
        psi.ArgumentList.Add(text);

        using var process = new Process { StartInfo = psi };
        var stderr = new StringBuilder();
        process.ErrorDataReceived += (s, e) => { if (e.Data != null) { stderr.AppendLine(e.Data); } };

        process.Start();
        process.BeginErrorReadLine();

        // Drain stdout continuously so a full pipe buffer can't stall Piper on a long utterance.
        using var pcmBuffer = new MemoryStream();
        await process.StandardOutput.BaseStream.CopyToAsync(pcmBuffer).ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            logger.LogError("Piper exited {Code}: {Error}", process.ExitCode, stderr.ToString().Trim());
            return (Array.Empty<short>(), _modelSampleRate);
        }

        var bytes = pcmBuffer.ToArray();
        var samples = new short[bytes.Length / sizeof(short)];
        Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * sizeof(short));
        return (samples, _modelSampleRate);
    }

    /// <summary>
    /// Minimal RIFF/WAVE parser for the 16-bit mono PCM Piper returns: reads the sample rate from the
    /// "fmt " chunk and the samples from the "data" chunk, skipping any other chunks.
    /// </summary>
    private static (short[] samples, int sampleRate) ParseWav(byte[] wav)
    {
        if (wav.Length < 44 ||
            wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F' ||
            wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
        {
            logger.LogError("Piper HTTP response was not a WAV file ({Bytes} bytes).", wav.Length);
            return (Array.Empty<short>(), TargetRate);
        }

        int sampleRate = TargetRate;
        int pos = 12; // past "RIFF"<size>"WAVE".
        while (pos + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, pos, 4);
            int size = BitConverter.ToInt32(wav, pos + 4);
            int body = pos + 8;

            if (id == "fmt " && body + 16 <= wav.Length)
            {
                sampleRate = BitConverter.ToInt32(wav, body + 4);
            }
            else if (id == "data")
            {
                int len = Math.Min(size, wav.Length - body);
                var samples = new short[len / sizeof(short)];
                Buffer.BlockCopy(wav, body, samples, 0, samples.Length * sizeof(short));
                return (samples, sampleRate);
            }

            pos = body + size + (size & 1); // chunks are word-aligned.
        }

        logger.LogError("Piper WAV had no data chunk ({Bytes} bytes).", wav.Length);
        return (Array.Empty<short>(), sampleRate);
    }

    /// <summary>True if the Piper command is a Python interpreter, so it must be run as `python -m piper`.</summary>
    private static bool IsPython(string exe)
    {
        var name = Path.GetFileNameWithoutExtension(exe)?.ToLowerInvariant();
        return name is "python" or "python3";
    }

    /// <summary>
    /// Reads the voice's sample rate from its Piper ".onnx.json" config (audio.sample_rate), looking
    /// beside the .onnx when <paramref name="model"/> is a path, or under <paramref name="dataDir"/>
    /// when it is a voice name. Falls back to 22050 (the most common Piper rate) if not found.
    /// </summary>
    private static int ReadModelSampleRate(string model, string dataDir)
    {
        const int fallback = 22050;
        try
        {
            var configPath = ResolveConfigPath(model, dataDir);
            if (configPath == null)
            {
                logger.LogWarning("Piper voice config for {Model} not found; assuming {Rate}Hz.", model, fallback);
                return fallback;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(configPath));
            if (doc.RootElement.TryGetProperty("audio", out var audio) &&
                audio.TryGetProperty("sample_rate", out var rate))
            {
                return rate.GetInt32();
            }
        }
        catch (Exception excp)
        {
            logger.LogWarning("Failed to read Piper sample rate, assuming {Rate}Hz: {Error}", fallback, excp.Message);
        }
        return fallback;
    }

    /// <summary>Locates the "{voice}.onnx.json" config for a model given either as a .onnx path or a voice name.</summary>
    private static string ResolveConfigPath(string model, string dataDir)
    {
        // download_voices writes "{name}.onnx" + "{name}.onnx.json"; a path-based model has the
        // config as a sibling "{model}.json", which collapses to the same "{name}.onnx.json".
        var byPath = model + ".json";
        if (File.Exists(byPath))
        {
            return byPath;
        }

        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            var byName = Path.Combine(dataDir, model + ".onnx.json");
            if (File.Exists(byName))
            {
                return byName;
            }
        }
        return null;
    }
}
