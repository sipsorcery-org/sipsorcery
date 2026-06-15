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
}
