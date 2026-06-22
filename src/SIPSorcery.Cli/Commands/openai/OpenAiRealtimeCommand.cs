//-----------------------------------------------------------------------------
// Filename: OpenAiRealtimeCommand.cs
//
// Description: The "sipsorcery openai realtime" verb. A connectivity test for the
// OpenAI Realtime WebRTC API, based on the OpenAIExamples/GetStarted demo. It
// negotiates a WebRTC peer connection to OpenAI, opens the events data channel,
// asks the model to say a few words, and succeeds once the model's voice (an
// OPUS audio frame) is received back. This exercises the whole path: API key,
// HTTP SDP exchange, ICE, DTLS, SRTP, the data channel and the model producing
// audio.
//
// No microphone is used: the verb does not send audio, it triggers the model with
// a data channel "response.create" and listens for the returned audio frames. The
// received audio can optionally be played or captured with --audio (play, a .wav
// file or raw PCM on stdout). https://platform.openai.com/docs/guides/realtime-webrtc
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
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.OpenAI.Realtime;
using SIPSorcery.OpenAI.Realtime.Models;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Cli.Commands;

public sealed class OpenAiRealtimeCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 20;
    private const RealtimeVoicesEnum DEFAULT_VOICE = RealtimeVoicesEnum.marin;

    // OPUS for WebRTC is a 48 kHz mono stream; the decoded model audio is rendered/recorded at this rate.
    private const int SAMPLE_RATE = 48000;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record RealtimeResult(
        bool Success,
        bool Connected,
        long? ConnectTimeMs,
        bool VoiceDetected,
        int AudioFrames,
        long? FirstAudioMs,
        string? Transcript,
        string? Error);

    public OpenAiRealtimeCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var apiKeyOption = new Option<string?>("--api-key")
        {
            Description = "The OpenAI API key. Defaults to the OPENAI_API_KEY environment variable."
        };

        var promptOption = new Option<string>("--prompt")
        {
            Description = "The instruction sent to the model to make it speak.",
            DefaultValueFactory = _ => "Say a short hello."
        };

        var voiceOption = new Option<RealtimeVoicesEnum>("--voice")
        {
            Description = "The voice the model should use.",
            DefaultValueFactory = _ => DEFAULT_VOICE
        };

        var audioOption = new Option<string?>("--audio")
        {
            Description = "Where to send the model's received audio: \"play\" to hear it in an ffplay window, a .wav " +
                          "file path to record it, or \"-\" for raw s16le 48kHz mono PCM on stdout (the result then " +
                          "moves to stderr). Omit to only detect that voice arrived. ffplay is part of ffmpeg."
        };

        var command = new Command("realtime", "Connectivity test for the OpenAI Realtime WebRTC API; succeeds when the model's voice is received.");
        command.Options.Add(apiKeyOption);
        command.Options.Add(promptOption);
        command.Options.Add(voiceOption);
        command.Options.Add(audioOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(apiKeyOption),
            parseResult.GetValue(promptOption)!,
            parseResult.GetValue(voiceOption),
            parseResult.GetValue(audioOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string? apiKey, string prompt, RealtimeVoicesEnum voice, string? audioOut,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(OpenAiRealtimeCommand));

        apiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return WriteResult(asJson, false,
                new RealtimeResult(false, false, null, false, 0, null, null,
                    "An OpenAI API key is required (--api-key or the OPENAI_API_KEY environment variable)."),
                ExitCodes.InvalidArgument);
        }

        // Optional audio sink for the received model voice ("play", a .wav file or "-"). When active the
        // OPUS frames are decoded to PCM and written to it; otherwise only the frame count matters.
        using var audioSink = AudioSink.Create(audioOut, logger, out string? sinkError);
        if (sinkError != null)
        {
            return WriteResult(asJson, false,
                new RealtimeResult(false, false, null, false, 0, null, null, sinkError),
                ExitCodes.InvalidArgument);
        }
        var decoder = audioSink.IsActive ? new AudioEncoder(AudioCommonlyUsedFormats.OpusWebRTC) : null;
        bool stdoutClaimed = audioSink.IsStdout;

        using var endpoint = new WebRTCEndPoint(apiKey, loggerFactory);

        var stopwatch = Stopwatch.StartNew();
        long? connectTimeMs = null;
        long? firstAudioMs = null;
        long lastAudioTicks = 0;
        int audioFrames = 0;
        string? transcript = null;

        var voiceDetected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionFailed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        endpoint.OnAudioFrameReceived += frame =>
        {
            lastAudioTicks = DateTime.UtcNow.Ticks;

            if (Interlocked.Increment(ref audioFrames) == 1)
            {
                firstAudioMs = stopwatch.ElapsedMilliseconds;
                logger.LogDebug("First audio frame received from OpenAI at {FirstAudioMs}ms; voice detected.", firstAudioMs);
                voiceDetected.TrySetResult(true);
            }

            if (decoder != null)
            {
                try
                {
                    var pcm = decoder.DecodeAudio(frame.EncodedAudio, AudioCommonlyUsedFormats.OpusWebRTC);
                    audioSink.Write(pcm, SAMPLE_RATE);
                }
                catch (Exception excp)
                {
                    logger.LogWarning("Failed to decode/play a model audio frame: {Error}", excp.Message);
                }
            }
        };

        endpoint.OnPeerConnectionFailed += () => connectionFailed.TrySetResult(true);

        endpoint.OnDataChannelMessage += (dc, message) =>
        {
            // Capture the model's spoken transcript for the report when it completes.
            if (message is RealtimeServerEventResponseAudioTranscriptDone done && !string.IsNullOrWhiteSpace(done.Transcript))
            {
                transcript = done.Transcript!.Trim();
            }
        };

        endpoint.OnPeerConnectionConnected += () =>
        {
            connectTimeMs = stopwatch.ElapsedMilliseconds;
            logger.LogDebug("Peer connection and data channel established in {ConnectTimeMs}ms; asking the model to speak.", connectTimeMs);

            // Set the voice/instructions then trigger the model to produce a spoken response.
            endpoint.DataChannelMessenger.SendSessionUpdate(voice, "Keep it short.", transcriptionModel: TranscriptionModelEnum.Whisper1);
            var responseResult = endpoint.DataChannelMessenger.SendResponseCreate(voice, prompt);
            if (responseResult.IsLeft)
            {
                logger.LogWarning("Failed to send the response create message: {Error}", responseResult.LeftAsEnumerable().First().Message);
            }
        };

        try
        {
            var connectResult = await endpoint.StartConnect().ConfigureAwait(false);

            if (connectResult.IsLeft)
            {
                return WriteResult(asJson, stdoutClaimed,
                    new RealtimeResult(false, false, null, false, 0, null, null,
                        $"Failed to negotiate the connection to OpenAI: {connectResult.LeftAsEnumerable().First().Message}"),
                    ExitCodes.TransportError);
            }

            // Wait for the model's voice, a connection failure, or the timeout.
            var completed = await Task.WhenAny(
                voiceDetected.Task,
                connectionFailed.Task,
                Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), ct)).ConfigureAwait(false);

            if (completed == connectionFailed.Task)
            {
                return WriteResult(asJson, stdoutClaimed,
                    new RealtimeResult(false, connectTimeMs != null, connectTimeMs, false, audioFrames, firstAudioMs, transcript,
                        "The peer connection failed before any audio was received."),
                    ExitCodes.TransportError);
            }

            if (completed != voiceDetected.Task)
            {
                bool connected = connectTimeMs != null;
                return WriteResult(asJson, stdoutClaimed,
                    new RealtimeResult(false, connected, connectTimeMs, false, audioFrames, firstAudioMs, transcript,
                        ct.IsCancellationRequested ? "Cancelled." :
                        connected
                            ? $"Connected but no audio was received from the model within {timeoutSeconds}s."
                            : $"The connection did not establish within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            // Voice detected. When a sink is active, let the model finish speaking so the whole reply is
            // heard/recorded (wait until the audio has been idle briefly, capped by the timeout); without
            // a sink a short fixed grace is enough to report a representative frame count and transcript.
            try
            {
                if (audioSink.IsActive)
                {
                    var drain = Stopwatch.StartNew();
                    long idleTicks = TimeSpan.FromMilliseconds(800).Ticks;
                    while (!ct.IsCancellationRequested &&
                           drain.Elapsed < TimeSpan.FromSeconds(timeoutSeconds) &&
                           (DateTime.UtcNow.Ticks - lastAudioTicks) < idleTicks)
                    {
                        await Task.Delay(100, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }

            return WriteResult(asJson, stdoutClaimed,
                new RealtimeResult(true, true, connectTimeMs, true, audioFrames, firstAudioMs, transcript, null),
                ExitCodes.Ok);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson, stdoutClaimed,
                new RealtimeResult(false, connectTimeMs != null, connectTimeMs, false, audioFrames, firstAudioMs, transcript, "Cancelled."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson, stdoutClaimed,
                new RealtimeResult(false, connectTimeMs != null, connectTimeMs, false, audioFrames, firstAudioMs, transcript, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            endpoint.Close();
        }
    }

    private static int WriteResult(bool asJson, bool stdoutClaimed, RealtimeResult result, int exitCode)
    {
        // When the audio bitstream has claimed stdout (--audio -), the result is commentary and moves
        // to stderr so stdout carries exactly one payload.
        var output = stdoutClaimed ? Console.Error : Console.Out;

        if (asJson)
        {
            output.WriteLine(SerializeResult(result));
        }
        else if (result.Success)
        {
            string said = result.Transcript != null ? $" Model said: \"{result.Transcript}\"." : string.Empty;
            output.WriteLine($"OpenAI Realtime OK: connected in {result.ConnectTimeMs}ms, voice detected after {result.FirstAudioMs}ms " +
                $"({result.AudioFrames} audio frames).{said}");
        }
        else
        {
            Console.Error.WriteLine($"OpenAI Realtime check failed: {result.Error}");
        }

        return exitCode;
    }
}
