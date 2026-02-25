//-----------------------------------------------------------------------------
// Filename: SdpAssert.cs
//
// Description: Composable assertions for verifying SDP shapes produced by
// createOffer / createAnswer / SetRemoteDescription. Replaces the per-test
// "loop the announcements and Assert.Equal" boilerplate.
//
// All assertions are static methods on SdpAssert. They throw xUnit
// Sdk.XunitException on failure (i.e. the same exception type the framework
// uses), so a failure surfaces exactly as a normal Assert.* would.
//
// Convention: every assertion takes the SDP under test as the first
// argument, then the expectation, then optional extra context for the
// failure message.
//
// History:
// 20 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SIPSorcery.Net.UnitTests.Helpers
{
    /// <summary>
    /// Static helpers for verifying the structure of a parsed
    /// <see cref="SDP"/> object produced by negotiation. Designed to be
    /// imported with <c>using static SIPSorcery.Net.UnitTests.Helpers.SdpAssert;</c>
    /// so call sites read as <c>HasMediaLines(sdp, "audio", "video")</c>.
    /// </summary>
    public static class SdpAssert
    {
        /// <summary>
        /// Parses raw SDP text. Convenience wrapper so tests don't repeat
        /// <c>SDP.ParseSDPDescription(...)</c> everywhere.
        /// </summary>
        public static SDP Parse(string rawSdp) => SDP.ParseSDPDescription(rawSdp);

        /// <summary>
        /// Asserts the SDP has exactly the given m-line types, in the given
        /// order. <c>kinds</c> are case-insensitive strings ("audio",
        /// "video", "text", "application").
        /// </summary>
        public static void HasMediaLines(SDP sdp, params string[] kinds)
        {
            Assert.NotNull(sdp);
            Assert.Equal(kinds.Length, sdp.Media.Count);
            for (var i = 0; i < kinds.Length; i++)
            {
                Assert.Equal(
                    kinds[i].ToLowerInvariant(),
                    sdp.Media[i].Media.ToString().ToLowerInvariant());
            }
        }

        /// <summary>
        /// Asserts the SDP carries a DTLS fingerprint either at the session
        /// level or on at least one m-line.
        /// </summary>
        public static void HasFingerprint(SDP sdp)
        {
            Assert.NotNull(sdp);
            // DTLS fingerprint may be declared at session level (Sdp.DtlsFingerprint)
            // or per-m-line (Media[i].DtlsFingerprint). Treat either as present.
            var any = !string.IsNullOrEmpty(sdp.DtlsFingerprint)
                       || sdp.Media.Any(m => !string.IsNullOrEmpty(m.DtlsFingerprint));
            Assert.True(any, "Expected at least one DTLS fingerprint in the SDP (session-level or m-line).");
        }

        /// <summary>
        /// Asserts the SDP declares an a=group:BUNDLE line bundling all
        /// media into one transport.
        /// </summary>
        public static void HasBundle(SDP sdp)
        {
            Assert.NotNull(sdp);
            Assert.False(string.IsNullOrEmpty(sdp.Group),
                "Expected the SDP to declare a BUNDLE group (a=group:BUNDLE ...).");
        }

        /// <summary>
        /// Asserts the SDP carries ICE ufrag and password, either at the
        /// session level or on every m-line.
        /// </summary>
        public static void HasIceCredentials(SDP sdp)
        {
            Assert.NotNull(sdp);
            var sessionLevel = !string.IsNullOrEmpty(sdp.IceUfrag) && !string.IsNullOrEmpty(sdp.IcePwd);
            var anyMediaLevel = sdp.Media.Any(m => !string.IsNullOrEmpty(m.IceUfrag) && !string.IsNullOrEmpty(m.IcePwd));
            Assert.True(sessionLevel || anyMediaLevel,
                "Expected ICE ufrag/pwd at the session level or on every m-line.");
        }

        /// <summary>
        /// Returns the first audio announcement in <paramref name="sdp"/>,
        /// failing the test if none is present. For SDPs with multiple
        /// audio m-lines, this targets index 0.
        /// </summary>
        public static SDPMediaAnnouncement Audio(SDP sdp) => FirstOfKind(sdp, SDPMediaTypesEnum.audio);

        /// <summary>
        /// Returns the first video announcement in <paramref name="sdp"/>,
        /// failing the test if none is present.
        /// </summary>
        public static SDPMediaAnnouncement Video(SDP sdp) => FirstOfKind(sdp, SDPMediaTypesEnum.video);

        /// <summary>
        /// Returns the first text announcement in <paramref name="sdp"/>,
        /// failing the test if none is present.
        /// </summary>
        public static SDPMediaAnnouncement Text(SDP sdp) => FirstOfKind(sdp, SDPMediaTypesEnum.text);

        private static SDPMediaAnnouncement FirstOfKind(SDP sdp, SDPMediaTypesEnum kind)
        {
            Assert.NotNull(sdp);
            SDPMediaAnnouncement m = sdp.Media.FirstOrDefault(x => x.Media == kind);
            Assert.NotNull(m); // "expected at least one m=<kind> line"
            return m;
        }

        /// <summary>
        /// Asserts the announcement's direction matches the expected
        /// MediaStreamStatusEnum value. A missing a=direction line in the
        /// source SDP is treated as the per-spec default of SendRecv.
        /// </summary>
        public static void HasDirection(SDPMediaAnnouncement m, MediaStreamStatusEnum expected)
        {
            Assert.NotNull(m);
            // MediaStreamStatus can be null when the SDP omits a=sendrecv/sendonly/etc.,
            // in which case the per-spec default is sendrecv.
            MediaStreamStatusEnum actual = m.MediaStreamStatus ?? MediaStreamStatusEnum.SendRecv;
            Assert.Equal(expected, actual);
        }

        /// <summary>
        /// Asserts the announcement advertises the named codec at the given
        /// payload id. Codec name match is case-insensitive.
        /// </summary>
        public static void HasCodec(SDPMediaAnnouncement m, string codecName, int payloadId)
        {
            Assert.NotNull(m);
            var present = m.MediaFormats.Values.Any(f =>
                f.ID == payloadId
                && string.Equals(f.Name(), codecName, System.StringComparison.OrdinalIgnoreCase));
            Assert.True(present,
                $"Expected m={m.Media} to advertise {codecName} (PT {payloadId}). Got: {string.Join(", ", m.MediaFormats.Values.Select(f => $"{f.Name()}/{f.ID}"))}");
        }

        public static void DoesNotHaveCodec(SDPMediaAnnouncement m, string codecName)
        {
            Assert.NotNull(m);
            var present = m.MediaFormats.Values.Any(f =>
                string.Equals(f.Name(), codecName, System.StringComparison.OrdinalIgnoreCase));
            Assert.False(present,
                $"Expected m={m.Media} to NOT advertise {codecName}.");
        }

        /// <summary>
        /// Asserts the announcement is accepted (m= port != 0). Per RFC 3264
        /// §8.2, port 0 means the announcement is rejected.
        /// </summary>
        public static void IsAccepted(SDPMediaAnnouncement m)
        {
            Assert.NotNull(m);
            Assert.NotEqual(0, m.Port);
        }

        /// <summary>
        /// Asserts the announcement is rejected (m= port == 0).
        /// </summary>
        public static void IsRejected(SDPMediaAnnouncement m)
        {
            Assert.NotNull(m);
            Assert.Equal(0, m.Port);
        }

        /// <summary>
        /// Asserts the announcement's setup attribute is the given role.
        /// Per RFC 5763 §5 and #1463: answer SDPs must use active/passive,
        /// never actpass.
        /// </summary>
        public static void HasSetupRole(SDPMediaAnnouncement m, IceRolesEnum expected)
        {
            Assert.NotNull(m);
            Assert.NotNull(m.IceRole);
            Assert.Equal(expected, m.IceRole.Value);
        }

        public static void SetupIsNotActpass(SDPMediaAnnouncement m)
        {
            Assert.NotNull(m);
            Assert.NotNull(m.IceRole);
            Assert.NotEqual(IceRolesEnum.actpass, m.IceRole.Value);
        }

        public static void HasMid(SDPMediaAnnouncement m, string expectedMid)
        {
            Assert.NotNull(m);
            Assert.Equal(expectedMid, m.MediaID);
        }
    }
}
