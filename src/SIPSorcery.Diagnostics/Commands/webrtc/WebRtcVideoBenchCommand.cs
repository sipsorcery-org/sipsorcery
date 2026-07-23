//-----------------------------------------------------------------------------
// Filename: WebRtcVideoBenchCommand.cs
//
// Description: The "sipsorcery webrtc video-bench" verb. Benchmarks the video
// send pipeline to answer "can this machine sustain a target resolution and
// frame rate" (the motivating goal being 1080p30). The pipeline is measured in
// stages so the bottleneck can be isolated:
//
//   Stage 1 (--encoder none):        no encoding. A static, pre-sized encoded
//                                    frame is packetised flat out. Measures the
//                                    RTP packetisation/serialisation ceiling.
//   Stage 2a (--encoder ffmpeg):     native codec via the SIPSorceryMedia.FFmpeg
//                                    in-process IVideoEncoder (FFmpeg.AutoGen).
//                                    --codec selects vp8, vp9, h264, h265 or av1.
//   Stage 2b (--encoder ffmpeg-piped): native codec by piping raw frames to an
//                                    external ffmpeg process (vp8/vp9 via IVF).
//   Stage 3 (--encoder vp8.net):     the managed Vpx.Net VP8 codec.
//
// This is a SEND-SIDE benchmark: there is no peer connection, DTLS/SRTP or
// socket. Each frame is fragmented and serialised the same way the library's
// VideoStream.SendVp8Frame does (RTP_MAX_PAYLOAD chunks, a one-byte payload
// descriptor stand-in, real RTPPacket serialisation) and the bytes are counted
// and discarded; that serialisation cost is codec independent. The reported
// achieved frame rate is the maximum the stage can do; comparing the stages
// shows whether the limit is the packetiser or the encoder.
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
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.Sys;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;
using Vpx.Net;

namespace SIPSorcery.Diagnostics.Commands;

public sealed class WebRtcVideoBenchCommand : CommandBase
{
    private const int DEFAULT_WIDTH = 1920;
    private const int DEFAULT_HEIGHT = 1080;
    private const int DEFAULT_FPS = 30;
    private const int DEFAULT_DURATION_SECONDS = 5;
    // Bits per pixel per frame used to derive a representative default bitrate from the resolution
    // and frame rate when --bitrate is not given (~0.1 bpp is a typical medium-quality H264/VP8
    // figure: 720p30 -> ~2.8 Mbps, 1080p30 -> ~6.2 Mbps, 4K50 -> ~41 Mbps).
    private const double BITS_PER_PIXEL_PER_FRAME = 0.1;
    private const int VP8_PAYLOAD_ID = 96;
    private const uint VIDEO_CLOCK_RATE = 90000;

    // Mirrors RTPSession.RTP_MAX_PAYLOAD (which is protected internal so not visible here). The
    // packetisation below must use the same value as the library to produce a representative
    // packet count per frame.
    private const int RTP_MAX_PAYLOAD = 1200;

    // Cap on the number of per-frame timings retained for the percentile calculation. The average
    // and max are accumulated over every frame; only the percentile sample is bounded so a flat out
    // packetisation run (which can do hundreds of thousands of frames) does not grow unbounded.
    private const int MAX_TIMING_SAMPLES = 100_000;

    // Number of distinct frames pre-generated and cycled through so the encoder sees motion.
    private const int RING_SIZE = 16;

    /// <summary>
    /// The result shape written to stdout with --json. Stable field names; additive changes only.
    /// </summary>
    private sealed record VideoBenchResult(
        bool Success,
        string Stage,
        string Codec,
        int Width,
        int Height,
        int TargetFps,
        int FramesProcessed,
        double AchievedFps,
        double PerFrameMsAvg,
        double PerFrameMsP95,
        double PerFrameMsMax,
        int PacketsPerFrame,
        int FrameBytes,
        double ThroughputMbps,
        long DurationMs,
        string? Error);

    public WebRtcVideoBenchCommand() : base(DEFAULT_DURATION_SECONDS)
    { }

