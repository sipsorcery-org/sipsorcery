//-----------------------------------------------------------------------------
// Filename: WebRtcWhipCommand.cs
//
// Description: The "sipsorcery webrtc whip" verb. Publishes a generated video
// test pattern to a WHIP (WebRTC-HTTP Ingestion Protocol) endpoint using the
// SIPSorcery stack as the sender, exercising the full send pipeline: generate ->
// encode -> RTP packetise -> SRTP -> ICE/DTLS socket. It is the publishing
// counterpart to "webrtc whip-server" (a library->library loopback when pointed
// at it) and also publishes to any WHIP ingest (Broadcast Box, MediaMTX, ...).
//
// The publish itself is implemented by the shared LibraryVideoPublisher, which
// is also used in-process by "webrtc whip-server --publish". Where "video-bench"
// measures the encoder/packetiser in isolation (no network), this adds the real
// WebRTC transport. The encoder is selectable: vp8.net (managed Vpx.Net VP8) or
// ffmpeg (SIPSorceryMedia.FFmpeg, H264 or VP8 via --codec).
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 15 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.CommandLine;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class WebRtcWhipCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 15;
    private const int DEFAULT_DURATION_SECONDS = 10;
    private const int DEFAULT_FPS = 30;
    private const string DEFAULT_PRESET = "720p";
    private const string DEFAULT_ENCODER = "vp8.net";

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record WhipResult(
        bool Success,
        string Url,
        string Encoder,
        string Codec,
        int Width,
        int Height,
        int TargetFps,
        string ConnectionState,
        long? ConnectTimeMs,
        int? MediaDurationMs,
        int FramesSent,
        long BytesSent,
        double AchievedFps,
        double EncodeMsAvg,
        string? Error);

    public WebRtcWhipCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var urlArg = new Argument<string>("url")
        {
            Description = "The WHIP endpoint URL to publish to, e.g. http://localhost:8080/whip."
        };

        var presetOption = new Option<string>("--preset")
        {
            Description = $"Resolution preset ({VideoPresets.Names}). Ignored if --size is given.",
            DefaultValueFactory = _ => DEFAULT_PRESET
        };

        var sizeOption = new Option<string?>("--size", "-s")
        {
            Description = "Explicit frame size WxH (e.g. 1280x720), overriding --preset."
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = "Target frame rate to publish at (ignored with --max-rate).",
            DefaultValueFactory = _ => DEFAULT_FPS
        };

        var encoderOption = new Option<string>("--encoder")
        {
            Description = "Video encoder: vp8.net (managed Vpx.Net VP8, no native deps) or ffmpeg (SIPSorceryMedia.FFmpeg).",
            DefaultValueFactory = _ => DEFAULT_ENCODER
        };

        var codecOption = new Option<string?>("--codec")
        {
            Description = "Codec for the ffmpeg encoder: h264 (default), h265, vp8, vp9 or av1. Ignored for vp8.net, which is always VP8."
        };

        var bitrateOption = new Option<int>("--bitrate")
        {
            Description = "Target encoder bitrate in bits per second (ffmpeg encoder only). 0 (default) derives it from the resolution and frame rate."
        };

        var maxRateOption = new Option<bool>("--max-rate")
        {
            Description = "Send as fast as the encoder and transport allow (ignores --fps) to measure the send pipeline ceiling. Use only against a local receiver, never a remote ingest."
        };

        var ffmpegPathOption = new Option<string?>("--ffmpeg-path")
        {
            Description = "Directory containing the FFmpeg shared libraries for --encoder ffmpeg. Defaults to the system path."
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "Optional bearer token for the WHIP endpoint Authorization header."
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to publish for after the connection is established.",
            DefaultValueFactory = _ => DEFAULT_DURATION_SECONDS
        };

        var command = new Command("whip", "Publish a test pattern to a WHIP endpoint using the SIPSorcery stack (full send pipeline: encode, RTP/SRTP, ICE/DTLS).");
        command.Arguments.Add(urlArg);
        command.Options.Add(presetOption);
        command.Options.Add(sizeOption);
        command.Options.Add(fpsOption);
        command.Options.Add(encoderOption);
        command.Options.Add(codecOption);
        command.Options.Add(bitrateOption);
        command.Options.Add(maxRateOption);
        command.Options.Add(ffmpegPathOption);
        command.Options.Add(tokenOption);
        command.Options.Add(durationOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => RunAsync(
            parseResult.GetValue(urlArg)!,
            new LibraryVideoPublisher.Settings(
                parseResult.GetValue(presetOption)!,
                parseResult.GetValue(sizeOption),
                parseResult.GetValue(fpsOption),
                parseResult.GetValue(encoderOption)!,
                parseResult.GetValue(codecOption),
                parseResult.GetValue(bitrateOption),
                parseResult.GetValue(maxRateOption),
                parseResult.GetValue(ffmpegPathOption),
                parseResult.GetValue(durationOption)),
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(string url, LibraryVideoPublisher.Settings settings, string? token,
        int timeoutSeconds, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(WebRtcWhipCommand));

        string encoder = settings.Encoder.ToLowerInvariant();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttp && endpointUri.Scheme != Uri.UriSchemeHttps))
        {
            return WriteResult(asJson,
                new WhipResult(false, url, encoder, "", 0, 0, settings.Fps, "new", null, null, 0, 0, 0, 0,
                    $"Could not parse \"{url}\" as an HTTP or HTTPS URL."),
                ExitCodes.InvalidArgument);
        }

        Console.Error.WriteLine($"Publishing {settings.Preset}{(settings.Size != null ? $" ({settings.Size})" : "")} {encoder} " +
            $"{(settings.MaxRate ? "flat out" : $"at {settings.Fps} fps")} to {url}.");

        var result = await LibraryVideoPublisher.RunAsync(url, settings, token, timeoutSeconds, logger, ct).ConfigureAwait(false);

        return WriteResult(asJson,
            new WhipResult(result.Success, url, encoder, result.Codec, result.Width, result.Height, settings.Fps,
                result.ConnectionState, result.ConnectTimeMs, result.MediaDurationMs, result.FramesSent, result.BytesSent,
                result.AchievedFps, result.EncodeMsAvg, result.Error),
            result.ExitCode);
    }

    private static int WriteResult(bool asJson, WhipResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else if (result.Success)
        {
            Console.WriteLine($"WHIP publish to {result.Url} OK: connected in {result.ConnectTimeMs}ms, sent {result.FramesSent} {result.Width}x{result.Height} " +
                $"{result.Codec} frames ({result.Encoder}) at {result.AchievedFps} fps over {result.MediaDurationMs}ms (encode {result.EncodeMsAvg}ms/frame avg).");
        }
        else
        {
            Console.Error.WriteLine($"WHIP publish to {result.Url} failed (state {result.ConnectionState}): {result.Error}");
        }

        return exitCode;
    }
}
