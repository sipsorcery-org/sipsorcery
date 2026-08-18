using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Serilog;
using Serilog.Extensions.Logging;
using SIPSorcery.Gemini.Realtime;
using SIPSorcery.Gemini.Realtime.Models;

namespace demo;

class Program
{
    private const int INPUT_SAMPLE_RATE = 16000;
    private const int OUTPUT_SAMPLE_RATE = 24000;

    static async Task Main()
    {
        // Windows' console defaults to a legacy codepage that can't represent Polish diacritics
        // (or the checkmark glyphs used in the transcript log lines below) — without this,
        // unmappable characters silently become "?", making transcripts unreadable.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Only warnings/errors go through Serilog — transcription lines are written directly to
        // the console below so they show up regardless of log level. Set back to Debug (and swap
        // Console.WriteLine for Log.Information below) if you need the full diagnostic trace.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .CreateLogger();

        var loggerFactory = new SerilogLoggerFactory(Log.Logger);

        Log.Logger.Information("Gemini Live Demo Program");

        var apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Log.Logger.Error("Please provide your Gemini API key as an environment variable. For example: set GEMINI_API_KEY=<your gemini api key>");
            return;
        }

        var geminiEndPoint = new GeminiLiveEndPoint(apiKey, loggerFactory);

        Log.Logger.Information("Available recording devices ({Count}):", WaveInEvent.DeviceCount);
        for (int i = 0; i < WaveInEvent.DeviceCount; i++)
        {
            Log.Logger.Information("  [{Index}] {ProductName}", i, WaveInEvent.GetCapabilities(i).ProductName);
        }

        var micDeviceNumber = 0;
        var micDeviceEnv = Environment.GetEnvironmentVariable("GEMINI_MIC_DEVICE_INDEX");
        if (!string.IsNullOrWhiteSpace(micDeviceEnv) && int.TryParse(micDeviceEnv, out var parsedDeviceNumber))
        {
            micDeviceNumber = parsedDeviceNumber;
        }
        Log.Logger.Information("Using recording device [{Index}] {ProductName}. Set GEMINI_MIC_DEVICE_INDEX to use a different one.",
            micDeviceNumber, WaveInEvent.GetCapabilities(micDeviceNumber).ProductName);

        var waveIn = new WaveInEvent
        {
            DeviceNumber = micDeviceNumber,
            WaveFormat = new WaveFormat(INPUT_SAMPLE_RATE, 16, 1)
        };

        long audioChunksCaptured = 0;
        long audioBytesCaptured = 0;
        waveIn.DataAvailable += (s, e) =>
        {
            audioChunksCaptured++;
            audioBytesCaptured += e.BytesRecorded;
            if (audioChunksCaptured == 1)
            {
                Log.Logger.Debug("First microphone audio chunk captured ({Bytes} bytes) — capture is working.", e.BytesRecorded);
            }
            else if (audioChunksCaptured % 100 == 0)
            {
                Log.Logger.Debug("Captured {Chunks} audio chunks so far ({Bytes} bytes total).", audioChunksCaptured, audioBytesCaptured);
            }

            if (e.BytesRecorded == e.Buffer.Length)
            {
                geminiEndPoint.SendAudio(e.Buffer);
            }
            else
            {
                geminiEndPoint.SendAudio(e.Buffer.Take(e.BytesRecorded).ToArray());
            }
        };
        waveIn.RecordingStopped += (s, e) =>
        {
            if (e.Exception != null)
            {
                Log.Logger.Error(e.Exception, "Microphone recording stopped unexpectedly.");
            }
        };

