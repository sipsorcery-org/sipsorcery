//-----------------------------------------------------------------------------
// Filename: RouteVideoTests.cs
//
// Description: Unit tests for RouteVideo.TryResolveCodec - the route verb's
// --video-codec resolution - and for the EdgeFactory wiring that turns an
// unrecognised name into an edge error before any transport is brought up.
// Unlike --audio-codec there is no default: left out, each edge keeps its own
// (H264 when sending, every supported codec when receiving).
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

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Cli.Commands.Route;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class RouteVideoTests
{
    private static readonly ILogger Log = NullLogger.Instance;

    [Theory]
    [InlineData("vp8", VideoCodecsEnum.VP8)]
    [InlineData("VP9", VideoCodecsEnum.VP9)]
    [InlineData("h264", VideoCodecsEnum.H264)]
    [InlineData("h265", VideoCodecsEnum.H265)]
    [InlineData("Av1", VideoCodecsEnum.AV1)]
    public void Resolves_known_codecs(string name, VideoCodecsEnum expected)
    {
        bool ok = RouteVideo.TryResolveCodec(name, out var format, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, format.Codec);
    }

    [Fact]
    public void Rejects_an_unknown_codec_with_a_helpful_message()
    {
        bool ok = RouteVideo.TryResolveCodec("theora", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("--video-codec", error);
        Assert.Contains("exactly one of", error);
    }

    /// <summary>
    /// A send-only edge resolves the codec while its node is being built, so a typo fails as an edge
    /// error before a connection is attempted - not silently as H264 halfway through a test run.
    /// </summary>
    [Fact]
    public void An_unknown_codec_fails_a_send_only_edge_before_it_connects()
    {
        var ex = Assert.Throws<EdgeException>(() =>
            EdgeFactory.CreateSink("whip:http://localhost:9999/whip", new EdgeOptions(30, null, 30, VideoCodec: "nope"), Log));

        Assert.Contains("--video-codec", ex.Message);
    }

    /// <summary>And the same for a receive-only edge, which otherwise offers every supported codec.</summary>
    [Fact]
    public void An_unknown_codec_fails_a_receive_only_edge_before_it_connects()
    {
        var ex = Assert.Throws<EdgeException>(() =>
            EdgeFactory.CreateSource("whep:http://localhost:9999/whep", new EdgeOptions(30, null, 30, VideoCodec: "nope"), Log));

        Assert.Contains("--video-codec", ex.Message);
    }

    [Fact]
    public void A_known_codec_builds_the_edge()
    {
        var sink = EdgeFactory.CreateSink("null", new EdgeOptions(30, null, 30, VideoCodec: "vp9"), Log);

        Assert.NotNull(sink);
    }
}
