//-----------------------------------------------------------------------------
// Filename: SipDestinationTests.cs
//
// Description: Unit tests for SipDestination.TryParse - the shared parser for the
// sip: destination argument (accepts "sip:user@host", "sips:...", and a bare
// user@host; rejects nonsense rather than letting it become a DNS failure).
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

using SIPSorcery.Cli.Common;
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class SipDestinationTests
{
    [Theory]
    [InlineData("sip:music@iptel.org", "music", "iptel.org")]
    [InlineData("music@iptel.org", "music", "iptel.org")]
    [InlineData("sips:alice@example.com", "alice", "example.com")]
    public void Parses_valid_destinations(string input, string expectedUser, string expectedHost)
    {
        bool ok = SipDestination.TryParse(input, out var uri, out var error);

        Assert.True(ok, error);
        Assert.Equal(expectedUser, uri.User);
        Assert.Equal(expectedHost, uri.Host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad name@with spaces")]
    public void Rejects_invalid_destinations(string input)
    {
        bool ok = SipDestination.TryParse(input, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
