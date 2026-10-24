//-----------------------------------------------------------------------------
// Filename: RtpIceChannelTcpCandidateUnitTest.cs
//
// Description: Unit tests for how the RTP ICE channel handles remote TCP
// candidates. Chrome trickles active TCP candidates with the RFC 6544
// placeholder port 9 alongside its UDP candidates; these must be rejected
// rather than treated as UDP candidates on port 9.
//
// Author(s):
// Claude Code
//
// History:
// 24 Oct 2026	Claude Code	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RtpIceChannelTcpCandidateUnitTest
    {
        public RtpIceChannelTcpCandidateUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static RTCIceCandidate FromInit(string candidateLine) =>
            new RTCIceCandidate(new RTCIceCandidateInit
            {
                candidate = candidateLine,
                sdpMid = "0",
                sdpMLineIndex = 0
            });

        [Theory]
        [InlineData("candidate:3286179282 1 tcp 1518214911 100.122.148.20 9 typ host tcptype active generation 0 ufrag zISE network-id 1 network-cost 50")]
        [InlineData("candidate:2039123007 1 tcp 1518285567 fd7a:115c:a1e0:ab12:4843:cd96:627a:9414 9 typ host tcptype active generation 0 ufrag zISE network-id 2")]
        [InlineData("candidate:4 1 TCP 2105458943 10.0.1.16 9 typ host tcptype active")]
        public void ActiveTcpCandidateIsRejected(string candidateLine)
        {
            using var channel = new RtpIceChannel();
            var errors = new List<string>();
            channel.OnIceCandidateError += (candidate, error) => errors.Add(error);

            channel.AddRemoteCandidate(FromInit(candidateLine));

            Assert.Empty(channel._remoteCandidates);
            Assert.Single(errors);
            Assert.Contains("active TCP", errors[0]);
        }

        [Fact]
        public void PassiveTcpCandidateIsRejectedAsUnsupported()
        {
            using var channel = new RtpIceChannel();
            var errors = new List<string>();
            channel.OnIceCandidateError += (candidate, error) => errors.Add(error);

            channel.AddRemoteCandidate(FromInit("candidate:1 1 tcp 1518280447 192.168.1.10 50000 typ host tcptype passive generation 0"));

            Assert.Empty(channel._remoteCandidates);
            Assert.Single(errors);
            Assert.Contains("unsupported transport protocol tcp", errors[0]);
        }

        [Fact]
        public void UdpCandidateIsAccepted()
        {
            using var channel = new RtpIceChannel();
            var errors = new List<string>();
            channel.OnIceCandidateError += (candidate, error) => errors.Add(error);

            channel.AddRemoteCandidate(FromInit("candidate:1031129414 1 udp 2122194687 100.122.148.20 51843 typ host generation 0 ufrag zISE network-id 1 network-cost 50"));

            Assert.Empty(errors);
            var remote = Assert.Single(channel._remoteCandidates.ToList());
            Assert.Equal(RTCIceProtocol.udp, remote.protocol);
            Assert.Equal(51843, remote.port);
        }
    }
}
