//-----------------------------------------------------------------------------
// Filename: RtpIceChannelCharacterizationUnitTest.cs
//
// Description: Characterization tests for the public behaviour of RtpIceChannel.
// These pin the externally observable contract - initial state, remote candidate
// validation rules, credential handling and host candidate gathering - so that an
// upcoming refactor that pulls the candidate-gathering, ICE-server/TURN and
// connectivity-check responsibilities out of RtpIceChannel can be verified to
// preserve behaviour.
//
// They are deterministic and network-free beyond binding a loopback UDP socket
// (the same approach RTPChannelUnitTest uses). The internal _remoteCandidates bag
// is used to confirm the accept path (InternalsVisibleTo SIPSorcery.UnitTests).
//
// Author(s):
// Aaron Clauson
//
// History:
// 08 Jun 2026	Aaron Clauson	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RtpIceChannelCharacterizationUnitTest
    {
        private Microsoft.Extensions.Logging.ILogger logger = null;

        public RtpIceChannelCharacterizationUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        private static RTCIceCandidate Remote(
            string address,
            ushort port = 5000,
            RTCIceProtocol protocol = RTCIceProtocol.udp,
            RTCIceComponent component = RTCIceComponent.rtp,
            ushort sdpMLineIndex = 0)
        {
            return new RTCIceCandidate(new RTCIceCandidateInit())
            {
                address = address,
                port = port,
                protocol = protocol,
                component = component,
                sdpMLineIndex = sdpMLineIndex
            };
        }

        [Fact]
        public void NewChannelHasNewGatheringAndConnectionState()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = new RtpIceChannel(IPAddress.Loopback, RTCIceComponent.rtp);
            try
            {
                Assert.Equal(RTCIceGatheringState.@new, channel.IceGatheringState);
                Assert.Equal(RTCIceConnectionState.@new, channel.IceConnectionState);
                Assert.Equal(RTCIceComponent.rtp, channel.Component);
                Assert.False(string.IsNullOrEmpty(channel.LocalIceUser));
                Assert.False(string.IsNullOrEmpty(channel.LocalIcePassword));
            }
            finally
            {
                channel.Close();
            }
        }

        [Fact]
        public void SetRemoteCredentialsStoresUserAndPassword()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = new RtpIceChannel(IPAddress.Loopback, RTCIceComponent.rtp);
            try
            {
                channel.SetRemoteCredentials("remoteuser", "remotepassword");

                Assert.Equal("remoteuser", channel.RemoteIceUser);
                Assert.Equal("remotepassword", channel.RemoteIcePassword);
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// A well-formed remote candidate is accepted: no error is raised and it is stored in the
        /// remote candidate set ready for checklist processing.
        /// </summary>
        [Fact]
        public void AddRemoteCandidateAcceptsValidCandidate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = new RtpIceChannel(IPAddress.Loopback, RTCIceComponent.rtp);
            try
            {
                var errors = new List<string>();
                channel.OnIceCandidateError += (c, err) => errors.Add(err);

                channel.AddRemoteCandidate(Remote("192.168.1.50", 5000));

                Assert.Empty(errors);
                Assert.Single(channel._remoteCandidates);
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// Each of the deterministic validation rules in AddRemoteCandidate must reject the candidate by
        /// raising OnIceCandidateError and must NOT add it to the remote candidate set. The OS-IPv6 rule is
        /// intentionally excluded because it depends on the host network configuration.
        /// </summary>
        [Theory]
        [InlineData("empty-address")]
        [InlineData("wrong-component")]
        [InlineData("nonzero-mline")]
        [InlineData("non-udp")]
        [InlineData("wildcard-ipv4")]
        [InlineData("wildcard-ipv6")]
        [InlineData("zero-port")]
        public void AddRemoteCandidateRejectsInvalidCandidate(string scenario)
        {
            logger.LogDebug("--> {MethodName} ({Scenario})", TestHelper.GetCurrentMethodName(), scenario);

            var channel = new RtpIceChannel(IPAddress.Loopback, RTCIceComponent.rtp);
            try
            {
                RTCIceCandidate candidate = scenario switch
                {
                    "empty-address"   => Remote("   "),
                    "wrong-component" => Remote("192.168.1.50", component: RTCIceComponent.rtcp),
                    "nonzero-mline"   => Remote("192.168.1.50", sdpMLineIndex: 1),
                    "non-udp"         => Remote("192.168.1.50", protocol: RTCIceProtocol.tcp),
                    "wildcard-ipv4"   => Remote("0.0.0.0"),
                    "wildcard-ipv6"   => Remote("::"),
                    "zero-port"       => Remote("192.168.1.50", port: 0),
                    _                 => null
                };

                var errors = new List<string>();
                channel.OnIceCandidateError += (c, err) => errors.Add(err);

                channel.AddRemoteCandidate(candidate);

                Assert.NotEmpty(errors);
                Assert.Empty(channel._remoteCandidates);
            }
            finally
            {
                channel.Close();
            }
        }

        [Fact]
        public void AddNullRemoteCandidateRaisesErrorAndIsNotStored()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = new RtpIceChannel(IPAddress.Loopback, RTCIceComponent.rtp);
            try
            {
                var errors = new List<string>();
                channel.OnIceCandidateError += (c, err) => errors.Add(err);

                channel.AddRemoteCandidate(null);

                Assert.NotEmpty(errors);
                Assert.Empty(channel._remoteCandidates);
            }
            finally
            {
                channel.Close();
            }
        }

        /// <summary>
        /// With no ICE servers configured, gathering on a specific (loopback) bind address completes
        /// synchronously: the gathering state advances new -> gathering -> complete, a single host
        /// candidate is produced for the bind address, and it is exposed via the Candidates collection.
        /// </summary>
        [Fact]
        public void StartGatheringWithNoIceServersProducesHostCandidateAndCompletes()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            var channel = new RtpIceChannel(IPAddress.Loopback, RTCIceComponent.rtp);
            try
            {
                var gatheringStates = new List<RTCIceGatheringState>();
                var emittedCandidates = new List<RTCIceCandidate>();
                channel.OnIceGatheringStateChange += s => gatheringStates.Add(s);
                channel.OnIceCandidate += c => emittedCandidates.Add(c);

                channel.StartGathering();

                Assert.Contains(RTCIceGatheringState.gathering, gatheringStates);
                Assert.Contains(RTCIceGatheringState.complete, gatheringStates);
                Assert.Equal(RTCIceGatheringState.complete, channel.IceGatheringState);

                var host = Assert.Single(emittedCandidates);
                Assert.Equal(RTCIceCandidateType.host, host.type);
                Assert.Equal(IPAddress.Loopback.ToString(), host.address);

                Assert.Contains(channel.Candidates, c => c.type == RTCIceCandidateType.host);
            }
            finally
            {
                channel.Close();
            }
        }
    }
}
