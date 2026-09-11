//-----------------------------------------------------------------------------
// Filename: SipCallCommand.cs
//
// Description: The "sipsorcery sip call" verb. Places a SIP call, sends a
// device-less audio source (music, tone, silence or a file) and reports on
// the media received in return.
//
// Received audio can be routed three ways via --audio, following the rule
// that stdout carries exactly one payload:
//  - play:      spawn ffplay and render to the speakers; stdout untouched.
//  - <file.wav> write a WAV file; stdout untouched.
//  - "-"        raw s16le PCM on stdout; the result object moves to stderr.
//
// In addition --scope renders a live frequency spectrum and level meter for
// the received audio as a single self-redrawing line on stderr. Because the
// scope is display (stderr) rather than payload (stdout) it is independent
// of --audio and the two compose freely, e.g. listen AND watch with:
//
//   sipsorcery sip call music@iptel.org --audio play --scope
//
// No audio devices are used, so the verb behaves identically on every OS.
// For microphone input pipe PCM in via external tools (ffmpeg/sox), planned
// as --play -.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 12 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class SipCallCommand : CommandBase
{
    private const int DEFAULT_RING_TIMEOUT_SECONDS = 30;
    private const int DEFAULT_CALL_DURATION_SECONDS = 10;
    private const int DTMF_INTER_TONE_GAP_MILLISECONDS = 250;

    /// <summary>
    /// The result shape written with --json. Stable field names; additive changes only. Written
    /// to stdout unless the received audio has claimed stdout (--audio -), in which case the
    /// result moves to stderr.
    /// </summary>
    private sealed record CallResult(
        bool Success,
        string Destination,
        bool Answered,
        int? StatusCode,
        string? Codec,
        long? ConnectTimeMs,
        long? CallDurationMs,
        int AudioPackets,
        long AudioLost,
        int AudioOutOfOrder,
        int AudioDuplicates,
        long? AudioBytesWritten,
        string? Error);

    public SipCallCommand() : base(DEFAULT_RING_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var destinationArg = new Argument<string>("destination")
        {
            Description = "The SIP destination in the form [sip:|sips:|udp:|tcp:|tls:][user@]host[:port][;transport=x], " +
                          "e.g. music@iptel.org."
        };

        var playOption = new Option<string>("--play")
        {
            Description = "The audio to send: music, tone, silence, or the path to a 8/16KHz 16 bit mono PCM WAV file.",
            DefaultValueFactory = _ => "music"
        };

        var audioOption = new Option<string?>("--audio")
        {
            Description = "Where to send the received audio: \"play\" to render with ffplay, a .wav file path, " +
                          "or \"-\" for raw s16le PCM on stdout (the result then moves to stderr)."
        };

        var dtmfOption = new Option<string?>("--send-dtmf")
        {
            Description = "DTMF digits (0-9, * and #) to send once the call is answered."
        };

        var usernameOption = new Option<string?>("--username", "-u")
        {
            Description = "Optional username for authenticating the call."
        };

        var passwordOption = new Option<string?>("--password")
        {
            Description = "Optional password for authenticating the call."
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to stay on the call after it is answered. The call also ends if the remote party hangs up.",
            DefaultValueFactory = _ => DEFAULT_CALL_DURATION_SECONDS
        };

        var scopeOption = new Option<bool>("--scope")
        {
            Description = "Render a live frequency spectrum and level meter for the received audio on stderr. " +
                          "Composes with any --audio mode."
        };

        var hepOption = HepCapture.CreateOption();

        var command = new Command("call", "Place a SIP call, send a test audio source and report on the media received.");
        command.Arguments.Add(destinationArg);
        command.Options.Add(playOption);
        command.Options.Add(audioOption);
        command.Options.Add(scopeOption);
        command.Options.Add(dtmfOption);
        command.Options.Add(usernameOption);
        command.Options.Add(passwordOption);
        command.Options.Add(durationOption);
        command.Options.Add(hepOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(destinationArg)!,
            parseResult.GetValue(playOption)!,
            parseResult.GetValue(audioOption),
            parseResult.GetValue(scopeOption),
            parseResult.GetValue(dtmfOption),
            parseResult.GetValue(usernameOption),
            parseResult.GetValue(passwordOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(hepOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string destination, string play, string? audioOut, bool showScope, string? dtmf,
        string? username, string? password, int durationSeconds, string? hep, int ringTimeoutSeconds, bool asJson, bool verbose,
        CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(SipCallCommand));

        using var audioSink = AudioSink.Create(audioOut, logger, out string? sinkError);

        if (sinkError != null)
        {
            return WriteResult(asJson, audioSink,
                new CallResult(false, destination, false, null, null, null, null, 0, 0, 0, 0, null, sinkError),
                ExitCodes.InvalidArgument);
        }

        if (!SipDestination.TryParse(destination, out var dstUri, out var parseError))
        {
            return WriteResult(asJson, audioSink,
                new CallResult(false, destination, false, null, null, null, null, 0, 0, 0, 0, null, parseError),
                ExitCodes.InvalidArgument);
        }

        if (!TryGetAudioSource(play, out var sourceOptions, out byte[]? filePcm, out var fileRate, out var playError))
        {
            return WriteResult(asJson, audioSink,
                new CallResult(false, dstUri.ToString(), false, null, null, null, null, 0, 0, 0, 0, null, playError),
                ExitCodes.InvalidArgument);
        }

        using var hepCapture = HepCapture.Create(hep, logger, out string? hepError);

        if (hepError != null)
        {
            return WriteResult(asJson, audioSink,
                new CallResult(false, dstUri.ToString(), false, null, null, null, null, 0, 0, 0, 0, null, hepError),
                ExitCodes.InvalidArgument);
        }

        var sipTransport = new SIPTransport();
        hepCapture?.Attach(sipTransport);

        if (verbose)
        {
            sipTransport.EnableTraceLogs();
        }

        var mediaSession = new VoIPMediaSession();
        mediaSession.AcceptRtpFromAny = true;
        mediaSession.AudioExtrasSource.SetSource(sourceOptions);

        using var audioDecoder = new AudioEncoder();
        AudioFormat negotiatedFormat = AudioFormat.Empty;
        mediaSession.OnAudioFormatsNegotiated += (formats) => negotiatedFormat = formats.First();

        var audioStats = new RtpStreamStats();
        var dummyVideoStats = new RtpStreamStats();
        var recordPacket = RtpStreamStats.CreateRtpHandler(audioStats, dummyVideoStats, logger);

        using var scope = showScope
            ? new TerminalAudioScope(() => $"{(negotiatedFormat.IsEmpty() ? "?" : $"{negotiatedFormat.Codec}/{negotiatedFormat.ClockRate}")}  pkts {audioStats.Packets}")
            : null;

        mediaSession.OnRtpPacketReceived += (remoteEndPoint, mediaType, rtpPacket) =>
        {
            recordPacket(remoteEndPoint, mediaType, rtpPacket);

            if (mediaType == SDPMediaTypesEnum.audio && (audioSink.IsActive || scope != null) && !negotiatedFormat.IsEmpty())
            {
                try
                {
                    var pcm = audioDecoder.DecodeAudio(rtpPacket.GetPayloadBytes(), negotiatedFormat);
                    audioSink.Write(pcm, negotiatedFormat.ClockRate);
                    scope?.Write(pcm, negotiatedFormat.ClockRate);
                }
                catch (Exception excp)
                {
                    logger.LogWarning("Failed to decode received audio packet: {Error}", excp.Message);
                }
            }
        };

        var ua = new SIPUserAgent(sipTransport, null);
        SIPResponse? failureResponse = null;
        var remoteHungup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        ua.ClientCallTrying += (uac, resp) => Console.Error.WriteLine($"Trying: {resp.StatusCode} {resp.ReasonPhrase}.");
        ua.ClientCallRinging += (uac, resp) => Console.Error.WriteLine($"Ringing: {resp.StatusCode} {resp.ReasonPhrase}.");
        ua.ClientCallFailed += (uac, error, resp) =>
        {
            failureResponse = resp;
            Console.Error.WriteLine($"Call failed: {error}.");
        };
        ua.OnCallHungup += (dialog) =>
        {
            Console.Error.WriteLine("Remote party hung up.");
            remoteHungup.TrySetResult(true);
        };

        try
        {
            Console.Error.WriteLine($"Calling {dstUri} ...");

            var stopwatch = Stopwatch.StartNew();

            bool answered = await ua.Call(dstUri.ToString(), username, password, mediaSession, ringTimeoutSeconds).ConfigureAwait(false);

            long connectTimeMs = stopwatch.ElapsedMilliseconds;

            if (!answered)
            {
                int? statusCode = failureResponse != null ? (int)failureResponse.Status : null;
                return WriteResult(asJson, audioSink,
                    new CallResult(false, dstUri.ToString(), false, statusCode, null, connectTimeMs, null, 0, 0, 0, 0, null,
                        statusCode != null
                            ? $"The call was not answered: {statusCode} {failureResponse!.ReasonPhrase}."
                            : $"The call was not answered within {ringTimeoutSeconds}s."),
                    statusCode != null ? ExitCodes.Failed : ExitCodes.Timeout);
            }

            Console.Error.WriteLine($"Answered in {connectTimeMs}ms. Staying on the call for up to {durationSeconds}s.");

            // If a WAV file was supplied for the send audio, stream it now (interrupts the
            // configured source for its duration).
            if (filePcm != null)
            {
                _ = mediaSession.AudioExtrasSource.SendAudioFromStream(new MemoryStream(filePcm), fileRate);
            }

            if (!string.IsNullOrWhiteSpace(dtmf))
            {
                foreach (char digit in dtmf)
                {
                    if (TryGetDtmfByte(digit, out byte tone))
                    {
                        logger.LogDebug("Sending DTMF tone {Digit}.", digit);
                        await ua.SendDtmf(tone).ConfigureAwait(false);
                        await Task.Delay(DTMF_INTER_TONE_GAP_MILLISECONDS, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        logger.LogWarning("Ignoring invalid DTMF digit '{Digit}'.", digit);
                    }
                }
            }

            var callWindow = Stopwatch.StartNew();
            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(durationSeconds), ct), remoteHungup.Task).ConfigureAwait(false);
            callWindow.Stop();

            if (ua.IsCallActive)
            {
                ua.Hangup();
                // Give the BYE a moment to be transmitted.
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }

            bool gotMedia = audioStats.Packets > 0;

            return WriteResult(asJson, audioSink,
                new CallResult(gotMedia, dstUri.ToString(), true, (int)SIPResponseStatusCodesEnum.Ok,
                    negotiatedFormat.IsEmpty() ? null : $"{negotiatedFormat.Codec}/{negotiatedFormat.ClockRate}",
                    connectTimeMs, callWindow.ElapsedMilliseconds,
                    audioStats.Packets, audioStats.Lost, audioStats.OutOfOrder, audioStats.Duplicates,
                    audioSink.IsActive ? audioSink.BytesWritten : null,
                    gotMedia ? null : "The call was answered but no audio was received."),
                gotMedia ? ExitCodes.Ok : ExitCodes.Failed);
        }
        catch (OperationCanceledException)
        {
            return WriteResult(asJson, audioSink,
                new CallResult(false, dstUri.ToString(), false, null, null, null, null,
                    audioStats.Packets, audioStats.Lost, audioStats.OutOfOrder, audioStats.Duplicates, null, "Cancelled."),
                ExitCodes.Timeout);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson, audioSink,
                new CallResult(false, dstUri.ToString(), false, null, null, null, null,
                    audioStats.Packets, audioStats.Lost, audioStats.OutOfOrder, audioStats.Duplicates, null, excp.Message),
                ExitCodes.TransportError);
        }
        finally
        {
            sipTransport.Shutdown();
        }
    }

    private static bool TryGetAudioSource(string play, out AudioSourceOptions options, out byte[]? filePcm,
        out AudioSamplingRatesEnum fileRate, out string? error)
    {
        options = new AudioSourceOptions { AudioSource = AudioSourcesEnum.Music };
        filePcm = null;
        fileRate = AudioSamplingRatesEnum.Rate8KHz;
        error = null;

        switch (play.ToLowerInvariant())
        {
            case "music":
                return true;
            case "tone":
                options.AudioSource = AudioSourcesEnum.SineWave;
                return true;
            case "silence":
                options.AudioSource = AudioSourcesEnum.Silence;
                return true;
            default:
                if (!File.Exists(play))
                {
                    error = $"The --play value \"{play}\" is not music, tone, silence or an existing file.";
                    return false;
                }

                if (!WavFile.TryReadPcm(play, out filePcm, out int sampleRate, out string? wavError))
                {
                    error = wavError;
                    return false;
                }

                fileRate = sampleRate == 16000 ? AudioSamplingRatesEnum.Rate16KHz : AudioSamplingRatesEnum.Rate8KHz;
                options.AudioSource = AudioSourcesEnum.Silence;
                return true;
        }
    }

    private static bool TryGetDtmfByte(char digit, out byte tone)
    {
        switch (digit)
        {
            case >= '0' and <= '9':
                tone = (byte)(digit - '0');
                return true;
            case '*':
                tone = 10;
                return true;
            case '#':
                tone = 11;
                return true;
            default:
                tone = 0;
                return false;
        }
    }

    private static int WriteResult(bool asJson, AudioSink audioSink, CallResult result, int exitCode)
    {
        // The stdout payload rule: when the received audio has claimed stdout, the result is
        // commentary and moves to stderr.
        var output = audioSink.IsStdout ? Console.Error : Console.Out;

        if (asJson)
        {
            output.WriteLine(SerializeResult(result));
        }
        else if (result.Success)
        {
            string sink = result.AudioBytesWritten != null ? $", {result.AudioBytesWritten} bytes of audio written" : string.Empty;
            output.WriteLine($"Call to {result.Destination} answered in {result.ConnectTimeMs}ms, codec {result.Codec}. " +
                $"Received {result.AudioPackets} audio packets ({FormatAnomalies(result.AudioLost, result.AudioOutOfOrder, result.AudioDuplicates)}) " +
                $"in {result.CallDurationMs}ms{sink}.");
        }
        else
        {
            Console.Error.WriteLine($"Call to {result.Destination} failed: {result.Error}");
        }

        return exitCode;
    }

    private static string FormatAnomalies(long lost, int outOfOrder, int duplicates)
    {
        if (lost == 0 && outOfOrder == 0 && duplicates == 0)
        {
            return "clean";
        }

        return $"{lost} lost, {outOfOrder} reordered, {duplicates} duplicate";
    }
}
