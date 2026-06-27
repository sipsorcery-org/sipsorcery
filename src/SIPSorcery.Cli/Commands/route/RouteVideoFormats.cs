//-----------------------------------------------------------------------------
// Filename: RouteVideoFormats.cs
//
// Description: Single source of truth for the video formats the route graph
// negotiates. Both the full list and the single H264 format come from the
// SIPSorceryMedia.FFmpeg supported-format list (Helper.GetSupportedVideoFormats)
// so the codec set and, crucially, the H264 fmtp (profile-level-id) are defined
// in exactly one place rather than being re-typed at each call site.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.Cli.Commands.Route;

internal static class RouteVideoFormats
{
    /// <summary>
    /// Every codec the library can negotiate, offered by receive-only edges (e.g. WHEP) so the server
    /// can match whatever the publisher is sending.
    /// </summary>
    public static List<VideoFormat> All() => Helper.GetSupportedVideoFormats();

    /// <summary>
    /// The single H264 format, derived from <see cref="All"/> via RestrictFormats so its
    /// profile-level-id stays in lockstep with the shared list. Used by send-only / single-codec edges
    /// (WHIP sink, audio scope, test pattern).
    /// </summary>
    public static VideoFormat H264 { get; } = Restrict(VideoCodecsEnum.H264);

    private static VideoFormat Restrict(VideoCodecsEnum codec)
    {
        var manager = new MediaFormatManager<VideoFormat>(Helper.GetSupportedVideoFormats());
        manager.RestrictFormats(f => f.Codec == codec);
        return manager.GetSourceFormats().Single();
    }
}
