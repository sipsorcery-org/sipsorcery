//-----------------------------------------------------------------------------
// Filename: BrowserTestTests.cs
//
// Description: Unit tests for the parts of "webrtc browser-test" that can be
// exercised without a browser: the browser stats parsing, the option
// validation and the generated page.
//
// The stats parsing matters most. It takes JSON a BROWSER produced, so it has
// to be total: which counters a browser publishes varies by browser, version
// and codec, and an absent counter has to read as "not reported" rather than
// break the run or silently become a zero that would be read as "no freezes".
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

using SIPSorcery.Cli.Commands;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class BrowserStatsReportTests
{
    /// <summary>A Chrome publish-mode report, trimmed to the members the page actually sends.</summary>
    private const string CHROME_OUTBOUND_JSON = """
        {
          "mode": "publish",
          "elapsedMs": 10012,
          "final": true,
          "outbound": {
            "codec": "video/VP8",
            "width": 640,
            "height": 360,
            "framesPerSecond": 30,
            "framesEncoded": 298,
            "framesSent": 298,
            "keyFramesEncoded": 1,
            "pliCount": 0,
            "nackCount": 0,
            "firCount": 0,
            "packetsSent": 812,
            "bytesSent": 643221,
            "qualityLimitationReason": "none"
          },
          "inbound": null
        }
        """;

    [Fact]
    public void Parses_a_chrome_outbound_report()
    {
        bool ok = BrowserStatsReport.TryParse(CHROME_OUTBOUND_JSON, out var report, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.NotNull(report);
        Assert.Equal("publish", report!.Mode);
        Assert.Equal(10012, report.ElapsedMs);
        Assert.True(report.Final);
        Assert.Null(report.Inbound);

        Assert.NotNull(report.Outbound);
        Assert.Equal("video/VP8", report.Outbound!.Codec);
        Assert.Equal(640, report.Outbound.Width);
        Assert.Equal(360, report.Outbound.Height);
        Assert.Equal(298, report.Outbound.FramesEncoded);
        Assert.Equal(1, report.Outbound.KeyFramesEncoded);
        Assert.Equal(812L, report.Outbound.PacketsSent);
        Assert.Equal(643221L, report.Outbound.BytesSent);
        Assert.Equal("none", report.Outbound.QualityLimitationReason);
    }

    [Fact]
    public void Parses_an_echo_report_with_both_directions()
    {
        const string json = """
            {
              "mode": "echo",
              "elapsedMs": 10004,
              "final": true,
              "outbound": { "codec": "video/H264", "framesEncoded": 240, "packetsSent": 900 },
              "inbound": { "codec": "video/H264", "framesDecoded": 251, "framesDropped": 0,
                           "keyFramesDecoded": 3, "freezeCount": 0, "packetsLost": 0, "pliCount": 1 }
            }
            """;

        bool ok = BrowserStatsReport.TryParse(json, out var report, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(240, report!.Outbound!.FramesEncoded);
        Assert.Equal(251, report.Inbound!.FramesDecoded);
        Assert.Equal(0, report.Inbound.FreezeCount);
        Assert.Equal(0L, report.Inbound.PacketsLost);
        Assert.Equal(1, report.Inbound.PliCount);
    }

    [Fact]
    public void A_counter_the_browser_omits_stays_null_rather_than_becoming_zero()
    {
        // Firefox and older Chrome do not report every counter. A missing freezeCount must not read
        // as "zero freezes", which would be an interop PASS claimed from an absent measurement.
        const string json = """{ "mode": "publish", "inbound": { "framesDecoded": 10 } }""";

        Assert.True(BrowserStatsReport.TryParse(json, out var report, out _));

        Assert.Equal(10, report!.Inbound!.FramesDecoded);
        Assert.Null(report.Inbound.FreezeCount);
        Assert.Null(report.Inbound.PliCount);
        Assert.Null(report.Inbound.PacketsLost);
        Assert.Null(report.Inbound.Codec);
    }

    [Fact]
    public void Ignores_members_the_cli_does_not_model()
    {
        // A future browser adding counters must not fail the run.
        const string json = """
            { "mode": "publish", "somethingNew": 42,
              "outbound": { "framesEncoded": 5, "totalEncodeTime": 1.25, "someOtherNewCounter": "x" } }
            """;

        Assert.True(BrowserStatsReport.TryParse(json, out var report, out string? error));
        Assert.Null(error);
        Assert.Equal(5, report!.Outbound!.FramesEncoded);
    }

    [Fact]
    public void Carries_the_page_load_identity_so_a_refused_tab_can_be_ignored()
    {
        // A tab whose offer was refused (409) still runs its failure path and posts a final error
        // report. Without the session id the CLI would honour it and end the session the ACCEPTED
        // tab is in the middle of carrying - which is exactly what happened before this was added.
        const string json = """
            { "sessionId": "4e1b0f2a-0000-4000-8000-000000000001", "mode": "publish", "final": true,
              "error": "the CLI rejected the offer: HTTP 409" }
            """;

        Assert.True(BrowserStatsReport.TryParse(json, out var report, out _));

        Assert.Equal("4e1b0f2a-0000-4000-8000-000000000001", report!.SessionId);
        Assert.True(report.Final);
    }

    [Fact]
    public void Carries_the_pages_own_error_back()
    {
        // The page posts this when it cannot pin the codec, which is how an h265 run explains itself
        // instead of timing out with no reason.
        const string json = """
            { "mode": "publish", "final": true,
              "error": "This browser reports no video/H265 capability for sending" }
            """;

        Assert.True(BrowserStatsReport.TryParse(json, out var report, out _));

        Assert.True(report!.Final);
        Assert.Contains("no video/H265 capability", report.Error);
        Assert.Null(report.Outbound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_body(string? json)
    {
        Assert.False(BrowserStatsReport.TryParse(json, out var report, out string? error));
        Assert.Null(report);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"mode\": }")]
    public void Rejects_malformed_json(string json)
    {
        Assert.False(BrowserStatsReport.TryParse(json, out var report, out string? error));
        Assert.Null(report);
        Assert.Contains("not valid JSON", error);
    }

    [Fact]
    public void Describe_names_both_directions_and_never_throws_on_nulls()
    {
        var report = new BrowserStatsReport(
            Outbound: new BrowserVideoStats(Codec: "video/AV1", FramesEncoded: 100, PacketsSent: 500, QualityLimitationReason: "cpu"),
            Inbound: new BrowserVideoStats(FramesDecoded: 99, FreezeCount: 2));

        string described = report.Describe();

        Assert.Contains("100 encoded", described);
        Assert.Contains("video/AV1", described);
        Assert.Contains("quality limited: cpu", described);
        Assert.Contains("decoded 99 frames", described);
        Assert.Contains("2 freezes", described);
        // The unreported counters read as "?" rather than as a fabricated zero.
        Assert.Contains("? key", described);
    }

    [Fact]
    public void Describe_says_so_when_there_are_no_video_reports()
    {
        Assert.Equal("no video reports", new BrowserStatsReport(Mode: "publish").Describe());
    }

    [Fact]
    public void Describe_omits_the_quality_limit_when_the_browser_reports_none()
    {
        var report = new BrowserStatsReport(Outbound: new BrowserVideoStats(FramesEncoded: 10, QualityLimitationReason: "none"));

        Assert.DoesNotContain("quality limited", report.Describe());
    }
}

[Trait("Category", "unit")]
public class BrowserTestPageTests
{
    [Theory]
    [InlineData("vp8", "video/VP8")]
    [InlineData("VP9", "video/VP9")]
    [InlineData(" h264 ", "video/H264")]
    [InlineData("h265", "video/H265")]
    [InlineData("av1", "video/AV1")]
    public void Maps_cli_codec_names_to_browser_mime_types(string name, string expected)
    {
        Assert.Equal(expected, BrowserTestPage.TryGetMimeType(name));
    }

    [Theory]
    [InlineData("mjpeg")]
    [InlineData("vp10")]
    [InlineData(null)]
    public void Has_no_mime_type_for_a_codec_no_browser_names(string? name)
    {
        Assert.Null(BrowserTestPage.TryGetMimeType(name));
    }

    [Theory]
    [InlineData("publish", true)]
    [InlineData("PUBLISH", true)]
    [InlineData("echo", true)]
    [InlineData("loopback", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Validates_the_mode(string? mode, bool expected)
    {
        Assert.Equal(expected, BrowserTestPage.IsValidMode(mode));
    }

    [Fact]
    public void Publish_page_carries_the_pinned_codec_and_the_run_settings()
    {
        string page = BrowserTestPage.Build(BrowserTestPage.MODE_PUBLISH, "video/VP9", 1280, 720, 25, 15);

        Assert.Contains("const MIME = 'video/VP9';", page);
        Assert.Contains("const W = 1280, H = 720, FPS = 25;", page);
        Assert.Contains("const DURATION_MS = 15 * 1000;", page);
        Assert.Contains("const MODE = 'publish';", page);
        Assert.Contains("width=\"1280\" height=\"720\"", page);

        // Publish mode captures the canvas on a sendonly transceiver and never calls replaceTrack
        // for its own sake; the pinning goes through setCodecPreferences, never SDP munging.
        Assert.Contains("canvas.captureStream(FPS)", page);
        Assert.Contains("direction: 'sendonly'", page);
        Assert.Contains("setCodecPreferences", page);
    }

    [Fact]
    public void Echo_page_uses_one_sendrecv_transceiver_and_replaces_the_track()
    {
        string page = BrowserTestPage.Build(BrowserTestPage.MODE_ECHO, "video/H264", 640, 360, 30, 10);

        // Both directions live in the one template behind a runtime `if (ECHO)`; MODE is what
        // selects between them, so that is what the build has to get right.
        Assert.Contains("const MODE = 'echo';", page);
        Assert.Contains("const ECHO = MODE === 'echo';", page);
        // One sendrecv transceiver plus replaceTrack inside ontrack: the return leg must not need a
        // renegotiation, which a second addTrack would force.
        Assert.Contains("pc.addTransceiver('video', { direction: 'sendrecv' })", page);
        Assert.Contains("tx.sender.replaceTrack(e.track)", page);
        // Echo mode shows the returned video rather than the local canvas.
        Assert.DoesNotContain("""<div id="rxbox" class="hide">""", page);
    }

    [Fact]
    public void An_unpinned_page_leaves_the_codec_to_negotiation()
    {
        string page = BrowserTestPage.Build(BrowserTestPage.MODE_PUBLISH, null, 640, 360, 30, 10);

        Assert.Contains("const MIME = null;", page);
    }

    [Fact]
    public void The_page_posts_its_stats_back_to_the_cli()
    {
        string page = BrowserTestPage.Build(BrowserTestPage.MODE_PUBLISH, "video/VP8", 640, 360, 30, 10);

        Assert.Contains("pc.getStats()", page);
        Assert.Contains("fetch('/stats'", page);
        Assert.Contains("fetch('/whip'", page);
        // The page load identity must reach the CLI on both the offer and every stats post.
        Assert.Contains("'X-Session-Id': SESSION", page);
        Assert.Contains("sessionId: SESSION", page);
        // The counters the result is read on have to actually be collected.
        foreach (string counter in new[] { "framesEncoded", "framesDecoded", "framesDropped", "freezeCount", "keyFramesDecoded", "pliCount", "nackCount" })
        {
            Assert.Contains(counter, page);
        }
    }

    [Fact]
    public void The_page_is_self_contained()
    {
        string page = BrowserTestPage.Build(BrowserTestPage.MODE_PUBLISH, "video/AV1", 640, 360, 30, 10);

        // No external script or stylesheet: the verb serves one file over a loopback listener and
        // the test must not depend on reaching the internet.
        Assert.DoesNotContain("<script src", page);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", page);
        Assert.StartsWith("<!doctype html>", page);
    }
}

[Trait("Category", "unit")]
public class BrowserTestOptionTests
{
    private static bool Validate(out string? error, string mode = "publish", int port = 8080, int duration = 10,
        string preset = "360p", string? size = null, int fps = 30, string? videoCodec = null) =>
        WebRtcBrowserTestCommand.TryValidate(mode, port, duration, preset, size, fps, videoCodec,
            out _, out _, out _, out error);

    [Fact]
    public void Accepts_the_defaults()
    {
        bool ok = WebRtcBrowserTestCommand.TryValidate("publish", 8080, 10, "360p", null, 30, null,
            out int width, out int height, out string? mime, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(640, width);
        Assert.Equal(360, height);
        Assert.Null(mime);      // Nothing pinned without --video-codec.
    }

    [Fact]
    public void Size_overrides_the_preset()
    {
        bool ok = WebRtcBrowserTestCommand.TryValidate("echo", 9000, 5, "1080p", "800x600", 15, "vp9",
            out int width, out int height, out string? mime, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(800, width);
        Assert.Equal(600, height);
        Assert.Equal("video/VP9", mime);
    }

    [Theory]
    [InlineData("vp8", "video/VP8")]
    [InlineData("vp9", "video/VP9")]
    [InlineData("h264", "video/H264")]
    [InlineData("av1", "video/AV1")]
    [InlineData("h265", "video/H265")]
    public void Resolves_a_pinned_codec_for_both_ends(string codec, string expectedMime)
    {
        // h265 validates here: the CLI can offer it. It is the BROWSER that cannot send it, which
        // the page reports at run time - see the README's known gap.
        bool ok = WebRtcBrowserTestCommand.TryValidate("publish", 8080, 10, "360p", null, 30, codec,
            out _, out _, out string? mime, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expectedMime, mime);
    }

    [Fact]
    public void Rejects_an_unknown_mode()
    {
        Assert.False(Validate(out string? error, mode: "loopback"));
        Assert.Contains("Unknown --mode", error);
        Assert.Contains("publish", error);
        Assert.Contains("echo", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(70000)]
    public void Rejects_a_port_outside_the_tcp_range(int port)
    {
        Assert.False(Validate(out string? error, port: port));
        Assert.Contains("not a valid TCP port", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Rejects_a_non_positive_duration(int duration)
    {
        Assert.False(Validate(out string? error, duration: duration));
        Assert.Contains("--duration", error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public void Rejects_an_out_of_range_frame_rate(int fps)
    {
        Assert.False(Validate(out string? error, fps: fps));
        Assert.Contains("--fps", error);
    }

    [Theory]
    [InlineData("1280")]
    [InlineData("1280*720")]
    [InlineData("axb")]
    [InlineData("8x8")]
    [InlineData("99999x1080")]
    public void Rejects_a_size_that_is_not_a_usable_WxH(string size)
    {
        Assert.False(Validate(out string? error, size: size));
        Assert.Contains("--size", error);
    }

    [Fact]
    public void Rejects_an_unknown_preset()
    {
        Assert.False(Validate(out string? error, preset: "240p"));
        Assert.Contains("Unknown preset", error);
    }

    [Fact]
    public void Rejects_a_codec_the_library_does_not_know()
    {
        // A typo must stop the run: falling through to the full offer would answer "that codec
        // works" with a session that used a different one.
        Assert.False(Validate(out string? error, videoCodec: "vp10"));
        Assert.Contains("vp10", error);
    }

    [Fact]
    public void Every_codec_the_library_offers_can_also_be_pinned_in_the_browser()
    {
        // The guard in TryValidate refuses a codec the page could not pin on the browser side,
        // because pinning only our end would let the browser pick something else and make a pass
        // meaningless. That guard must be unreachable in practice: if the library's supported list
        // ever grows a codec the page has no mime type for, this fails and names it.
        foreach (string codec in SIPSorcery.Cli.Common.CodecSelection.VideoNames)
        {
            Assert.True(Validate(out string? error, videoCodec: codec),
                $"--video-codec {codec} is offered by the library but could not be validated: {error}");
        }
    }
}

[Trait("Category", "unit")]
public class BrowserTestAnswerTests
{
    private const string ACCEPTED_ANSWER = """
        v=0
        o=- 7613 0 IN IP4 127.0.0.1
        s=-
        t=0 0
        m=video 52000 UDP/TLS/RTP/SAVP 96
        c=IN IP4 127.0.0.1
        a=rtpmap:96 VP8/90000
        a=recvonly
        """;

    private const string REJECTED_ANSWER = """
        v=0
        o=- 7613 0 IN IP4 127.0.0.1
        s=-
        t=0 0
        m=video 0 UDP/TLS/RTP/SAVP 96
        c=IN IP4 127.0.0.1
        a=inactive
        """;

    [Fact]
    public void Accepts_an_answer_that_negotiated_video()
    {
        Assert.True(BrowserWhipServer.HasNegotiatedVideo(ACCEPTED_ANSWER, out string? error));
        Assert.Null(error);
    }

    [Fact]
    public void Reports_a_video_m_line_the_answer_rejected()
    {
        // This is what a codec the browser would not offer looks like coming back: the run has to
        // say "no format in common" rather than connect and go quiet.
        Assert.False(BrowserWhipServer.HasNegotiatedVideo(REJECTED_ANSWER, out string? error));
        Assert.Contains("no video format in common", error);
    }

    [Fact]
    public void Reports_an_answer_with_no_video_at_all()
    {
        const string audioOnly = """
            v=0
            o=- 7613 0 IN IP4 127.0.0.1
            s=-
            t=0 0
            m=audio 52000 UDP/TLS/RTP/SAVP 111
            c=IN IP4 127.0.0.1
            a=rtpmap:111 opus/48000/2
            """;

        Assert.False(BrowserWhipServer.HasNegotiatedVideo(audioOnly, out string? error));
        Assert.Contains("no video media announcement", error);
    }
}
