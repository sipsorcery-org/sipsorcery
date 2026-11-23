//-----------------------------------------------------------------------------
// Filename: CodecSelection.cs
//
// Description: Resolves an --audio-codec / --video-codec name to the single
// AudioFormat / VideoFormat the library knows it by, so a test run can pin one
// codec and find out whether that codec works end to end.
//
// Exactly one codec per media type: no lists, no priority order, no fallback. A
// pinned codec that cannot be negotiated has to fail the run - quietly falling
// back to another codec would make a negative result meaningless - so an
// unrecognised name is an argument error listing the valid values rather than a
// value that drops through to default negotiation.
//
// The video names and formats come from SIPSorceryMedia.FFmpeg's canonical
// supported-format list, filtered with the library's own
// MediaFormatManager.RestrictFormats (the same hook the media sources use). A
// name therefore resolves to the exact format - payload ID and fmtp, e.g. the
// H264 profile-level-id - that the edges already negotiate, rather than a
// re-typed copy that could drift from it.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SIPSorcery.Cli.Common;

public static class CodecSelection
{
    public const string AUDIO_OPTION = "--audio-codec";
    public const string VIDEO_OPTION = "--video-codec";

    /// <summary>
    /// The audio codec names accepted. Opus plus the two G.711 flavours: the codecs the CLI's audio
    /// paths can actually carry end to end, so a pinned one that negotiates also works.
    /// </summary>
    public static IReadOnlyList<string> AudioNames { get; } = new[] { "opus", "pcmu", "pcma" };

    /// <summary>The video codec names accepted, taken from the library's supported-format list.</summary>
    public static IReadOnlyList<string> VideoNames { get; } =
        Helper.GetSupportedVideoFormats().Select(f => f.Codec.ToString().ToLowerInvariant()).ToList();

    /// <summary>The valid values, as the --help text and the error messages word them.</summary>
    public static string AudioNamesList => string.Join(", ", AudioNames);

    public static string VideoNamesList => string.Join(", ", VideoNames);

    /// <summary>
    /// Resolves a single audio codec name (case-insensitively) to the format the CLI offers for it.
    /// </summary>
    /// <param name="name">The codec name as typed, e.g. "OPUS".</param>
    /// <param name="optionName">The option the name came from, for the error message.</param>
    public static bool TryResolveAudio(string? name, string optionName, out AudioFormat format, out string? error)
    {
        error = null;

        switch (Normalise(name))
        {
            case "opus":
                format = AudioCommonlyUsedFormats.OpusWebRTC;
                return true;
            case "pcmu":
                format = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMU);
                return true;
            case "pcma":
                format = new AudioFormat(SDPWellKnownMediaFormatsEnum.PCMA);
                return true;
            default:
                format = AudioFormat.Empty;
                error = UnknownCodecError(optionName, name, AudioNamesList);
                return false;
        }
    }

    /// <summary>
    /// Resolves a single video codec name (case-insensitively) to the library's format for it, via
    /// RestrictFormats so the payload ID and fmtp stay in lockstep with the shared supported list.
    /// </summary>
    /// <param name="name">The codec name as typed, e.g. "H264".</param>
    /// <param name="optionName">The option the name came from, for the error message.</param>
    public static bool TryResolveVideo(string? name, string optionName, out VideoFormat format, out string? error)
    {
        error = null;
        string normalised = Normalise(name);

        var manager = new MediaFormatManager<VideoFormat>(Helper.GetSupportedVideoFormats());
        manager.RestrictFormats(f => string.Equals(f.Codec.ToString(), normalised, StringComparison.OrdinalIgnoreCase));
        var matches = manager.GetSourceFormats();

        if (matches.Count != 1)
        {
            format = VideoFormat.Empty;
            error = UnknownCodecError(optionName, name, VideoNamesList);
            return false;
        }

        format = matches[0];
        return true;
    }

    private static string Normalise(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant();

    private static string UnknownCodecError(string optionName, string? name, string validNames) =>
        string.IsNullOrWhiteSpace(name)
            ? $"{optionName} needs a codec name. It takes exactly one of: {validNames}."
            : $"Unknown {optionName} '{name.Trim()}'. It takes exactly one of: {validNames}.";
}
