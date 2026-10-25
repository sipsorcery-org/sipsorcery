//-----------------------------------------------------------------------------
// Filename: AgentAvatarTests.cs
//
// Description: Unit tests for the --avatar name resolution (AgentAvatar.Resolve):
// the max cartoon, unknown names, and the default. Whether the default wav2lip
// head can run depends on the machine (Windows plus its model and persona files),
// so those tests accept either outcome but check it is the right one.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 25 Oct 2026	Aaron Clauson	Created, Wexford, Ireland.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Cli.Commands.Bridge;
using SIPSorcery.Cli.Commands.Route;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class AgentAvatarTests
{
    private static bool Wav2LipCanRun =>
        OperatingSystem.IsWindows() &&
        File.Exists(Wav2LipAvatarRenderer.DefaultModelPath()) &&
        File.Exists(Wav2LipAvatarRenderer.DefaultPersonaPath());

    [Theory]
    [InlineData("max")]
    [InlineData("MAX")]
    [InlineData(" max ")]
    public void Max_resolves_to_the_cartoon(string requested)
    {
        Assert.Equal(AgentAvatar.MAX, AgentAvatar.Resolve(requested, NullLogger.Instance));
    }

    [Fact]
    public void Unknown_avatar_is_an_error()
    {
        var ex = Assert.Throws<EdgeException>(() => AgentAvatar.Resolve("bogus", NullLogger.Instance));
        Assert.Contains("Unknown avatar", ex.Message);
    }

    [Fact]
    public void Default_is_wav2lip_when_it_can_run_else_max()
    {
        string expected = Wav2LipCanRun ? AgentAvatar.WAV2LIP : AgentAvatar.MAX;

        Assert.Equal(expected, AgentAvatar.Resolve(AgentAvatar.DEFAULT, NullLogger.Instance));
    }

    [Fact]
    public void Explicit_wav2lip_does_not_fall_back()
    {
        if (Wav2LipCanRun)
        {
            Assert.Equal(AgentAvatar.WAV2LIP, AgentAvatar.Resolve("wav2lip", NullLogger.Instance));
        }
        else
        {
            var ex = Assert.Throws<EdgeException>(() => AgentAvatar.Resolve("wav2lip", NullLogger.Instance));
            Assert.Contains("--avatar max", ex.Message);
        }
    }
}