    public override Command Build()
    {
        var presetOption = new Option<string?>("--preset")
        {
            Description = $"Resolution preset ({VideoPresets.Names}); overrides --width/--height when set."
        };

        var widthOption = new Option<int>("--width", "-w")
        {
            Description = "The frame width in pixels.",
            DefaultValueFactory = _ => DEFAULT_WIDTH
        };

        var heightOption = new Option<int>("--height")
        {
            Description = "The frame height in pixels.",
            DefaultValueFactory = _ => DEFAULT_HEIGHT
        };

        var fpsOption = new Option<int>("--fps")
        {
            Description = "The target frame rate to test against.",
            DefaultValueFactory = _ => DEFAULT_FPS
        };

        var durationOption = new Option<int>("--duration", "-d")
        {
            Description = "The number of seconds to run the benchmark for.",
            DefaultValueFactory = _ => DEFAULT_DURATION_SECONDS
        };

        var bitrateOption = new Option<int>("--bitrate")
        {
            Description = "Target encoded bitrate in bits per second; sets the per-frame size for the packetise stage. " +
                          "0 (default) derives it from the resolution and frame rate.",
            DefaultValueFactory = _ => 0
        };

        var encoderOption = new Option<string>("--encoder")
        {
            Description = "The pipeline stage to test: none (packetise only), ffmpeg (SIPSorceryMedia.FFmpeg in-process encoder), " +
                          "ffmpeg-piped (external ffmpeg process) or vp8.net (managed Vpx.Net codec). The none stage packetises a frame " +
                          "sized from --bitrate/--fps, so it is independent of resolution (width/height are reported for context only).",
            DefaultValueFactory = _ => "none"
        };

        var codecOption = new Option<string>("--codec")
        {
            Description = "The video codec to encode/packetise as: vp8, vp9, h264, h265 (alias hevc) or av1. The in-process " +
                          "ffmpeg encoder supports all five; vp8.net is VP8 only and ffmpeg-piped supports vp8 and vp9 (IVF) only.",
            DefaultValueFactory = _ => "vp8"
        };

        var ffmpegPathOption = new Option<string?>("--ffmpeg-path")
        {
            Description = "Directory containing the FFmpeg shared libraries for the --encoder ffmpeg stage. Defaults to the system path."
        };

        // libvpx tuning knobs, applied to the ffmpeg and ffmpeg-piped stages.
        var deadlineOption = new Option<string>("--deadline")
        {
            Description = "libvpx deadline for the ffmpeg stages: realtime, good or best.",
            DefaultValueFactory = _ => "realtime"
        };

        var cpuUsedOption = new Option<int>("--cpu-used")
        {
            Description = "libvpx cpu-used (speed) for the ffmpeg stages. Higher is faster/lower quality (VP8 realtime 0-16). -1 leaves it unset.",
            DefaultValueFactory = _ => 5
        };

        var threadsOption = new Option<int>("--threads")
        {
            Description = "Encoder thread count for the ffmpeg stages. 0 lets the encoder decide.",
            DefaultValueFactory = _ => 0
        };

        var av1EncoderOption = new Option<string>("--av1-encoder")
        {
            Description = "FFmpeg AV1 encoder for the in-process ffmpeg stage: libsvtav1 (fastest for realtime), " +
                          "libaom-av1, librav1e, or a hardware encoder (av1_nvenc, av1_qsv). Applies to --codec av1 and --all.",
            DefaultValueFactory = _ => "libsvtav1"
        };

        var av1PresetOption = new Option<int>("--av1-preset")
        {
            Description = "Speed preset for the AV1 encoder, higher is faster (libsvtav1 preset 0-13). -1 uses the encoder's realtime default.",
            DefaultValueFactory = _ => -1
        };

        var allOption = new Option<bool>("--all", "--extended")
        {
            Description = "Benchmark every video codec (vp8, vp9, h265, av1) with the in-process ffmpeg encoder and report each. Overrides --encoder and --codec."
        };

        var command = new Command("video-bench", "Benchmark the video send pipeline (packetisation/encoding) against a target resolution and frame rate.");
        command.Options.Add(presetOption);
        command.Options.Add(widthOption);
        command.Options.Add(heightOption);
        command.Options.Add(fpsOption);
        command.Options.Add(durationOption);
        command.Options.Add(bitrateOption);
        command.Options.Add(encoderOption);
        command.Options.Add(codecOption);
        command.Options.Add(ffmpegPathOption);
        command.Options.Add(deadlineOption);
        command.Options.Add(cpuUsedOption);
        command.Options.Add(threadsOption);
        command.Options.Add(av1EncoderOption);
        command.Options.Add(av1PresetOption);
        command.Options.Add(allOption);
        command.Options.Add(JsonOption);
        command.Options.Add(VerboseOption);

        command.SetAction((parseResult, cancellationToken) => Task.FromResult(Run(
            parseResult.GetValue(presetOption),
            parseResult.GetValue(widthOption),
            parseResult.GetValue(heightOption),
            parseResult.GetValue(fpsOption),
            parseResult.GetValue(durationOption),
            parseResult.GetValue(bitrateOption),
            parseResult.GetValue(encoderOption)!,
            parseResult.GetValue(codecOption)!,
            parseResult.GetValue(ffmpegPathOption),
            new FfmpegTuning(parseResult.GetValue(deadlineOption)!, parseResult.GetValue(cpuUsedOption), parseResult.GetValue(threadsOption),
                parseResult.GetValue(av1EncoderOption)!, parseResult.GetValue(av1PresetOption)),
            parseResult.GetValue(allOption),
            parseResult.GetValue(JsonOption),
            parseResult.GetValue(VerboseOption),
            cancellationToken)));

        return command;
    }

    /// <summary>Encoder tuning passed to the ffmpeg encoder stages.</summary>
    private readonly record struct FfmpegTuning(string Deadline, int CpuUsed, int Threads, string Av1Encoder, int Av1Preset);

    /// <summary>The codecs benchmarked by --all, using the in-process ffmpeg encoder.</summary>
    private static readonly (string Label, VideoCodecsEnum Codec)[] ALL_CODECS =
    {
        ("vp8", VideoCodecsEnum.VP8),
        ("vp9", VideoCodecsEnum.VP9),
        ("h265", VideoCodecsEnum.H265),
        ("av1", VideoCodecsEnum.AV1),
    };

