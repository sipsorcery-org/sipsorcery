//-----------------------------------------------------------------------------
// Filename: Av1TileLayoutTests.cs
//
// Description: Unit tests for WebRtcVideoBenchCommand.Av1TileLayoutLog2, the
// resolution based AV1 tile layout the video-bench decode clips are encoded
// with by both SVT-AV1 and av1.net.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 22 Nov 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using SIPSorcery.Cli.Commands;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class Av1TileLayoutTests
{
    [Theory]
    [InlineData(320, 240, 0, 0)]
    [InlineData(640, 480, 0, 0)]
    [InlineData(1280, 720, 0, 0)]
    [InlineData(1920, 1080, 1, 1)]
    [InlineData(2560, 1440, 1, 1)]
    [InlineData(3840, 2160, 2, 2)]
    [InlineData(7680, 4320, 3, 3)]
    public void Layout_scales_with_resolution(int width, int height, int colsLog2, int rowsLog2)
    {
        Assert.Equal((colsLog2, rowsLog2), WebRtcVideoBenchCommand.Av1TileLayoutLog2(width, height));
    }

    [Fact]
    public void Layout_is_capped()
    {
        Assert.Equal((4, 4), WebRtcVideoBenchCommand.Av1TileLayoutLog2(65536, 65536));
    }
}
