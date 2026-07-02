//-----------------------------------------------------------------------------
// Filename: SherpaTtsSpeaker.cs
//
// Description: Local, offline, IN-PROCESS text-to-speech using sherpa-onnx
// (https://github.com/k2-fsa/sherpa-onnx) - one of the avatar's TTS engines (see
// LipSyncTtsSpeaker for the shared playback / lip-sync pipeline).
//
// sherpa-onnx runs the same Piper VITS voices as the PiperTtsSpeaker, but as a native
// library inside this process (the NuGet carries the binaries, including the espeak-ng
// phonemizer that made Piper need Python). Compared to the Piper engine there is no
// child process, no HTTP server and no venv to orchestrate - just a model directory.
//
// Voice models: any "vits-piper-*" archive from
// https://github.com/k2-fsa/sherpa-onnx/releases/tag/tts-models extracted to a folder
// containing the voice .onnx, tokens.txt and espeak-ng-data/. Point SHERPA_MODEL_DIR
// at that folder.
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
using SIPSorcery.Media;

namespace demo;

public sealed class SherpaTtsSpeaker : LipSyncTtsSpeaker, IDisposable
{
    private static readonly ILogger logger = SIPSorcery.LogFactory.CreateLogger<SherpaTtsSpeaker>();

    private readonly OfflineTts _tts;
    private readonly float _speed;

    protected override string EngineName => "SherpaOnnx";

    /// <param name="modelDir">Folder holding a Piper VITS voice: the .onnx model, tokens.txt
    /// and the espeak-ng-data directory (an extracted sherpa-onnx vits-piper-* archive).</param>
    public SherpaTtsSpeaker(string modelDir, IAvatarRenderer renderer, AudioExtrasSource audio,
        int visemeLeadMs = 150, float speed = 1.0f)
        : base(renderer, audio, visemeLeadMs)
    {
        var modelPath = Directory.EnumerateFiles(modelDir, "*.onnx").FirstOrDefault()
            ?? throw new FileNotFoundException($"No .onnx voice model found in {modelDir}.");
        var tokensPath = Path.Combine(modelDir, "tokens.txt");
        var dataDir = Path.Combine(modelDir, "espeak-ng-data");

        var config = new OfflineTtsConfig();
        config.Model.Vits.Model = modelPath;
        config.Model.Vits.Tokens = tokensPath;
        config.Model.Vits.DataDir = dataDir;
        config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        config.Model.Provider = "cpu";   // VITS synthesis is comfortably real-time on CPU.

        _tts = new OfflineTts(config);
        _speed = speed;

        logger.LogInformation("sherpa-onnx TTS loaded {Model} ({Rate}Hz, {Speakers} speaker(s)).",
            Path.GetFileName(modelPath), _tts.SampleRate, _tts.NumSpeakers);
    }

    /// <summary>Synthesises in-process; sherpa-onnx returns float samples in [-1,1].</summary>
    protected override Task<(short[] samples, int sampleRate)> SynthesiseAsync(string text)
    {
        return Task.Run(() =>
        {
            var audio = _tts.Generate(text, _speed, speakerId: 0);
            var floats = audio.Samples;
            var samples = new short[floats.Length];
            for (int i = 0; i < floats.Length; i++)
            {
                samples[i] = (short)Math.Clamp(floats[i] * 32767.0f, short.MinValue, short.MaxValue);
            }
            return (samples, audio.SampleRate);
        });
    }

    /// <summary>Exposes synthesis for the --tts-test smoke check (no playback pipeline).</summary>
    public Task<(short[] samples, int sampleRate)> TestSynthesiseAsync(string text) => SynthesiseAsync(text);

    public void Dispose() => _tts?.Dispose();
}