    private static int Run(string? preset, int width, int height, int fps, int durationSeconds, int bitrate, string encoder, string codec,
        string? ffmpegPath, FfmpegTuning tuning, bool all, bool asJson, bool verbose, CancellationToken ct)
    {
        using var loggerFactory = InitLogging(verbose);
        var logger = loggerFactory.CreateLogger(nameof(WebRtcVideoBenchCommand));

        if (!string.IsNullOrWhiteSpace(preset))
        {
            if (!VideoPresets.TryResolve(preset, out width, out height, out string? presetError))
            {
                return WriteResult(asJson, Empty(encoder, codec, width, height, fps, presetError!),
                    ExitCodes.InvalidArgument);
            }
        }

        if (width < 2 || height < 2 || fps < 1 || durationSeconds < 1)
        {
            return WriteResult(asJson, Empty(encoder, codec, width, height, fps,
                "Invalid arguments: width/height must be >= 2, fps >= 1 and duration >= 1."),
                ExitCodes.InvalidArgument);
        }

        if (bitrate <= 0)
        {
            // Derive a representative bitrate from the resolution and frame rate so the per-frame size
            // (and so the packetise stage) scales with the picture instead of using a fixed default.
            bitrate = (int)(width * (long)height * fps * BITS_PER_PIXEL_PER_FRAME);
        }

        if (bitrate < 1000)
        {
            return WriteResult(asJson, Empty(encoder, codec, width, height, fps,
                "Invalid --bitrate: must be >= 1000 bits per second."),
                ExitCodes.InvalidArgument);
        }

        // Extended mode benchmarks every codec with the in-process ffmpeg encoder (the only one that
        // supports them all), ignoring --encoder/--codec.
        if (all)
        {
            return RunAllCodecs(width, height, fps, durationSeconds, ffmpegPath, tuning, asJson, logger, ct);
        }

        if (!TryParseCodec(codec, out var codecEnum, out string? codecError))
        {
            return WriteResult(asJson, Empty(encoder, codec, width, height, fps, codecError!),
                ExitCodes.InvalidArgument);
        }

        switch (encoder.ToLowerInvariant())
        {
            case "none":
                // Packetise-only models a target-sized encoded frame and fragments it; the measured cost
                // is the RTP serialisation, which is codec independent, so any codec label is accepted.
                return RunPacketiseOnly(width, height, fps, durationSeconds, bitrate, codec, asJson, logger, ct);

            case "vp8.net":
                if (codecEnum != VideoCodecsEnum.VP8)
                {
                    return WriteResult(asJson, Empty(encoder, codec, width, height, fps,
                        $"The vp8.net managed encoder only supports --codec vp8. Use --encoder ffmpeg for {codec.ToLowerInvariant()}."),
                        ExitCodes.InvalidArgument);
                }
                return RunVp8Encode(width, height, fps, durationSeconds, codec, asJson, logger, ct);

            case "ffmpeg":
                return RunNativeFfmpegEncode(width, height, fps, durationSeconds, codec, codecEnum, ffmpegPath, tuning, asJson, logger, ct);

            case "ffmpeg-piped":
                return RunFfmpegEncode(width, height, fps, durationSeconds, bitrate, codec, codecEnum, tuning, asJson, logger, ct);

            default:
                return WriteResult(asJson, Empty(encoder, codec, width, height, fps,
                    $"Unknown --encoder \"{encoder}\". Expected none, ffmpeg, ffmpeg-piped or vp8.net."),
                    ExitCodes.InvalidArgument);
        }
    }

    /// <summary>
    /// Stage 1: no encoding. Packetises a static, target-sized encoded frame flat out and measures
    /// how many frames per second the RTP packetisation/serialisation path can produce. If this
    /// already cannot reach the target frame rate the bottleneck is the packetiser, not the encoder.
    /// </summary>
    private static int RunPacketiseOnly(int width, int height, int fps, int durationSeconds, int bitrate, string codec,
        bool asJson, ILogger logger, CancellationToken ct)
    {
        // A representative encoded frame size for the target bitrate, e.g. 4 Mbps at 30 fps is
        // ~16.7 KB per frame. The packetiser cost scales with this (number of RTP fragments), not
        // with the pixel dimensions, so width/height are reported for context only at this stage.
        int frameBytes = Math.Max(1, bitrate / fps / 8);
        var encodedFrame = new byte[frameBytes];
        new Random(20260613).NextBytes(encodedFrame);

        // Make the model explicit: this stage does not encode, so the frame size (and therefore the
        // result) comes from the bitrate/frame rate, and the resolution is shown for context only.
        Console.Error.WriteLine($"Packetise-only stage: modelling a {frameBytes}-byte encoded frame from {bitrate / 1000} kbps at {fps} fps " +
            $"({width}x{height} is context only; use --encoder ffmpeg/vp8 to benchmark actual encoding).");

        uint rtpDuration = VIDEO_CLOCK_RATE / (uint)fps;
        uint ssrc = 0x1234_5678;
        ushort seqnum = 0;
        uint timestamp = 0;

        int frames = 0;
        long totalPackets = 0;
        long totalBytes = 0;
        int packetsPerFrame = 0;

        double sumMs = 0;
        double maxMs = 0;
        var samples = new List<double>(Math.Min(MAX_TIMING_SAMPLES, fps * durationSeconds + 1));

        logger.LogDebug("Stage 1 packetise-only: {Width}x{Height}@{Fps}, frame size {FrameBytes} bytes, for {Duration}s.",
            width, height, fps, frameBytes, durationSeconds);

        long durationMs = durationSeconds * 1000L;
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < durationMs && !ct.IsCancellationRequested)
        {
            long start = Stopwatch.GetTimestamp();

            int packets = PacketiseFrame(encodedFrame, timestamp, ssrc, ref seqnum, ref totalBytes);

            double frameMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            sumMs += frameMs;
            if (frameMs > maxMs) { maxMs = frameMs; }
            if (samples.Count < MAX_TIMING_SAMPLES) { samples.Add(frameMs); }

            packetsPerFrame = packets;
            totalPackets += packets;
            timestamp += rtpDuration;
            frames++;
        }

        sw.Stop();

        double elapsedSec = sw.Elapsed.TotalSeconds;
        double achievedFps = frames / elapsedSec;
        double throughputMbps = totalBytes * 8.0 / elapsedSec / 1_000_000.0;
        double avgMs = frames > 0 ? sumMs / frames : 0;
        double p95Ms = Percentile(samples, 95);

