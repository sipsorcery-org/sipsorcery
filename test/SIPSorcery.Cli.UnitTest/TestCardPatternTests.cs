//-----------------------------------------------------------------------------
// Filename: TestCardPatternTests.cs
//
// Description: Unit tests for the testcard video pattern and the --pattern auto
// resolution used by "webrtc loopback" and "webrtc whip".
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 24 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorcery.Cli.Commands;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class TestCardPatternTests
{
    [Theory]
    [InlineData(null, true, LibraryVideoPublisher.PATTERN_TESTCARD)]
    [InlineData("auto", true, LibraryVideoPublisher.PATTERN_TESTCARD)]
    [InlineData("AUTO", false, LibraryVideoPublisher.PATTERN_TEXTURE)]
    [InlineData("texture", true, LibraryVideoPublisher.PATTERN_TEXTURE)]
    [InlineData("testcard", false, LibraryVideoPublisher.PATTERN_TESTCARD)]
    public void Resolves_pattern(string? requested, bool videoShown, string expected)
    {
        Assert.Equal(expected, LibraryVideoPublisher.ResolvePattern(requested, videoShown));
    }

    [Fact]
    public void Rejects_unknown_pattern()
    {
        var settings = new LibraryVideoPublisher.Settings("720p", null, 30, "ffmpeg", null, 0, false, null, 0, Pattern: "bogus");

        Assert.False(LibraryVideoPublisher.TryValidate(settings, out string? error));
        Assert.Contains("--pattern", error);
    }

    [Fact]
    public void Draws_colour_bars()
    {
        const int width = 640, height = 360;
        var card = new TestCardPattern(width, height, 30);
        var frame = new byte[card.I420Size];

        card.Fill(frame, 0);

        // Top left is the white bar and top right the blue bar (BT.601 75% bar luma).
        Assert.Equal(180, frame[0]);
        Assert.Equal(35, frame[width - 1]);
    }

    [Fact]
    public void Frames_change_as_the_counter_and_block_move()
    {
        var card = new TestCardPattern(640, 360, 30);
        var first = new byte[card.I420Size];
        var second = new byte[card.I420Size];

        card.Fill(first, 0);
        card.Fill(second, 1);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Same_frame_index_draws_the_same_frame()
    {
        var card = new TestCardPattern(640, 360, 30);
        var first = new byte[card.I420Size];
        var second = new byte[card.I420Size];

        card.Fill(first, 42);
        card.Fill(second, 42);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Draws_small_frames_without_overrunning()
    {
        var card = new TestCardPattern(16, 16, 1);
        var frame = new byte[card.I420Size];

        for (int i = 0; i < 10; i++)
        {
            card.Fill(frame, i);
        }
    }
}
