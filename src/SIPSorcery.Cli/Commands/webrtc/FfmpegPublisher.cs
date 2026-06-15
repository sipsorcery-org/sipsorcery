//-----------------------------------------------------------------------------
// Filename: FfmpegPublisher.cs
//
// Description: Builds and runs an ffmpeg WHIP publish command from high level
// settings (resolution preset, frame rate, codec, ...). Used by the
// "webrtc whip-server --publish" self test to feed a test pattern to the
// server's own listener with a single command, removing the need to run a
// separate publisher process. ffmpeg is the publish engine deliberately: a frame
// rate / throughput test wants a fast, configurable encoder (libx264, any
// resolution) rather than the library's managed VP8 encoder.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 14 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Diagnostics;

namespace SIPSorcery.Cli.Commands;

public static class FfmpegPublisher
{
    /// <summary>The commonly adjusted publish controls. Size, when set, overrides Preset.</summary>
    public sealed record Settings(string Preset, string? Size, int Fps, string Codec, string? Bitrate, bool Audio);

    /// <summary>A running ffmpeg publisher process, stopped via <see cref="Stop"/>.</summary>
    public sealed class Publisher
    {
        private readonly Process _ffmpeg;

        internal Publisher(Process ffmpeg) => _ffmpeg = ffmpeg;

        public bool HasExited => _ffmpeg.HasExited;

        public int ExitCode => _ffmpeg.ExitCode;

        /// <summary>Completes when the ffmpeg process exits (used to fail fast if it dies early).</summary>
        public Task WaitForExitAsync(CancellationToken ct) => _ffmpeg.WaitForExitAsync(ct);

        public void Stop()
        {
            try { if (!_ffmpeg.HasExited) { _ffmpeg.Kill(entireProcessTree: true); } } catch { /* already gone */ }
            try { _ffmpeg.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Validates the publish settings (codec, frame rate and resolution) without starting anything.
    /// </summary>
    public static bool TryValidate(Settings settings, out string? error)
    {
        error = null;

        string codec = settings.Codec.ToLowerInvariant();
        if (codec != "h264" && codec != "vp8")
        {
            error = $"Unknown --codec \"{settings.Codec}\". Expected h264 or vp8.";
            return false;
        }

        if (settings.Fps < 1)
        {
            error = "--fps must be at least 1.";
            return false;
        }

        return TryResolveSize(settings.Size, settings.Preset, out _, out _, out error);
    }

    /// <summary>
    /// Starts an ffmpeg WHIP publisher for the given URL, returning a handle, or null and an error if
    /// the settings are invalid or ffmpeg could not be started. duration 0 runs until <see cref="Publisher.Stop"/>.
    /// The generated command is returned in <paramref name="command"/> for display.
    /// </summary>
    public static Publisher? Start(string url, Settings settings, string? token, int duration, out string command, out string? error)
    {
        command = string.Empty;

        if (!TryValidate(settings, out error))
        {
            return null;
        }

        TryResolveSize(settings.Size, settings.Preset, out int width, out int height, out _);
        string codec = settings.Codec.ToLowerInvariant();

        var args = BuildFfmpegArgs(url, width, height, settings.Fps, codec, settings.Bitrate, settings.Fps * 2, "testsrc", settings.Audio, token, duration);
        command = "ffmpeg " + string.Join(" ", args.Select(QuoteForDisplay));

        // ffmpeg writes only to the network (the WHIP muxer) and its logs/progress to stderr, so the
        // inherited stdout stays clean for the caller's result.
        var startInfo = new ProcessStartInfo("ffmpeg") { UseShellExecute = false };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            var ffmpeg = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg did not start.");
            error = null;
            return new Publisher(ffmpeg);
        }
        catch (Exception excp)
        {
            error = $"Could not start ffmpeg: {excp.Message}. Install ffmpeg and ensure it is on the PATH.";
            return null;
        }
    }

    private static List<string> BuildFfmpegArgs(string url, int width, int height, int fps, string codec,
        string? bitrate, int gop, string pattern, bool audio, string? token, int duration)
    {
        var args = new List<string> { "-hide_banner", "-re", "-f", "lavfi", "-i", $"{pattern}=size={width}x{height}:rate={fps}" };

        if (audio)
        {
            args.AddRange(["-f", "lavfi", "-i", "sine=frequency=440"]);
        }

        args.AddRange(["-pix_fmt", "yuv420p"]);

        if (codec == "h264")
        {
            args.AddRange(["-c:v", "libx264", "-profile:v", "baseline"]);
        }
        else
        {
            args.AddRange(["-c:v", "libvpx", "-deadline", "realtime", "-cpu-used", "5"]);
        }

        args.AddRange(["-r", fps.ToString(), "-g", gop.ToString()]);

        // libvpx produces near unusable quality without an explicit target bitrate, so default it.
        string? effectiveBitrate = bitrate ?? (codec == "vp8" ? "2M" : null);
        if (!string.IsNullOrWhiteSpace(effectiveBitrate))
        {
            args.AddRange(["-b:v", effectiveBitrate]);
        }

        if (audio)
        {
            args.AddRange(["-c:a", "libopus", "-ar", "48000", "-ac", "2"]);
        }

        if (duration > 0)
        {
            args.AddRange(["-t", duration.ToString()]);
        }

        args.AddRange(["-f", "whip"]);

        if (!string.IsNullOrWhiteSpace(token))
        {
            args.AddRange(["-authorization", token]);
        }

        args.Add(url);

        return args;
    }

    private static bool TryResolveSize(string? size, string preset, out int width, out int height, out string? error)
    {
        error = null;
        width = height = 0;

        if (!string.IsNullOrWhiteSpace(size))
        {
            string[] parts = size.ToLowerInvariant().Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) && width > 0 && height > 0)
            {
                return true;
            }

            error = $"Could not parse --size \"{size}\". Expected WxH, e.g. 1280x720.";
            return false;
        }

        return VideoPresets.TryResolve(preset, out width, out height, out error);
    }

    /// <summary>
    /// Quotes an argument for the displayed command so it is safe to copy and paste. Only arguments
    /// with whitespace or shell significant characters (e.g. a URL query string) are quoted.
    /// </summary>
    private static string QuoteForDisplay(string arg)
    {
        if (arg.Length > 0 && arg.IndexOfAny([' ', '\t', '?', '&', '"', '\'', ';', '|', '<', '>']) < 0)
        {
            return arg;
        }

        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }
}
