//-----------------------------------------------------------------------------
// Filename: VideoPresets.cs
//
// Description: Shared resolution presets (360p, 480p, ...) used by the video
// related verbs (webrtc whip-server --publish, webrtc video-bench) so they
// expose the same named sizes and cannot drift apart.
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

namespace SIPSorcery.Cli.Commands;

public static class VideoPresets
{
    /// <summary>The accepted preset names, for help text.</summary>
    public const string Names = "360p, 480p, 720p, 1080p, 1440p or 4k";

    /// <summary>
    /// Resolves a resolution preset name (case-insensitive) to its pixel dimensions.
    /// </summary>
    public static bool TryResolve(string preset, out int width, out int height, out string? error)
    {
        error = null;

        (width, height) = preset?.ToLowerInvariant() switch
        {
            "360p" => (640, 360),
            "480p" => (640, 480),
            "720p" => (1280, 720),
            "1080p" => (1920, 1080),
            "1440p" => (2560, 1440),
            "2160p" or "4k" => (3840, 2160),
            _ => (0, 0)
        };

        if (width == 0)
        {
            error = $"Unknown preset \"{preset}\". Expected {Names}.";
            return false;
        }

        return true;
    }

    /// <summary>The frame dimensions accepted by <see cref="TryResolveSize"/>, for help and error text.</summary>
    public const int MIN_DIMENSION = 16;
    public const int MAX_WIDTH = 7680;
    public const int MAX_HEIGHT = 4320;

    /// <summary>
    /// Resolves an explicit "WxH" size, falling back to a preset name when no size was given. Shared
    /// by the verbs that take both, so --size and --preset mean the same thing everywhere.
    /// </summary>
    /// <param name="size">An explicit size as WxH, or null/blank to use <paramref name="preset"/>.</param>
    /// <param name="preset">The preset name to fall back on.</param>
    /// <param name="sizeOptionName">The option the size came from, for the error message.</param>
    public static bool TryResolveSize(string? size, string preset, out int width, out int height,
        out string? error, string sizeOptionName = "--size")
    {
        width = 0;
        height = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(size))
        {
            return TryResolve(preset, out width, out height, out error);
        }

        var parts = size.Split('x', 'X');
        if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height) ||
            width < MIN_DIMENSION || height < MIN_DIMENSION || width > MAX_WIDTH || height > MAX_HEIGHT)
        {
            width = 0;
            height = 0;
            error = $"Could not parse {sizeOptionName} \"{size}\" as WxH in the range " +
                    $"{MIN_DIMENSION}x{MIN_DIMENSION} to {MAX_WIDTH}x{MAX_HEIGHT}, e.g. 1280x720.";
            return false;
        }

        return true;
    }
}
