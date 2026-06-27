//-----------------------------------------------------------------------------
// Filename: RouteAudioTests.cs
//
// Description: Unit tests for RouteAudio.TryResolveCodec - the --audio-codec
// resolution that decides what a route generator produces and a sink offers.
// Opus is the default (the universal WebRTC codec); pcmu/pcma are overrides.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 27 Jun 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorcery.Cli.Commands.Route;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class RouteAudioTests
{
    [Theory]
    [InlineData("opus", AudioCodecsEnum.OPUS)]
    [InlineData("OPUS", AudioCodecsEnum.OPUS)]
    [InlineData("pcmu", AudioCodecsEnum.PCMU)]
    [InlineData("pcma", AudioCodecsEnum.PCMA)]
    public void Resolves_known_codecs(string name, AudioCodecsEnum expected)
    {
        bool ok = RouteAudio.TryResolveCodec(name, out var format, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, format.Codec);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Defaults_to_opus_when_unset(string? name)
    {
        bool ok = RouteAudio.TryResolveCodec(name, out var format, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(AudioCodecsEnum.OPUS, format.Codec);
    }

    [Fact]
    public void Rejects_unknown_codec_with_a_helpful_message()
    {
        bool ok = RouteAudio.TryResolveCodec("g729", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("opus (default)", error);
    }
}
