//-----------------------------------------------------------------------------
// Filename: BridgeFactoryTests.cs
//
// Description: Unit tests for BridgeFactory - the bridge verb's endpoint spec ->
// participant dispatch and validation (web / agent / openai / sip:<uri>). As with
// the EdgeFactory tests these only exercise construction-time validation; no
// participant is started, so nothing connects out.
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SIPSorcery.Cli.Commands.Bridge;
using SIPSorcery.Cli.Commands.Route;   // EdgeException
using Xunit;

namespace SIPSorcery.Cli.UnitTest;

[Trait("Category", "unit")]
public class BridgeFactoryTests
{
    private static readonly ILogger Log = NullLogger.Instance;
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;

    [Fact]
    public void Web_endpoint_builds_a_web_participant()
    {
        var participant = BridgeFactory.CreateParticipant("web", new BridgeOptions(), LogFactory, Log);

        Assert.IsType<WebParticipant>(participant);
    }

    [Fact]
    public void Sip_endpoint_requires_a_destination()
        => AssertEdgeError(() => BridgeFactory.CreateParticipant("sip:", new BridgeOptions(), LogFactory, Log), "needs a destination");

    [Fact]
    public void Livekit_endpoint_is_not_wired()
        => AssertEdgeError(() => BridgeFactory.CreateParticipant("livekit", new BridgeOptions(), LogFactory, Log), "not wired into bridge");

    [Fact]
    public void Unknown_endpoint_is_rejected()
        => AssertEdgeError(() => BridgeFactory.CreateParticipant("nonsense", new BridgeOptions(), LogFactory, Log), "Unknown bridge endpoint");

    [Fact]
    public void Openai_endpoint_rejects_an_unknown_voice()
        => AssertEdgeError(
            () => BridgeFactory.CreateParticipant("openai", new BridgeOptions(LlmApiKey: "sk-test", Voice: "bogus"), LogFactory, Log),
            "Unknown OpenAI voice");

    [Fact]
    public void Agent_endpoint_rejects_an_unknown_avatar()
        => AssertEdgeError(
            () => BridgeFactory.CreateParticipant("agent", new BridgeOptions(AzureKey: "k", AzureRegion: "r", Avatar: "bogus"), LogFactory, Log),
            "Unknown avatar");

    private static void AssertEdgeError(Action act, string expectedFragment)
    {
        var ex = Assert.Throws<EdgeException>(act);
        Assert.Contains(expectedFragment, ex.Message);
    }
}
