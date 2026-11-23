//-----------------------------------------------------------------------------
// Filename: CodecSelectionTests.cs
//
// Description: Unit tests for CodecSelection - the --audio-codec / --video-codec
// name to format resolution shared by the route and webrtc verbs. Each flag
// takes exactly one codec name, matched case-insensitively, and an unrecognised
// name has to be an error listing the valid values rather than a value that
// drops through to default negotiation.
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

using SIPSorcery.Cli.Common;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class CodecSelectionTests
{
    [Theory]
    [InlineData("opus", AudioCodecsEnum.OPUS)]
    [InlineData("OPUS", AudioCodecsEnum.OPUS)]
    [InlineData("  Opus ", AudioCodecsEnum.OPUS)]
    [InlineData("pcmu", AudioCodecsEnum.PCMU)]
    [InlineData("PCMU", AudioCodecsEnum.PCMU)]
    [InlineData("pcma", AudioCodecsEnum.PCMA)]
    public void Resolves_audio_codecs_case_insensitively(string name, AudioCodecsEnum expected)
    {
        bool ok = CodecSelection.TryResolveAudio(name, CodecSelection.AUDIO_OPTION, out var format, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, format.Codec);
    }

    [Theory]
    [InlineData("vp8", VideoCodecsEnum.VP8)]
    [InlineData("VP8", VideoCodecsEnum.VP8)]
    [InlineData("vp9", VideoCodecsEnum.VP9)]
    [InlineData("h264", VideoCodecsEnum.H264)]
    [InlineData("H264", VideoCodecsEnum.H264)]
    [InlineData(" h265 ", VideoCodecsEnum.H265)]
    [InlineData("av1", VideoCodecsEnum.AV1)]
    public void Resolves_video_codecs_case_insensitively(string name, VideoCodecsEnum expected)
    {
        bool ok = CodecSelection.TryResolveVideo(name, CodecSelection.VIDEO_OPTION, out var format, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, format.Codec);
    }

    /// <summary>
    /// The resolved format has to be the library's own, fmtp included: an H264 answer without the
    /// profile-level-id is dropped by strict matchers, so a re-typed format would break the very
    /// endpoints a forced codec is used to test.
    /// </summary>
    [Fact]
    public void Resolved_video_format_carries_the_librarys_fmtp()
    {
        Assert.True(CodecSelection.TryResolveVideo("h264", CodecSelection.VIDEO_OPTION, out var format, out _));

        Assert.Equal("packetization-mode=1;profile-level-id=42e01f", format.Parameters);
    }

    [Theory]
    [InlineData("g729")]
    [InlineData("h264")]
    [InlineData("vp8")]
    public void Rejects_an_unknown_audio_codec_listing_the_valid_values(string name)
    {
        bool ok = CodecSelection.TryResolveAudio(name, CodecSelection.AUDIO_OPTION, out var format, out var error);

        Assert.False(ok);
        Assert.True(format.IsEmpty());
        Assert.NotNull(error);
        Assert.Contains(CodecSelection.AUDIO_OPTION, error);
        Assert.Contains(name, error);
        Assert.Contains("exactly one of: opus, pcmu, pcma", error);
    }

    [Theory]
    [InlineData("vp10")]
    [InlineData("opus")]
    [InlineData("h.264")]
    [InlineData("jpeg")]
    public void Rejects_an_unknown_video_codec_listing_the_valid_values(string name)
    {
        bool ok = CodecSelection.TryResolveVideo(name, CodecSelection.VIDEO_OPTION, out var format, out var error);

        Assert.False(ok);
        Assert.True(format.IsEmpty());
        Assert.NotNull(error);
        Assert.Contains(CodecSelection.VIDEO_OPTION, error);
        Assert.Contains(name, error);
        Assert.Contains("exactly one of: " + CodecSelection.VideoNamesList, error);
    }

    /// <summary>
    /// A comma separated list is exactly the shape someone reaches for out of habit, and taking the
    /// first of it would quietly negotiate a codec the run was not asked to prove. It has to fail.
    /// </summary>
    [Theory]
    [InlineData("h264,vp8")]
    [InlineData("vp8 vp9")]
    public void Rejects_more_than_one_video_codec(string name)
    {
        bool ok = CodecSelection.TryResolveVideo(name, CodecSelection.VIDEO_OPTION, out _, out var error);

        Assert.False(ok);
        Assert.Contains("exactly one of", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_codec_name(string? name)
    {
        Assert.False(CodecSelection.TryResolveAudio(name, CodecSelection.AUDIO_OPTION, out _, out var audioError));
        Assert.Contains("needs a codec name", audioError);

        Assert.False(CodecSelection.TryResolveVideo(name, CodecSelection.VIDEO_OPTION, out _, out var videoError));
        Assert.Contains("needs a codec name", videoError);
    }

    /// <summary>The names the --help text lists have to be the names that resolve.</summary>
    [Fact]
    public void Every_listed_name_resolves()
    {
        foreach (string name in CodecSelection.AudioNames)
        {
            Assert.True(CodecSelection.TryResolveAudio(name, CodecSelection.AUDIO_OPTION, out _, out _), name);
        }

        foreach (string name in CodecSelection.VideoNames)
        {
            Assert.True(CodecSelection.TryResolveVideo(name, CodecSelection.VIDEO_OPTION, out _, out _), name);
        }
    }
}
