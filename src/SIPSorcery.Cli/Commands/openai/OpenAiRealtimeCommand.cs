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
// No audio device is used: the verb does not send microphone audio, it triggers
// the model with a data channel "response.create" and listens for the returned
// audio frames. https://platform.openai.com/docs/guides/realtime-webrtc
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
using SIPSorcery.OpenAI.Realtime;
using SIPSorcery.OpenAI.Realtime.Models;

namespace SIPSorcery.Cli.Commands;

public sealed class OpenAiRealtimeCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 20;
    private const RealtimeVoicesEnum DEFAULT_VOICE = RealtimeVoicesEnum.marin;

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

        var command = new Command("realtime", "Connectivity test for the OpenAI Realtime WebRTC API; succeeds when the model's voice is received.");
        command.Options.Add(apiKeyOption);
        command.Options.Add(promptOption);
        command.Options.Add(voiceOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(apiKeyOption),
            parseResult.GetValue(promptOption)!,
            parseResult.GetValue(voiceOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string? apiKey, string prompt, RealtimeVoicesEnum voice,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(OpenAiRealtimeCommand));

        apiKey ??= Environment.GetEnvironmentVariable("OPENAI_API_KEY");

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return WriteResult(asJson,
                new RealtimeResult(false, false, null, false, 0, null, null,
                    "An OpenAI API key is required (--api-key or the OPENAI_API_KEY environment variable)."),
                ExitCodes.InvalidArgument);
        }

        using var endpoint = new WebRTCEndPoint(apiKey, loggerFactory);

        var stopwatch = Stopwatch.StartNew();
        long? connectTimeMs = null;
        long? firstAudioMs = null;
        int audioFrames = 0;
        string? transcript = null;

        var voiceDetected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionFailed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        endpoint.OnAudioFrameReceived += _ =>
        {
            if (Interlocked.Increment(ref audioFrames) == 1)
            {
                firstAudioMs = stopwatch.ElapsedMilliseconds;
                logger.LogDebug("First audio frame received from OpenAI at {FirstAudioMs}ms; voice detected.", firstAudioMs);
                voiceDetected.TrySetResult(true);
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
                return WriteResult(asJson,
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
                return WriteResult(asJson,
                    new RealtimeResult(false, connectTimeMs != null, connectTimeMs, false, audioFrames, firstAudioMs, transcript,
                        "The peer connection failed before any audio was received."),
                    ExitCodes.TransportError);
            }

            if (completed != voiceDetected.Task)
            {
                bool connected = connectTimeMs != null;
                return WriteResult(asJson,
                    new RealtimeResult(false, connected, connectTimeMs, false, audioFrames, firstAudioMs, transcript,
                        ct.IsCancellationRequested ? "Cancelled." :
                        connected
                            ? $"Connected but no audio was received from the model within {timeoutSeconds}s."
                            : $"The connection did not establish within {timeoutSeconds}s."),
                    ExitCodes.Timeout);
            }

            // Voice detected. Give the model a short grace period to finish speaking so the
            // transcript and a representative frame count can be reported.
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }

            return WriteResult(asJson,
                new RealtimeResult(true, true, connectTimeMs, true, audioFrames, firstAudioMs, transcript, null),
                ExitCodes.Ok);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson,
                new RealtimeResult(false, connectTimeMs != null, connectTimeMs, false, audioFrames, firstAudioMs, transcript, "Cancelled."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson,
                new RealtimeResult(false, connectTimeMs != null, connectTimeMs, false, audioFrames, firstAudioMs, transcript, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            endpoint.Close();
        }
    }

    private static int WriteResult(bool asJson, RealtimeResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            string said = result.Transcript != null ? $" Model said: \"{result.Transcript}\"." : string.Empty;
            Console.WriteLine($"OpenAI Realtime OK: connected in {result.ConnectTimeMs}ms, voice detected after {result.FirstAudioMs}ms " +
                $"({result.AudioFrames} audio frames).{said}");
        }
        else
        {
            Console.Error.WriteLine($"OpenAI Realtime check failed: {result.Error}");
        }

        return exitCode;
    }
}
