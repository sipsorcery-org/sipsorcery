//-----------------------------------------------------------------------------
// Filename: OpenAiChatCommand.cs
//
// Description: The "sipsorcery openai chat" verb. An interactive voice session
// with the OpenAI Realtime API (the session counterpart to the "openai realtime"
// probe). It connects, plays the model's voice through ffplay, and - with
// --play - - reads microphone PCM from stdin (pipe an ffmpeg capture in),
// encodes it to OPUS and sends it. It runs until ctrl-c.
//
// Echo handling: there is no acoustic echo canceller, so to stop the model
// hearing itself through the speakers the microphone is GATED while the model is
// speaking - silence is sent instead of the mic for as long as audio frames are
// arriving (plus a short hangover for the ffplay buffer to drain). This is
// half-duplex: it trades barge-in for reliable, echo-free operation without a
// headset. Use a headset for full-duplex.
//
//   ffmpeg -f dshow -i audio="<mic>" -ac 1 -ar 48000 -f s16le - | sipsorcery openai chat --play -
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 13 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.OpenAI.Realtime;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class OpenAiChatCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 20;
    private const RealtimeVoicesEnum DEFAULT_VOICE = RealtimeVoicesEnum.marin;

    // OPUS for WebRTC is a 48 kHz, mono (encoded in-band) stream; a 20 ms frame is 960 samples.
    private const int SAMPLE_RATE = 48000;
    private const int FRAME_SAMPLES = 960;
    private const int FRAME_BYTES = FRAME_SAMPLES * 2;

    // The mic stays gated for this long after the last model audio frame, so the tail of the
    // playback (still in the ffplay buffer) is not captured back.
    private const int GATE_HANGOVER_MS = 400;

    public OpenAiChatCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var apiKeyOption = new Option<string?>("--api-key")
        {
            Description = "The OpenAI API key. Defaults to the OPENAI_API_KEY environment variable."
        };

        var voiceOption = new Option<RealtimeVoicesEnum>("--voice")
        {
            Description = "The voice the model should use.",
            DefaultValueFactory = _ => DEFAULT_VOICE
        };

        var playOption = new Option<string?>("--play")
        {
            Description = "Microphone input: \"-\" reads raw s16le 48kHz mono PCM from stdin (pipe an ffmpeg mic capture in). " +
                          "Omit for listen-only (you hear the model but cannot talk back)."
        };

        var instructionsOption = new Option<string>("--instructions")
        {
            Description = "The system instructions for the model.",
            DefaultValueFactory = _ => "You are a helpful voice assistant. Keep your replies concise and conversational."
        };

        var command = new Command("chat", "Interactive voice chat with the OpenAI Realtime API (mic via --play -, speaker via ffplay). Runs until ctrl-c.");
        command.Options.Add(apiKeyOption);
        command.Options.Add(voiceOption);
        command.Options.Add(playOption);
        command.Options.Add(instructionsOption);
        command.Options.Add(TimeoutOption);
        command.Options.Add(VerboseOption);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(apiKeyOption),
            parseResult.GetValue(voiceOption),
            parseResult.GetValue(playOption),
            parseResult.GetValue(instructionsOption)!,
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string? apiKey, RealtimeVoicesEnum voice, string? play, string instructions,
        int timeoutSeconds, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(OpenAiChatCommand));

        apiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.Error.WriteLine("An OpenAI API key is required (--api-key or the OPENAI_API_KEY environment variable).");
            return ExitCodes.InvalidArgument;
        }

        bool useMic;
        if (string.IsNullOrEmpty(play)) { useMic = false; }
        else if (play == "-") { useMic = true; }
        else
        {
            Console.Error.WriteLine($"--play only accepts \"-\" (microphone PCM from stdin). Got \"{play}\".");
            return ExitCodes.InvalidArgument;
        }

        var session = new ChatSession(logger, loggerFactory, voice, instructions, useMic);
        return await session.RunAsync(apiKey, timeoutSeconds, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Holds the live session: the OpenAI endpoint, the ffplay output sink, the OPUS codecs and the
    /// microphone gate state.
    /// </summary>
    private sealed class ChatSession
    {
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly RealtimeVoicesEnum _voice;
        private readonly string _instructions;
        private readonly bool _useMic;

        // The encode (mic) and decode (model) OPUS codecs are independent objects, kept separate so
        // the mic loop and the receive callback never touch shared codec state.
        private readonly AudioEncoder _micEncoder = new(AudioCommonlyUsedFormats.OpusWebRTC);
        private readonly AudioEncoder _modelDecoder = new(AudioCommonlyUsedFormats.OpusWebRTC);

        // Ticks of the last received model audio frame; the mic is gated while this is recent.
        private long _lastModelAudioTicks;

        public ChatSession(ILogger logger, ILoggerFactory loggerFactory, RealtimeVoicesEnum voice, string instructions, bool useMic)
        {
            _logger = logger;
            _loggerFactory = loggerFactory;
            _voice = voice;
            _instructions = instructions;
            _useMic = useMic;
        }

        public async Task<int> RunAsync(string apiKey, int timeoutSeconds, CancellationToken ct)
        {
            using var endpoint = new WebRTCEndPoint(apiKey, _loggerFactory);
            using var speaker = AudioSink.Create("play", _logger, out string? sinkError);

            if (sinkError != null)
            {
                Console.Error.WriteLine($"Could not start the audio output: {sinkError}");
                return ExitCodes.InvalidArgument;
            }

            var connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            endpoint.OnAudioFrameReceived += frame =>
            {
                _lastModelAudioTicks = DateTime.UtcNow.Ticks;   // marks the model as speaking, gating the mic.
                try
                {
                    var pcm = _modelDecoder.DecodeAudio(frame.EncodedAudio, AudioCommonlyUsedFormats.OpusWebRTC);
                    speaker.Write(pcm, SAMPLE_RATE);
                }
                catch (Exception excp)
                {
                    _logger.LogWarning("Failed to decode/play a model audio frame: {Error}", excp.Message);
                }
            };

            endpoint.OnPeerConnectionFailed += () => failed.TrySetResult(true);
            endpoint.OnPeerConnectionClosed += () => failed.TrySetResult(true);

            endpoint.OnDataChannelMessage += (dc, message) =>
            {
                switch (message)
                {
                    case RealtimeServerEventConversationItemInputAudioTranscriptionCompleted you when !string.IsNullOrWhiteSpace(you.Transcript):
                        Console.WriteLine($"you ▶ {you.Transcript!.Trim()}");
                        break;
                    case RealtimeServerEventResponseAudioTranscriptDone ai when !string.IsNullOrWhiteSpace(ai.Transcript):
                        Console.WriteLine($"ai  ◀ {ai.Transcript!.Trim()}");
                        break;
                }
            };

            endpoint.OnPeerConnectionConnected += () =>
            {
                _logger.LogDebug("Connected to OpenAI; sending session update.");

                // Whisper transcription of the input is requested so the user's turns can be shown.
                endpoint.DataChannelMessenger.SendSessionUpdate(_voice, _instructions, transcriptionModel: TranscriptionModelEnum.Whisper1);

                // Have the model greet first, which confirms the audio output path before the user speaks.
                endpoint.DataChannelMessenger.SendResponseCreate(_voice, "Briefly greet the user and invite them to speak.");

                if (_useMic)
                {
                    _ = Task.Run(() => RunMicLoop(endpoint, sessionCts.Token));
                }

                connected.TrySetResult(true);
            };

            var connectResult = await endpoint.StartConnect().ConfigureAwait(false);
            if (connectResult.IsLeft)
            {
                Console.Error.WriteLine($"Failed to negotiate the connection to OpenAI: {connectResult.LeftAsEnumerable().First().Message}");
                return ExitCodes.TransportError;
            }

            var completed = await Task.WhenAny(connected.Task, failed.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);
            if (completed != connected.Task)
            {
                Console.Error.WriteLine(completed == failed.Task
                    ? "The peer connection failed before the session was established."
                    : $"The connection did not establish within {timeoutSeconds}s.");
                return completed == failed.Task ? ExitCodes.TransportError : ExitCodes.Timeout;
            }

            Console.Error.WriteLine(_useMic
                ? "Connected. Start talking — the mic is muted while the assistant speaks (half-duplex). Ctrl-C to quit."
                : "Connected (listen-only; pass --play - with a piped mic to talk back). Ctrl-C to quit.");

            // Run until ctrl-c, the connection drops, or (mic mode) the input pipe ends.
            var exit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; exit.TrySetResult(true); };
            Console.CancelKeyPress += onCancel;

            try
            {
                await Task.WhenAny(exit.Task, failed.Task, Task.Delay(Timeout.Infinite, ct)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            finally
            {
                Console.CancelKeyPress -= onCancel;
                sessionCts.Cancel();
                endpoint.Close();
            }

            Console.Error.WriteLine("Session ended.");
            return ExitCodes.Ok;
        }

        /// <summary>
        /// Reads 20 ms mic frames from stdin at the real-time rate the capture produces them, encodes
        /// each to OPUS and sends it. While the model is speaking the mic is replaced with silence so
        /// the assistant does not hear itself; the OPUS stream stays continuous either way (so the RTP
        /// timestamps keep advancing and the server VAD sees clean silence rather than a gap).
        /// </summary>
        private void RunMicLoop(WebRTCEndPoint endpoint, CancellationToken ct)
        {
            var stdin = Console.OpenStandardInput();
            var frameBytes = new byte[FRAME_BYTES];
            var pcm = new short[FRAME_SAMPLES];
            var silence = new short[FRAME_SAMPLES];
            long hangoverTicks = TimeSpan.FromMilliseconds(GATE_HANGOVER_MS).Ticks;

            try
            {
                while (!ct.IsCancellationRequested && ReadFully(stdin, frameBytes))
                {
                    bool gated = (DateTime.UtcNow.Ticks - _lastModelAudioTicks) < hangoverTicks;

                    short[] samples;
                    if (gated)
                    {
                        samples = silence;
                    }
                    else
                    {
                        for (int i = 0; i < FRAME_SAMPLES; i++)
                        {
                            pcm[i] = (short)(frameBytes[2 * i] | (frameBytes[2 * i + 1] << 8));
                        }
                        samples = pcm;
                    }

                    endpoint.SendAudio(FRAME_SAMPLES, _micEncoder.EncodeAudio(samples, AudioCommonlyUsedFormats.OpusWebRTC));
                }
            }
            catch (Exception excp)
            {
                _logger.LogDebug("Mic loop ended: {Error}", excp.Message);
            }
        }

        private static bool ReadFully(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                {
                    return false;
                }
                read += n;
            }
            return true;
        }
    }
}
