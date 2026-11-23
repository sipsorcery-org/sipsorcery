//-----------------------------------------------------------------------------
// Filename: EdgeFactoryTests.cs
//
// Description: Unit tests for EdgeFactory - the route verb's --from/--to spec ->
// node dispatch and validation. These exercise the pure parsing/validation logic
// (which scheme maps to which node, and the argument errors); they never bring an
// edge up, so no network or external process is touched.
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Cli.Commands.Route;
using SIPSorceryMedia.Abstractions;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class EdgeFactoryTests
{
    private static readonly ILogger Log = NullLogger.Instance;

    private static EdgeOptions Options() => new(30, null, 30);

    // ---- sources ----

    [Fact]
    public void Source_whep_requires_a_url()
        => AssertEdgeError(() => EdgeFactory.CreateSource("whep:", Options(), Log), "whep source needs a URL");

    [Theory]
    [InlineData("browser")]
    [InlineData("browser:9100")]
    public void Source_browser_builds_with_an_optional_port(string spec)
    {
        // Constructing the node must not bind the listener: StartAsync does that, so a spec check
        // stays free of side effects and these tests touch no sockets.
        var node = EdgeFactory.CreateSource(spec, Options(), Log);

        Assert.IsType<BrowserSourceNode>(node);
        Assert.Contains(spec.Contains(':') ? "9100" : "8080", node.Describe());
    }

    [Theory]
    [InlineData("browser:0")]
    [InlineData("browser:70000")]
    [InlineData("browser:eighty")]
    public void Source_browser_rejects_a_bad_port(string spec)
        => AssertEdgeError(() => EdgeFactory.CreateSource(spec, Options(), Log), "is not a valid port number");

    [Fact]
    public void Source_browser_rejects_a_size_that_is_not_a_usable_WxH()
        => AssertEdgeError(() => EdgeFactory.CreateSource("browser", Options() with { BrowserSize = "1280" }, Log),
            "--browser-size");

    [Fact]
    public void Source_browser_pins_the_video_codec_on_both_ends()
    {
        // Pinning only our end would let the browser pick a different codec, so a --video-codec the
        // page cannot name to the browser has to fail rather than half-apply.
        var node = EdgeFactory.CreateSource("browser", Options() with { VideoCodec = "vp9" }, Log);

        Assert.IsType<BrowserSourceNode>(node);
    }

    [Fact]
    public void Source_web_points_at_the_browser_source_for_a_one_way_route()
        => AssertEdgeError(() => EdgeFactory.CreateSource("web", Options(), Log), "--from browser");

    [Fact]
    public void Source_livekit_requires_a_room()
        => AssertEdgeError(() => EdgeFactory.CreateSource("livekit:", Options(), Log), "needs a room name");

    [Fact]
    public void Source_cloudflare_requires_a_session_id()
        => AssertEdgeError(() => EdgeFactory.CreateSource("cloudflare:", Options(), Log), "needs the publisher's session id");

    [Fact]
    public void Source_whip_is_not_a_route_source()
        => AssertEdgeError(() => EdgeFactory.CreateSource("whip:http://host/whip", Options(), Log), "not wired into route");

    [Fact]
    public void Source_unknown_scheme_is_rejected()
        => AssertEdgeError(() => EdgeFactory.CreateSource("nonsense", Options(), Log), "Unknown --from edge");

    // ---- sinks ----

    [Fact]
    public void Sink_null_builds_a_discard_video_sink()
    {
        var sink = EdgeFactory.CreateSink("null", Options(), Log);

        Assert.IsType<VideoSinkNode>(sink);
        Assert.Equal("null", sink.Describe());
    }

    [Fact]
    public void Sink_whip_requires_a_url()
        => AssertEdgeError(() => EdgeFactory.CreateSink("whip:", Options(), Log), "whip sink needs a URL");

    [Fact]
    public void Sink_whip_rejects_a_non_http_url()
        => AssertEdgeError(() => EdgeFactory.CreateSink("whip:notaurl", Options(), Log), "HTTP or HTTPS URL");

    [Fact]
    public void Sink_web_rejects_a_bad_port()
        => AssertEdgeError(() => EdgeFactory.CreateSink("web:99999", Options(), Log), "not a valid port number");

    [Theory]
    [InlineData("sip:bob@host")]
    [InlineData("sips:bob@host:5061")]
    public async Task Sink_sip_builds_a_call_sink_offering_opus_then_g711(string spec)
    {
        // Constructing the node places no call: StartAsync does that, so this touches no network.
        var sink = Assert.IsType<SipSinkNode>(EdgeFactory.CreateSink(spec, Options(), Log));
        try
        {
            Assert.Contains("bob@host", sink.Describe());
            Assert.Equal(new[] { AudioCodecsEnum.OPUS, AudioCodecsEnum.PCMU, AudioCodecsEnum.PCMA },
                sink.OfferedAudio.Select(f => f.Codec));
            Assert.Equal(VideoCodecsEnum.H264, sink.OfferedVideo.Codec);
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sink_sip_with_a_g711_graph_offers_just_that_codec()
    {
        var sink = Assert.IsType<SipSinkNode>(EdgeFactory.CreateSink("sip:bob@host", Options() with { AudioCodec = "pcma" }, Log));
        try
        {
            Assert.Equal(new[] { AudioCodecsEnum.PCMA }, sink.OfferedAudio.Select(f => f.Codec));
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    [Fact]
    public async Task Sink_sip_offers_the_pinned_video_codec()
    {
        var sink = Assert.IsType<SipSinkNode>(EdgeFactory.CreateSink("sip:bob@host", Options() with { VideoCodec = "vp8" }, Log));
        try
        {
            Assert.Equal(VideoCodecsEnum.VP8, sink.OfferedVideo.Codec);
        }
        finally
        {
            await sink.DisposeAsync();
        }
    }

    [Fact]
    public void Sink_sip_requires_a_destination()
        => AssertEdgeError(() => EdgeFactory.CreateSink("sip:", Options(), Log), "sip sink needs a destination");

    [Fact]
    public void Sink_bare_user_at_host_is_a_file_not_a_call()
    {
        // As a source a bare user@host is a SIP destination; as a sink it would be too easily a file name.
        var sink = EdgeFactory.CreateSink("bob@host.h264", Options(), Log);

        Assert.IsType<VideoSinkNode>(sink);
    }

    [Fact]
    public void Sink_cloudflare_requires_credentials()
        => WithoutEnv(new[] { "CLOUDFLARE_APPID", "CLOUDFLARE_API_TOKEN" }, () =>
            AssertEdgeError(() => EdgeFactory.CreateSink("cloudflare", Options(), Log), "needs an app ID and API token"));

    [Fact]
    public void Sink_livekit_requires_credentials()
        => WithoutEnv(new[] { "LIVEKIT_WEBSOCKET_URL", "LIVEKIT_API_KEY", "LIVEKIT_API_SECRET" }, () =>
            AssertEdgeError(() => EdgeFactory.CreateSink("livekit:room", Options(), Log), "needs a URL, API key and API secret"));

    private static void AssertEdgeError(Action act, string expectedFragment)
    {
        var ex = Assert.Throws<EdgeException>(act);
        Assert.Contains(expectedFragment, ex.Message);
    }

    /// <summary>Runs the action with the named environment variables cleared, restoring them afterwards,
    /// so a credential-resolution test is deterministic on a machine that has those variables set.</summary>
    private static void WithoutEnv(string[] names, Action act)
    {
        var saved = names.ToDictionary(n => n, Environment.GetEnvironmentVariable);
        try
        {
            foreach (var n in names)
            {
                Environment.SetEnvironmentVariable(n, null);
            }
            act();
        }
        finally
        {
            foreach (var kv in saved)
            {
                Environment.SetEnvironmentVariable(kv.Key, kv.Value);
            }
        }
    }
}