        bool success = achievedFps >= fps;

        var result = new VideoBenchResult(success, "none", codec.ToLowerInvariant(), width, height, fps,
            frames, Math.Round(achievedFps, 1), Math.Round(avgMs, 4), Math.Round(p95Ms, 4), Math.Round(maxMs, 4),
            packetsPerFrame, frameBytes, Math.Round(throughputMbps, 2), sw.ElapsedMilliseconds,
            success ? null : $"Packetisation only reached {achievedFps:0} fps, below the {fps} fps target.");

        return WriteResult(asJson, result, success ? ExitCodes.Ok : ExitCodes.Failed);
    }

    /// <summary>
    /// Stage 3: managed Vpx.Net VP8 codec. Encodes synthetic I420 frames flat out with
    /// VP8Codec.EncodeVideo. This is the throughput the library delivers today with no native deps.
    /// </summary>
    private static int RunVp8Encode(int width, int height, int fps, int durationSeconds, string codec,
        bool asJson, ILogger logger, CancellationToken ct)
    {
        // VP8 requires the coded dimensions to be positive multiples of 16, so round up (encoders
        // pad to macroblock boundaries internally anyway). 1080 is not a multiple of 16, so 1080p
        // is encoded as 1088 lines.
        int encWidth = RoundUpTo16(width);
        int encHeight = RoundUpTo16(height);

        if (encWidth != width || encHeight != height)
        {
            Console.Error.WriteLine($"Rounded {width}x{height} up to {encWidth}x{encHeight} (VP8 needs multiples of 16).");
        }

        byte[][] ring = GenerateRing(encWidth, encHeight);
        using var encoder = new VP8Codec();

        var result = RunEncoderLoop("vp8.net", encoder, encWidth, encHeight, ring, fps, durationSeconds, codec, VideoCodecsEnum.VP8, logger, ct);
        return WriteResult(asJson, result, result.Success ? ExitCodes.Ok : ExitCodes.Failed);
    }

    /// <summary>
    /// Stage 2 (in-process): native codec via the SIPSorceryMedia.FFmpeg IVideoEncoder, which wraps
    /// FFmpeg through FFmpeg.AutoGen. Same measurement loop as the managed stage, so the two are a
    /// direct managed-vs-native comparison of the encoder the library can plug in.
    /// </summary>
    private static int RunNativeFfmpegEncode(int width, int height, int fps, int durationSeconds, string codec,
        VideoCodecsEnum codecEnum, string? ffmpegPath, FfmpegTuning tuning, bool asJson, ILogger logger, CancellationToken ct)
    {
        try
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL, ffmpegPath, logger);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson, Empty("ffmpeg", codec, width, height, fps,
                $"Could not initialise FFmpeg: {excp.Message}. Install the FFmpeg shared libraries (e.g. winget install ffmpeg) or pass --ffmpeg-path."),
                ExitCodes.TransportError);
        }

        // libvpx needs even dimensions; it pads to macroblock boundaries internally so no
        // multiple-of-16 rounding is required.
        int encWidth = RoundUpToEven(width);
        int encHeight = RoundUpToEven(height);

        if (encWidth != width || encHeight != height)
        {
            Console.Error.WriteLine($"Rounded {width}x{height} up to {encWidth}x{encHeight} (even dimensions required).");
        }

        byte[][] ring = GenerateRing(encWidth, encHeight);

        var result = EncodeWithFfmpeg(encWidth, encHeight, ring, fps, durationSeconds, codec, codecEnum, tuning, logger, ct);
        return WriteResult(asJson, result, result.Success ? ExitCodes.Ok : ExitCodes.Failed);
    }

    /// <summary>
    /// Encodes the supplied frame ring with the in-process FFmpeg encoder for one codec and returns
    /// the result. Assumes FFmpegInit.Initialise has already been called.
    /// </summary>
    private static VideoBenchResult EncodeWithFfmpeg(int encWidth, int encHeight, byte[][] ring, int fps, int durationSeconds,
        string codec, VideoCodecsEnum codecEnum, FfmpegTuning tuning, ILogger logger, CancellationToken ct)
    {
        // Map the tuning knobs onto the encoder's libvpx options (applied via av_opt_set). The
        // encoder already sets quality=realtime by default but never sets cpu-used, which is the main
        // reason its out-of-the-box throughput is below an equivalently tuned external ffmpeg.
        // deadline/cpu-used are libvpx (VP8/VP9) knobs; for H264/H265 the encoder applies its own
        // realtime defaults (fast preset + zerolatency tune), so leave them unset to avoid invalid options.
        var encoderOptions = new Dictionary<string, string>();
        bool isVpx = codecEnum is VideoCodecsEnum.VP8 or VideoCodecsEnum.VP9;
        if (isVpx)
        {
            if (!string.IsNullOrWhiteSpace(tuning.Deadline)) { encoderOptions["deadline"] = tuning.Deadline; }
            if (tuning.CpuUsed >= 0) { encoderOptions["cpu-used"] = tuning.CpuUsed.ToString(); }
        }
        else if (codecEnum == VideoCodecsEnum.AV1 && tuning.Av1Preset >= 0)
        {
            // Overrides the encoder's realtime default preset (applied after it via av_opt_set). The
            // "preset" option is understood by libsvtav1; other AV1 encoders ignore it with a warning.
            encoderOptions["preset"] = tuning.Av1Preset.ToString();
        }

        logger.LogDebug("FFmpeg encoder tuning for {Codec}: deadline={Deadline}, cpu-used={CpuUsed}, threads={Threads}.",
            codec, tuning.Deadline, tuning.CpuUsed, tuning.Threads);

        try
        {
            using var encoder = new FFmpegVideoEncoder(encoderOptions);
            if (tuning.Threads > 0)
            {
                encoder.SetThreadCount(tuning.Threads);
            }

            // FFmpeg's default AV1 encoder is libaom-av1, the slowest. Select a realtime-oriented one
            // (libsvtav1 by default); the encoder applies its own realtime preset for whichever is chosen.
            if (codecEnum == VideoCodecsEnum.AV1 && !string.IsNullOrWhiteSpace(tuning.Av1Encoder))
            {
                if (encoder.SetCodec(AVCodecID.AV_CODEC_ID_AV1, tuning.Av1Encoder))
                {
                    logger.LogDebug("Using AV1 encoder {Av1Encoder}.", tuning.Av1Encoder);
                }
                else
                {
                    Console.Error.WriteLine($"AV1 encoder \"{tuning.Av1Encoder}\" is not available in this FFmpeg build; falling back to the default AV1 encoder.");
                }
            }

            return RunEncoderLoop("ffmpeg", encoder, encWidth, encHeight, ring, fps, durationSeconds, codec, codecEnum, logger, ct);
        }
        catch (Exception excp)
        {
            return Empty("ffmpeg", codec, encWidth, encHeight, fps, $"FFmpeg encode failed: {excp.Message}");
        }
    }

    /// <summary>
    /// Extended mode: benchmarks every codec in <see cref="ALL_CODECS"/> with the in-process ffmpeg
    /// encoder. FFmpeg is initialised once and the same frame ring feeds every codec.
    /// </summary>
    private static int RunAllCodecs(int width, int height, int fps, int durationSeconds, string? ffmpegPath,
        FfmpegTuning tuning, bool asJson, ILogger logger, CancellationToken ct)
    {
        try
        {
            FFmpegInit.Initialise(FfmpegLogLevelEnum.AV_LOG_FATAL, ffmpegPath, logger);
        }
        catch (Exception excp)
        {
            return WriteResult(asJson, Empty("ffmpeg", "all", width, height, fps,
                $"Could not initialise FFmpeg: {excp.Message}. Install the FFmpeg shared libraries (e.g. winget install ffmpeg) or pass --ffmpeg-path."),
                ExitCodes.TransportError);
        }

        int encWidth = RoundUpToEven(width);
        int encHeight = RoundUpToEven(height);
        if (encWidth != width || encHeight != height)
        {
            Console.Error.WriteLine($"Rounded {width}x{height} up to {encWidth}x{encHeight} (even dimensions required).");
        }

        // The same raw frames feed every codec, so generate the ring once.
        byte[][] ring = GenerateRing(encWidth, encHeight);

        var results = new List<VideoBenchResult>();
        foreach (var (label, codecEnum) in ALL_CODECS)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            Console.Error.WriteLine($"Benchmarking {label} at {encWidth}x{encHeight}@{fps} for {durationSeconds}s ...");
            results.Add(EncodeWithFfmpeg(encWidth, encHeight, ring, fps, durationSeconds, label, codecEnum, tuning, logger, ct));
        }

        return WriteResults(asJson, results);
    }

    private static byte[][] GenerateRing(int encWidth, int encHeight)
    {
        // Pre-generate a ring of distinct frames so the encode loop has motion to work on without
        // paying frame-generation cost inside the measured loop.
        Console.Error.WriteLine($"Generating {RING_SIZE} test frames at {encWidth}x{encHeight} ...");
        return GenerateI420Ring(encWidth, encHeight, RING_SIZE);
    }

    /// <summary>
    /// The shared encode + packetise measurement loop used by every encoder stage. Encodes ring
    /// frames flat out via the supplied <see cref="IVideoEncoder"/>, packetises each result and
    /// reports the achieved frame rate against the target.
    /// </summary>
    private static VideoBenchResult RunEncoderLoop(string stage, IVideoEncoder encoder, int encWidth, int encHeight, byte[][] ring,
        int fps, int durationSeconds, string codec, VideoCodecsEnum codecEnum, ILogger logger, CancellationToken ct)
    {
        uint rtpDuration = VIDEO_CLOCK_RATE / (uint)fps;
        uint ssrc = 0x1234_5678;
        ushort seqnum = 0;
        uint timestamp = 0;

        int frames = 0;
        long totalPackets = 0;
        long rtpBytes = 0;
        long encodedBytes = 0;

        double sumMs = 0;
        double maxMs = 0;
        var samples = new List<double>(Math.Min(MAX_TIMING_SAMPLES, fps * durationSeconds * 2 + 1));

        logger.LogDebug("Stage \"{Stage}\" encode: {Width}x{Height}@{Fps} for {Duration}s.", stage, encWidth, encHeight, fps, durationSeconds);

        long durationMs = durationSeconds * 1000L;
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < durationMs && !ct.IsCancellationRequested)
        {
            byte[] raw = ring[frames % ring.Length];

            long start = Stopwatch.GetTimestamp();

            byte[]? encoded = encoder.EncodeVideo(encWidth, encHeight, raw, VideoPixelFormatsEnum.I420, codecEnum);
            int packets = (encoded != null && encoded.Length > 0)
                ? PacketiseFrame(encoded, timestamp, ssrc, ref seqnum, ref rtpBytes)
                : 0;

            double frameMs = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            sumMs += frameMs;
            if (frameMs > maxMs) { maxMs = frameMs; }
            if (samples.Count < MAX_TIMING_SAMPLES) { samples.Add(frameMs); }

            encodedBytes += encoded?.Length ?? 0;
            totalPackets += packets;
            timestamp += rtpDuration;
            frames++;
        }

        sw.Stop();

        double elapsedSec = sw.Elapsed.TotalSeconds;
        double achievedFps = frames / elapsedSec;
        double throughputMbps = encodedBytes * 8.0 / elapsedSec / 1_000_000.0;
        double avgMs = frames > 0 ? sumMs / frames : 0;
        double p95Ms = Percentile(samples, 95);
        int avgPackets = frames > 0 ? (int)Math.Round((double)totalPackets / frames) : 0;
        int avgEncodedSize = frames > 0 ? (int)(encodedBytes / frames) : 0;

        bool success = achievedFps >= fps && frames > 0;

        var result = new VideoBenchResult(success, stage, codec.ToLowerInvariant(), encWidth, encHeight, fps,
            frames, Math.Round(achievedFps, 1), Math.Round(avgMs, 4), Math.Round(p95Ms, 4), Math.Round(maxMs, 4),
            avgPackets, avgEncodedSize, Math.Round(throughputMbps, 2), sw.ElapsedMilliseconds,
            frames == 0 ? "The encoder produced no frames."
                        : success ? null : $"The {stage} encoder only reached {achievedFps:0} fps, below the {fps} fps target.");

        return result;
    }

    /// <summary>
    /// Stage 2 (piped): native codec via ffmpeg. Pipes synthetic raw I420 frames to ffmpeg's libvpx encoder
    /// (rate controlled, realtime deadline) flat out, reads the encoded IVF stream back, packetises
    /// it and measures the achieved frame rate. This is the ceiling with a production-grade native
    /// encoder, for comparison with the managed VP8 stage.
    /// </summary>
    private static int RunFfmpegEncode(int width, int height, int fps, int durationSeconds, int bitrate, string codec,
        VideoCodecsEnum codecEnum, FfmpegTuning tuning, bool asJson, ILogger logger, CancellationToken ct)
    {
        // This stage reads ffmpeg's IVF output, which carries VP8/VP9 (and AV1) but not H264/H265. Map
        // the codec to its libvpx encoder; H264/H265 need the in-process --encoder ffmpeg stage instead.
        string vcodec = codecEnum switch
        {
            VideoCodecsEnum.VP8 => "libvpx",
            VideoCodecsEnum.VP9 => "libvpx-vp9",
            _ => string.Empty
        };
        if (vcodec.Length == 0)
        {
            return WriteResult(asJson, Empty("ffmpeg", codec, width, height, fps,
                $"The ffmpeg-piped stage supports vp8 and vp9 only (IVF output). Use --encoder ffmpeg for {codec.ToLowerInvariant()}."),
                ExitCodes.InvalidArgument);
        }

        // libvpx needs even dimensions; it pads internally so no multiple-of-16 rounding is required
        // (unlike the managed stage), which lets ffmpeg encode true 1920x1080.
        int encWidth = RoundUpToEven(width);
        int encHeight = RoundUpToEven(height);
        int frameSize = encWidth * encHeight * 3 / 2;

        const int RING_SIZE = 16;
        Console.Error.WriteLine($"Generating {RING_SIZE} test frames at {encWidth}x{encHeight} ...");
        byte[][] ring = GenerateI420Ring(encWidth, encHeight, RING_SIZE);

        string tuneArgs = $"-deadline {(string.IsNullOrWhiteSpace(tuning.Deadline) ? "realtime" : tuning.Deadline)}";
        if (tuning.CpuUsed >= 0) { tuneArgs += $" -cpu-used {tuning.CpuUsed}"; }
        if (tuning.Threads > 0) { tuneArgs += $" -threads {tuning.Threads}"; }
        // row-mt enables VP9's tile-row multi-threading, which is what lets it keep up at high rates.
        if (codecEnum == VideoCodecsEnum.VP9) { tuneArgs += " -row-mt 1"; }

        var startInfo = new ProcessStartInfo("ffmpeg")
        {
            // Flat out (no -re): the pipe back-pressure paces feeding to ffmpeg's encode speed, so the
            // rate frames flow through is the encoder's throughput.
            Arguments = $"-hide_banner -loglevel error -f rawvideo -pix_fmt yuv420p -s {encWidth}x{encHeight} -r {fps} -i - " +
                        $"-c:v {vcodec} {tuneArgs} -b:v {bitrate} -f ivf -",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        Process? proc;
        try
        {
            proc = Process.Start(startInfo);
            if (proc == null)
            {
                throw new ApplicationException("ffmpeg did not start.");
            }
        }
        catch (Exception excp)
        {
            return WriteResult(asJson, Empty("ffmpeg", codec, encWidth, encHeight, fps,
                $"Could not start ffmpeg: {excp.Message}. Install ffmpeg (with libvpx) and ensure it is on the PATH."),
                ExitCodes.TransportError);
        }

        // Drain stderr so the pipe never blocks ffmpeg.
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                logger.LogDebug("ffmpeg: {Line}", line);
            }
        });

        // Feed raw frames flat out for the requested duration, then close stdin.
        using var feedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        feedCts.CancelAfter(TimeSpan.FromSeconds(durationSeconds));
        var stdin = proc.StandardInput.BaseStream;
        var writer = Task.Run(() =>
        {
            try
            {
                int i = 0;
                while (!feedCts.IsCancellationRequested)
                {
                    stdin.Write(ring[i % RING_SIZE], 0, frameSize);
                    i++;
                }
            }
            catch (IOException) { /* ffmpeg closed the pipe (e.g. it exited). */ }
            finally { try { stdin.Close(); } catch { /* already gone */ } }
        });

        // Read the encoded IVF stream: 32-byte file header, then per-frame 12-byte headers.
        var stdout = proc.StandardOutput.BaseStream;
        uint rtpDuration = VIDEO_CLOCK_RATE / (uint)fps;
        uint ssrc = 0x1234_5678;
        ushort seqnum = 0;
        uint timestamp = 0;

        int frames = 0;
        long totalPackets = 0;
        long rtpBytes = 0;
        long encodedBytes = 0;
        double sumMs = 0;
        double maxMs = 0;
        var samples = new List<double>(Math.Min(MAX_TIMING_SAMPLES, fps * durationSeconds * 2 + 1));

        long lastTs = 0;
        var sw = new Stopwatch();

        var fileHeader = new byte[32];
        if (ReadFully(stdout, fileHeader, 32))
        {
            var frameHeader = new byte[12];
            while (ReadFully(stdout, frameHeader, 12))
            {
                int size = frameHeader[0] | (frameHeader[1] << 8) | (frameHeader[2] << 16) | (frameHeader[3] << 24);
                if (size <= 0 || size > 50_000_000)
                {
                    break;
                }

                var frame = new byte[size];
                if (!ReadFully(stdout, frame, size))
                {
                    break;
                }

                // Start the clock on the first frame so ffmpeg's process/encoder startup is excluded;
                // subsequent inter-frame intervals are the steady-state encode cadence.
                if (!sw.IsRunning)
                {
                    sw.Start();
                    lastTs = Stopwatch.GetTimestamp();
                }
                else
                {
                    long nowTs = Stopwatch.GetTimestamp();
                    double ms = (nowTs - lastTs) * 1000.0 / Stopwatch.Frequency;
                    lastTs = nowTs;
                    sumMs += ms;
                    if (ms > maxMs) { maxMs = ms; }
                    if (samples.Count < MAX_TIMING_SAMPLES) { samples.Add(ms); }
                }

                totalPackets += PacketiseFrame(frame, timestamp, ssrc, ref seqnum, ref rtpBytes);
                encodedBytes += size;
                timestamp += rtpDuration;
                frames++;
            }
        }

        sw.Stop();

        try { writer.Wait(2000); } catch { /* best effort */ }
        try { if (!proc.HasExited) { proc.WaitForExit(2000); } } catch { }
        try { if (!proc.HasExited) { proc.Kill(); } } catch { }
        proc.Dispose();

        if (frames == 0)
        {
            return WriteResult(asJson, Empty("ffmpeg", codec, encWidth, encHeight, fps,
                "ffmpeg produced no encoded frames. Check ffmpeg is installed and built with libvpx (run with --verbose to see ffmpeg's output)."),
                ExitCodes.Failed);
        }

        // The throughput window runs from the first to the last frame (startup excluded), so it
        // covers measuredFrames = frames - 1 intervals.
        int measuredFrames = Math.Max(1, frames - 1);
        double elapsedSec = sw.Elapsed.TotalSeconds;
        double achievedFps = elapsedSec > 0 ? measuredFrames / elapsedSec : 0;
        double throughputMbps = encodedBytes * 8.0 / (elapsedSec > 0 ? elapsedSec : 1) / 1_000_000.0;
        double avgMs = measuredFrames > 0 ? sumMs / measuredFrames : 0;
        double p95Ms = Percentile(samples, 95);
        int avgPackets = (int)Math.Round((double)totalPackets / frames);
        int avgEncodedSize = (int)(encodedBytes / frames);

        bool success = achievedFps >= fps;

        var result = new VideoBenchResult(success, "ffmpeg", codec.ToLowerInvariant(), encWidth, encHeight, fps,
            frames, Math.Round(achievedFps, 1), Math.Round(avgMs, 4), Math.Round(p95Ms, 4), Math.Round(maxMs, 4),
            avgPackets, avgEncodedSize, Math.Round(throughputMbps, 2), sw.ElapsedMilliseconds,
            success ? null : $"ffmpeg encode only reached {achievedFps:0} fps, below the {fps} fps target.");

        return WriteResult(asJson, result, success ? ExitCodes.Ok : ExitCodes.Failed);
    }

    private static bool ReadFully(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0)
            {
                return false;
            }
            read += n;
        }
        return true;
    }

    private static int RoundUpTo16(int value) => (value + 15) & ~15;

    private static int RoundUpToEven(int value) => (value + 1) & ~1;

    /// <summary>
    /// Maps a --codec string to the abstraction's codec enum. vp8, vp9, h264 and h265 (alias hevc) are
    /// the codecs the SIPSorceryMedia.FFmpeg in-process encoder can produce.
    /// </summary>
    private static bool TryParseCodec(string codec, out VideoCodecsEnum codecEnum, out string? error)
    {
        error = null;
        switch (codec?.ToLowerInvariant())
        {
            case "vp8": codecEnum = VideoCodecsEnum.VP8; return true;
            case "vp9": codecEnum = VideoCodecsEnum.VP9; return true;
            case "h264": codecEnum = VideoCodecsEnum.H264; return true;
            case "h265":
            case "hevc": codecEnum = VideoCodecsEnum.H265; return true;
            case "av1": codecEnum = VideoCodecsEnum.AV1; return true;
            default:
                codecEnum = VideoCodecsEnum.Unknown;
                error = $"Unsupported --codec \"{codec}\". Expected vp8, vp9, h264, h265 or av1.";
                return false;
        }
    }

    /// <summary>
    /// Generates a ring of distinct I420 frames with a shifting textured pattern, so the encoder
    /// sees inter-frame motion and moderate spatial detail (a flat frame would encode unrealistically
    /// fast). Each buffer is width*height*3/2 bytes as VP8Codec.EncodeVideo requires.
    /// </summary>
    private static byte[][] GenerateI420Ring(int width, int height, int count)
    {
        int ySize = width * height;
        int cSize = (width / 2) * (height / 2);
        var ring = new byte[count][];

        for (int f = 0; f < count; f++)
        {
            var buf = new byte[ySize + 2 * cSize];
            int shift = f * 8;

            for (int y = 0; y < height; y++)
            {
                int rowBase = y * width;
                for (int x = 0; x < width; x++)
                {
                    // Diagonal gradient plus a block-XOR texture, both shifting per frame for motion.
                    buf[rowBase + x] = (byte)((x + y + shift) ^ ((x >> 3) + (y >> 3)));
                }
            }

            for (int y = 0; y < height / 2; y++)
            {
                int uRow = ySize + y * (width / 2);
                int vRow = ySize + cSize + y * (width / 2);
                for (int x = 0; x < width / 2; x++)
                {
                    buf[uRow + x] = (byte)(128 + (((x + shift) & 0x3F) - 32));
                    buf[vRow + x] = (byte)(128 + (((y - shift) & 0x3F) - 32));
                }
            }

            ring[f] = buf;
        }

        return ring;
    }

    /// <summary>
    /// Fragments and serialises one encoded frame into RTP packets the same way VideoStream.SendVp8Frame
    /// does (RTP_MAX_PAYLOAD chunks with a one-byte payload-descriptor stand-in), returning the packet
    /// count. The serialised bytes are added to <paramref name="totalBytes"/> and discarded. The measured
    /// cost is the RTP serialisation, which is codec independent, so this is reused for every codec — a
    /// codec-specific payload descriptor (VP9/H264/H265) would not change the per-packet serialisation cost.
    /// </summary>
    private static int PacketiseFrame(byte[] frame, uint timestamp, uint ssrc, ref ushort seqnum, ref long totalBytes)
    {
        int packets = 0;

        for (int index = 0; index * RTP_MAX_PAYLOAD < frame.Length; index++)
        {
            int offset = index * RTP_MAX_PAYLOAD;
            int payloadLength = (offset + RTP_MAX_PAYLOAD < frame.Length) ? RTP_MAX_PAYLOAD : frame.Length - offset;

            // VP8 payload descriptor: 0x10 (S bit, start of partition) on the first fragment, 0x00 after.
            byte[] vp8Header = (index == 0) ? new byte[] { 0x10 } : new byte[] { 0x00 };
            byte[] payload = new byte[payloadLength + vp8Header.Length];
            Buffer.BlockCopy(vp8Header, 0, payload, 0, vp8Header.Length);
            Buffer.BlockCopy(frame, offset, payload, vp8Header.Length, payloadLength);

            var rtpPacket = new RTPPacket(payload);
            rtpPacket.Header.SyncSource = ssrc;
            rtpPacket.Header.SequenceNumber = seqnum++;
            rtpPacket.Header.Timestamp = timestamp;
            rtpPacket.Header.MarkerBit = (offset + payloadLength >= frame.Length) ? 1 : 0;
            rtpPacket.Header.PayloadType = VP8_PAYLOAD_ID;

            // The real RTP serialisation is the work being measured.
            byte[] bytes = rtpPacket.GetBytes();
            totalBytes += bytes.Length;
            packets++;
        }

        return packets;
    }

    private static double Percentile(List<double> values, int percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = new List<double>(values);
        sorted.Sort();
        int rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    private static VideoBenchResult Empty(string encoder, string codec, int width, int height, int fps, string error) =>
        new(false, encoder?.ToLowerInvariant() ?? string.Empty, codec?.ToLowerInvariant() ?? string.Empty,
            width, height, fps, 0, 0, 0, 0, 0, 0, 0, 0, 0, error);

    private static int WriteResult(bool asJson, VideoBenchResult result, int exitCode)
    {
        if (asJson)
        {
            WriteJson(result);
        }
        else
        {
            WriteResultHuman(result);
        }

        return exitCode;
    }

    /// <summary>
    /// Writes a set of results (the --all extended mode): a JSON array, or one human readable line
    /// per codec. The exit code is Ok only if every codec met the target frame rate.
    /// </summary>
    private static int WriteResults(bool asJson, List<VideoBenchResult> results)
    {
        if (asJson)
        {
            WriteJson(results);
        }
        else
        {
            foreach (var result in results)
            {
                WriteResultHuman(result);
            }
        }

        bool allOk = results.Count > 0 && results.TrueForAll(r => r.Success);
        return allOk ? ExitCodes.Ok : ExitCodes.Failed;
    }

    private static void WriteResultHuman(VideoBenchResult result)
    {
        if (result.FramesProcessed > 0)
        {
            string verdict = result.Success ? "PASS" : "BELOW TARGET";
            Console.WriteLine($"Stage \"{result.Stage}\" {result.Codec} {result.Width}x{result.Height}: " +
                $"{result.AchievedFps:0} fps achieved vs {result.TargetFps} target — {verdict}. " +
                $"Packetise {result.PerFrameMsAvg:0.###}/{result.PerFrameMsP95:0.###}/{result.PerFrameMsMax:0.###} ms avg/p95/max, " +
                $"{result.PacketsPerFrame} pkts/frame ({result.FrameBytes} bytes), {result.ThroughputMbps} Mbps over {result.DurationMs}ms.");
        }
        else
        {
            Console.Error.WriteLine($"Video bench {result.Codec} failed: {result.Error}");
        }
    }
}
