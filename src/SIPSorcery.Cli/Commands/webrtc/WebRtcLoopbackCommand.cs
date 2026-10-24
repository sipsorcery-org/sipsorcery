//-----------------------------------------------------------------------------
// Filename: WebRtcLoopbackCommand.cs
//
// Description: The "sipsorcery webrtc loopback" verb. Runs a self-contained
// WHIP encode -> network -> decode loop in a single process: it starts the same
// WHIP receive engine as "webrtc whip-server" and, in-process, publishes a
// generated test pattern to that listener with the shared LibraryVideoPublisher
// (the same publisher "webrtc whip" uses). No second terminal, no external
// network: the media goes out over the loopback transport and back into the
// receiver, which reports the send-side and receive-side stats together.
//
// This is the one process equivalent of running "webrtc whip-server" and
// "webrtc whip" side by side, and is what the video pipeline benchmark drives.
// Because the receiver only services a single session, this verb occupies it
// with the in-process publisher; to receive from an external publisher (ffmpeg,
// OBS, an SFU) use "webrtc whip-server" without this verb.
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

namespace SIPSorcery.Cli.Commands;

public sealed class WebRtcLoopbackCommand : CommandBase
{
    private const int DEFAULT_TIMEOUT_SECONDS = 60;
    private const int DEFAULT_MEDIA_DURATION_SECONDS = 10;
    private const string DEFAULT_LISTEN_URL = "http://localhost:8080/whip";
    private const string DEFAULT_ENCODER = "ffmpeg";
    private const string DEFAULT_PRESET = "720p";
    private const int DEFAULT_FPS = 30;

    public WebRtcLoopbackCommand() : base(DEFAULT_TIMEOUT_SECONDS)
    { }

    public override Command Build()
    {
        var listenOption = new Option<string>("--listen")
        {
            Description = $"The local HTTP URL the in-process receiver binds and the publisher targets. Defaults to {DEFAULT_LISTEN_URL}.",
            DefaultValueFactory = _ => DEFAULT_LISTEN_URL
        };

        var tokenOption = new Option<string?>("--token")
        {
            Description = "Optional bearer token shared between the in-process publisher and receiver."
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to run the loop for after the connection is established.",
            DefaultValueFactory = _ => DEFAULT_MEDIA_DURATION_SECONDS
        };

        var videoOption = new Option<string?>("--video")
        {
            Description = "Where to send the received video: \"play\" to render in an ffplay window, a file path " +
                          "(H264 is written as Annex B, VP8 in an IVF container), \"-\" for the bitstream on stdout " +
                          "(the result then moves to stderr), or \"null\" to discard it (headless throughput measurement, e.g. with --decode)."
        };

        var decodeOption = new Option<bool>("--decode")
        {
            Description = "Decode the received frames in-process (see --decoder) and send raw RGB to the --video sink, " +
                          "instead of passing the encoded bitstream through for the consumer to decode (the default). " +
                          "Requires a --video sink."
        };

        var decoderOption = new Option<string>("--decoder")
        {
            Description = "With --decode: the decoder, ffmpeg (SIPSorceryMedia.FFmpeg, any codec) or vp8.net (managed Vpx.Net, VP8 only).",
            DefaultValueFactory = _ => "ffmpeg"
        };

        var ffmpegPathOption = new Option<string?>("--ffmpeg-path")
        {
            Description = "Directory containing the FFmpeg shared libraries for the ffmpeg encoder and/or ffmpeg --decoder. Defaults to the system path."
        };

        var encoderOption = new Option<string>("--encoder")
        {
            Description = "Video encoder: vp8.net (managed Vpx.Net VP8, no native deps) or ffmpeg (SIPSorceryMedia.FFmpeg).",
            DefaultValueFactory = _ => DEFAULT_ENCODER
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
            Description = "Frame rate to publish at.",
            DefaultValueFactory = _ => DEFAULT_FPS
        };

        var codecOption = new Option<string?>("--codec")
        {
            Description = "Codec for the ffmpeg encoder: h264 (default), h265, vp8, vp9 or av1. Ignored for vp8.net, which is always VP8."
        };

        var bitrateOption = new Option<int>("--bitrate")
        {
            Description = "Target encoder bitrate in bits per second (ffmpeg encoder only). 0 (default) derives it from the resolution and frame rate."
        };

        var preEncodeOption = new Option<int>("--pre-encode")
        {
            Description = "Encode this many frames once before connecting and replay the encoded bitstream in a loop, so " +
                          "no encoding runs during the send window. Use it to isolate the receive/decode stage (e.g. with " +
                          "--decode) from the encoder. 0 (default) encodes live in the send loop."
        };

        var maxRateOption = new Option<bool>("--max-rate")
        {
            Description = "Send as fast as the encoder/replay and transport allow (ignores --fps), to measure the pipeline " +
                          "ceiling. Pair with --pre-encode and no --decode to get the pure transport (packetise/SRTP) ceiling."
        };

        var command = new Command("loopback",
            "Publish a test pattern to an in-process WHIP receiver and report on it: a self-contained encode, network and decode loop in one process.");
        command.Options.Add(listenOption);
        command.Options.Add(tokenOption);
        command.Options.Add(durationOption);
        command.Options.Add(videoOption);
        command.Options.Add(decodeOption);
        command.Options.Add(decoderOption);
        command.Options.Add(ffmpegPathOption);
        command.Options.Add(encoderOption);
        command.Options.Add(presetOption);
        command.Options.Add(sizeOption);
        command.Options.Add(fpsOption);
        command.Options.Add(codecOption);
        command.Options.Add(bitrateOption);
        command.Options.Add(preEncodeOption);
        command.Options.Add(maxRateOption);
        AddCommonOptions(command);

        command.SetAction((parseResult, cancellationToken) => WebRtcWhipServerCommand.RunReceiverAsync(
            parseResult.GetValue(listenOption)!,
            parseResult.GetValue(tokenOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(videoOption),
            parseResult.GetValue(decodeOption),
            parseResult.GetValue(decoderOption)!,
            parseResult.GetValue(ffmpegPathOption),
            // DurationSeconds 0: the publisher runs until the receiver stops it at the end of the media window.
            new LibraryVideoPublisher.Settings(
                parseResult.GetValue(presetOption)!,
                parseResult.GetValue(sizeOption),
                parseResult.GetValue(fpsOption),
                parseResult.GetValue(encoderOption)!,
                parseResult.GetValue(codecOption),
                parseResult.GetValue(bitrateOption),
                parseResult.GetValue(maxRateOption),
                parseResult.GetValue(ffmpegPathOption),
                0,
                parseResult.GetValue(preEncodeOption)),
            parseResult.GetValue(TimeoutOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken));

        return command;
    }
}