        // BufferDuration is how much audio the provider can hold; DesiredLatency/NumberOfBuffers
        // control how much NAudio buffers internally before/while playing. Gemini's audio parts
        // don't arrive at a perfectly steady real-time pace (network jitter, batched chunks), so
        // both need enough slack to absorb that without underrunning mid-sentence.
        var playbackBuffer = new BufferedWaveProvider(new WaveFormat(OUTPUT_SAMPLE_RATE, 16, 1))
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(10)
        };
        var waveOut = new WaveOutEvent
        {
            DesiredLatency = 500,
            NumberOfBuffers = 4
        };
        waveOut.Init(playbackBuffer);

        // Don't start playback the instant the buffer has ANY data — prime it with a small
        // cushion first so the very first sentence doesn't stutter while Gemini is still catching
        // up to real-time. Once started, playback just keeps running (NAudio pads brief gaps with
        // silence rather than stopping).
        const int PLAYBACK_PRIMING_MS = 400;
        var playbackPrimingBytes = OUTPUT_SAMPLE_RATE * 2 /* 16-bit */ * PLAYBACK_PRIMING_MS / 1000;
        var playbackStarted = false;

        geminiEndPoint.OnAudioReceived += (pcm, sampleRateHz) =>
        {
            if (sampleRateHz != OUTPUT_SAMPLE_RATE)
            {
                Log.Logger.Warning("Received Gemini audio at unexpected sample rate {SampleRate}Hz, expected {ExpectedSampleRate}Hz.", sampleRateHz, OUTPUT_SAMPLE_RATE);
            }
            playbackBuffer.AddSamples(pcm, 0, pcm.Length);

            if (!playbackStarted && playbackBuffer.BufferedBytes >= playbackPrimingBytes)
            {
                playbackStarted = true;
                waveOut.Play();
            }
        };

        geminiEndPoint.OnInterrupted += () =>
        {
            Log.Logger.Debug("Model turn interrupted by user, clearing playback buffer.");
            playbackBuffer.ClearBuffer();
        };

        geminiEndPoint.OnServerMessage += message =>
        {
            // Debug-level trace of every message so it's obvious whether Gemini is reacting to
            // the audio being sent at all, even before/without a recognised transcript line.
            Log.Logger.Debug("Gemini Live server message: {Kind} {Json}", message.Kind, message.ToJson());

            var log = message switch
            {
                GeminiServerEventContent { InputTranscription.Text: { Length: > 0 } inputText } => $"ME ✅: {inputText.Trim()}",
                GeminiServerEventContent { OutputTranscription.Text: { Length: > 0 } outputText } => $"AI ✅: {outputText.Trim()}",
                GeminiServerEventGoAway goAway => $"Gemini is closing the connection soon, time left: {goAway.TimeLeft}",
                _ => string.Empty
            };

            if (log != string.Empty)
            {
                Console.WriteLine(log);
            }
        };

        geminiEndPoint.OnConnected += () =>
        {
            Log.Logger.Information("Gemini Live session established.");
            waveIn.StartRecording();
            // waveOut.Play() is deferred to the priming check in OnAudioReceived above.

            // Prompt the model to speak first rather than waiting for the user to say something.
            _ = SendGreetingPromptAsync(geminiEndPoint);
        };

        geminiEndPoint.OnFailed += () => Log.Logger.Error("Gemini Live connection failed.");
        geminiEndPoint.OnClosed += () =>
        {
            Log.Logger.Information("Gemini Live connection closed.");
            waveIn.StopRecording();
            waveOut.Stop();
        };

        var setup = new GeminiSetup
        {
            GenerationConfig = new GeminiGenerationConfig
            {
                ResponseModalities = new List<GeminiResponseModalityEnum> { GeminiResponseModalityEnum.AUDIO },
                SpeechConfig = new GeminiSpeechConfig
                {
                    VoiceConfig = new GeminiVoiceConfig
                    {
                        PrebuiltVoiceConfig = new GeminiPrebuiltVoiceConfig { VoiceName = GeminiVoiceEnum.Puck }
                    }
                }
            },
            SystemInstruction = new GeminiContent
            {
                Parts = new List<GeminiPart>
                {
                    new GeminiPart
                    {
                        Text = "Jesteś asystentem laboratoriumpanidomu.pl, twoim zadaniem jest odpowiadanie " +
                               "tylko i wyłącznie na temat produktów naszej firmy. Masz na imię Labek i jesteś " +
                               "przyjaźnie nastawionym, ciepłym człowiekiem, ale przy tym wyrażasz się zwięźle i jasno."
                    }
                }
            },
            InputAudioTranscription = new GeminiAudioTranscriptionConfig(),
            OutputAudioTranscription = new GeminiAudioTranscriptionConfig()
        };

        var connectResult = await geminiEndPoint.StartConnect(GeminiLiveModelsEnum.Gemini25FlashNativeAudioLatest, setup);

        if (connectResult.IsLeft)
        {
            Log.Logger.Error($"Failed to connect to Gemini Live end point: {connectResult.LeftAsEnumerable().First()}");
            return;
        }

        Console.WriteLine("Wait for ctrl-c to indicate user exit.");

        var exitTcs = new TaskCompletionSource<object?>();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            exitTcs.TrySetResult(null);
        };

        await exitTcs.Task;

        waveIn.StopRecording();
        waveOut.Stop();
        await geminiEndPoint.Close();
    }

    /// <summary>
    /// Prompts Gemini to speak first (rather than waiting for the user to say something) by
    /// sending a short instruction as a completed user turn.
    /// </summary>
    private static async Task SendGreetingPromptAsync(GeminiLiveEndPoint geminiEndPoint)
    {
        var result = await geminiEndPoint.SendText("Przywitaj się krótko, przedstaw się jako Labek i zapytaj, jak możesz pomóc.");

        if (result.IsLeft)
        {
            Log.Logger.Warning("Failed to send greeting prompt: {Error}", result.LeftAsEnumerable().First());
        }
    }
}
