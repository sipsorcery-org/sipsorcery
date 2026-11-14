//-----------------------------------------------------------------------------
// Filename: RTCPeerConnectionDefaultCandidateUnitTest.cs
//
// Description: Tests the default destination (c= line, m= port and a=rtcp) in a
// generated offer. With candidates in the SDP it must be one of them (RFC 8839
// 4.2.1.1), and a=rtcp must name the same address and port since RTCP is
// multiplexed (RFC 5761 5.1.3). With no candidates it stays the trickle ICE
// placeholder 0.0.0.0 port 9.
//
// Regression coverage for pjsip (MicroSIP) answering a SIPSorcery WebRTC offer
// with "406 Failed creating media transport": the 0.0.0.0:9 default matched
// none of the candidates, so with "a=rtcp:9 IN IP4 0.0.0.0" pjsip expected a
// second (RTCP) ICE component, which it had already dropped for rtcp-mux, and
// asserted while encoding its answer.
//
// History:
// 14 Nov 2026	Claude Code - Opus 5.5	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net.UnitTests.Helpers;
using SIPSorcery.UnitTests;
using Xunit;

namespace SIPSorcery.Net.UnitTests
{
    [Trait("Category", "unit")]
    public class RTCPeerConnectionDefaultCandidateUnitTest
    {
        private readonly ILogger logger;

        public RTCPeerConnectionDefaultCandidateUnitTest(Xunit.Abstractions.ITestOutputHelper output)
        {
            logger = SIPSorcery.UnitTests.TestLogHelper.InitTestLogger(output);
        }

        /// <summary>The text of the first m= section (the audio one in these offers).</summary>
        private static string FirstMediaSection(string sdp)
        {
            int start = sdp.IndexOf("m=");
            int next = sdp.IndexOf("\nm=", start + 2);
            return next < 0 ? sdp.Substring(start) : sdp.Substring(start, next - start);
        }

        /// <summary>
        /// An offer with candidates: the audio m-line's c= address and port are those of one of
        /// its RTP component candidates, and a=rtcp names the same address and port.
        /// </summary>
        [Fact]
        public void OfferWithCandidates_DefaultDestinationIsACandidate()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var sdp = pc.createOffer(new RTCOfferOptions()).sdp;
                var audio = FirstMediaSection(sdp);

                var candidates = Regex.Matches(audio, @"^a=candidate:(\S+) (\d) (\S+) (\d+) (\S+) (\d+) typ", RegexOptions.Multiline)
                    .Cast<Match>()
                    .Where(m => m.Groups[2].Value == "1")
                    .Select(m => (address: m.Groups[5].Value, port: int.Parse(m.Groups[6].Value)))
                    .ToList();
                Assert.NotEmpty(candidates);

                var mline = Regex.Match(audio, @"^m=audio (\d+) ", RegexOptions.Multiline);
                var conn = Regex.Match(audio, @"^c=IN (IP4|IP6) (\S+)", RegexOptions.Multiline);
                Assert.True(mline.Success && conn.Success, audio);
                int port = int.Parse(mline.Groups[1].Value);
                string address = conn.Groups[2].Value.Trim();

                Assert.NotEqual(9, port);
                Assert.Contains((address, port), candidates);
                Assert.Matches(new Regex($@"^a=rtcp:{port} IN {conn.Groups[1].Value} {Regex.Escape(address)}\r?$", RegexOptions.Multiline), audio);
                Assert.Contains("a=rtcp-mux", audio);
            }
        }

        /// <summary>
        /// The default prefers an IPv4 candidate when there is one, as the c= line's address type
        /// is then what most SIP peers expect.
        /// </summary>
        [Fact]
        public void OfferWithCandidates_PrefersIPv4Default()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var sdp = pc.createOffer(new RTCOfferOptions()).sdp;
                var audio = FirstMediaSection(sdp);
                bool hasIPv4 = Regex.IsMatch(audio, @"^a=candidate:\S+ 1 \S+ \d+ \d+\.\d+\.\d+\.\d+ \d+ typ", RegexOptions.Multiline);

                if (hasIPv4)
                {
                    Assert.Matches(new Regex(@"^c=IN IP4 \d+\.\d+\.\d+\.\d+\r?$", RegexOptions.Multiline), audio);
                }
            }
        }

        /// <summary>
        /// Every bundled m-line shares the one ICE transport, so they all carry the same default.
        /// </summary>
        [Fact]
        public void OfferWithCandidates_AllBundledMediaShareTheDefault()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pc = new PeerConnectionBuilder().WithAudioTrack().WithVideoTrack().Build())
            {
                var parsed = SdpAssert.Parse(pc.createOffer(new RTCOfferOptions()).sdp);

                Assert.True(parsed.Media.Count >= 2);
                Assert.Single(parsed.Media.Select(m => (m.Port, m.Connection.ConnectionAddress)).Distinct());
                Assert.NotEqual(9, parsed.Media[0].Port);
            }
        }

        /// <summary>
        /// With no candidates in the SDP (X_ExcludeIceCandidates) the default stays the trickle
        /// ICE placeholder, and a=rtcp with it.
        /// </summary>
        [Fact]
        public void OfferWithoutCandidates_KeepsPlaceholderDefault()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var audio = FirstMediaSection(pc.createOffer(new RTCOfferOptions { X_ExcludeIceCandidates = true }).sdp);

                Assert.DoesNotContain("a=candidate:", audio);
                Assert.Matches(new Regex(@"^m=audio 9 ", RegexOptions.Multiline), audio);
                Assert.Matches(new Regex(@"^c=IN IP4 0\.0\.0\.0\r?$", RegexOptions.Multiline), audio);
                Assert.Matches(new Regex(@"^a=rtcp:9 IN IP4 0\.0\.0\.0\r?$", RegexOptions.Multiline), audio);
            }
        }

        /// <summary>
        /// ice-options tokens are separated by spaces (RFC 8839 5.6), not commas.
        /// </summary>
        [Fact]
        public void Offer_IceOptionsAreSpaceSeparated()
        {
            logger.LogDebug("--> {MethodName}", TestHelper.GetCurrentMethodName());

            using (var pc = new PeerConnectionBuilder().WithAudioTrack().Build())
            {
                var sdp = pc.createOffer(new RTCOfferOptions()).sdp;

                Assert.Contains("a=ice-options:ice2 trickle", sdp);
                Assert.DoesNotContain("ice2,trickle", sdp);
            }
        }
    }
}
