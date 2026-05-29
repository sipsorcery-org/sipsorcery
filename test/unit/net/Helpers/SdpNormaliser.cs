//-----------------------------------------------------------------------------
// Filename: SdpNormaliser.cs
//
// Description: Replaces the non-deterministic fields in a serialised SDP
// with stable placeholders so two SDP strings produced by different runs
// of the same code path can be compared character-for-character.
//
// Use this for "golden master" characterization tests:
//
//   string actual = SdpNormaliser.Normalise(pc.createOffer().sdp);
//   Assert.Equal(GoldenOffers.AudioOnly_Normalised, actual);
//
// The placeholders are deliberately short and human-readable so a
// mismatch diff is easy to read.
//
// Non-deterministic fields that get normalised:
//
//   o=line   session-id and version  -> <SID> <SVER>
//   m=line   port                    -> <PORT>
//   c=line   address                 -> <IP>
//   a=ice-ufrag                      -> <UFRAG>
//   a=ice-pwd                        -> <PWD>
//   a=fingerprint  hash bytes        -> <HASH>
//   a=ssrc                           -> <SSRC>
//   a=ice-candidate (raw)            -> <CANDIDATE>
//   a=crypto inline key              -> <CRYPTO_KEY>
//
// Deterministic fields are passed through untouched so the residual SDP
// is still a meaningful diff.
//
// History:
// 20 May 2026	Claude Code - Opus 4.7	Created.
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;

namespace SIPSorcery.Net.UnitTests.Helpers
{
    /// <summary>
    /// Strips non-deterministic fields from a serialised SDP so the rest
    /// can be compared as a golden-master fixture. See file header.
    /// </summary>
    public static class SdpNormaliser
    {
        // Capture the *positional* fields of an o= line; per RFC 4566:
        //   o=<username> <sess-id> <sess-version> <nettype> <addrtype> <unicast-address>
        private static readonly Regex s_oLine = new Regex(
            @"^o=(?<user>\S+)\s+\S+\s+\S+\s+(?<net>\S+)\s+(?<addr>\S+)\s+(?<host>\S+)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_mLine = new Regex(
            @"^m=(?<kind>\S+)\s+(?<port>\d+)(?<rest>(\s+.*)?)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_cLine = new Regex(
            @"^c=(?<net>\S+)\s+(?<addr>\S+)\s+(?<host>\S+)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_iceUfrag = new Regex(
            @"^a=ice-ufrag:\S+$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_icePwd = new Regex(
            @"^a=ice-pwd:\S+$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_fingerprint = new Regex(
            @"^a=fingerprint:(?<alg>\S+)\s+\S+$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_ssrc = new Regex(
            @"^a=ssrc:\d+(?<rest>(\s+.*)?)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_ssrcGroup = new Regex(
            @"^a=ssrc-group:(?<sem>\S+)(?<rest>(\s+\d+)+)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        private static readonly Regex s_candidate = new Regex(
            @"^a=candidate:.+$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // a=crypto:<tag> <suite> inline:<key>|<lifetime>|<mki>
        private static readonly Regex s_cryptoInline = new Regex(
            @"^(a=crypto:\S+\s+\S+\s+inline:)\S+",
            RegexOptions.Multiline | RegexOptions.Compiled);

        // cname:<guid|hash> shows up inside a=ssrc lines and (rarely) on
        // its own a=cname: line. CNAMEs are randomised per-session per RFC
        // 3550 so always volatile.
        private static readonly Regex s_cname = new Regex(
            @"cname:\S+",
            RegexOptions.Compiled);

        // a=msid:<stream-id> <track-id> — both ids are volatile.
        private static readonly Regex s_msid = new Regex(
            @"^a=msid:\S+(?<rest>(\s+\S+)*)$",
            RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Returns a normalised copy of the SDP with the volatile fields
        /// listed in the file header replaced by stable placeholders.
        /// </summary>
        public static string Normalise(string sdp)
        {
            if (sdp == null) { return null; }

            // Apply replacements in a fixed order. Each regex is multiline-
            // anchored so they don't bleed across lines.
            string s = sdp.Replace("\r\n", "\n");

            s = s_oLine.Replace(s, m =>
                $"o={m.Groups["user"].Value} <SID> <SVER> {m.Groups["net"].Value} {m.Groups["addr"].Value} {m.Groups["host"].Value}");

            s = s_mLine.Replace(s, m =>
                $"m={m.Groups["kind"].Value} <PORT>{m.Groups["rest"].Value}");

            s = s_cLine.Replace(s, m =>
                $"c={m.Groups["net"].Value} {m.Groups["addr"].Value} <IP>");

            s = s_iceUfrag.Replace(s, "a=ice-ufrag:<UFRAG>");
            s = s_icePwd.Replace(s, "a=ice-pwd:<PWD>");

            s = s_fingerprint.Replace(s, m =>
                $"a=fingerprint:{m.Groups["alg"].Value} <HASH>");

            s = s_ssrc.Replace(s, m =>
                $"a=ssrc:<SSRC>{m.Groups["rest"].Value}");

            s = s_ssrcGroup.Replace(s, m =>
                $"a=ssrc-group:{m.Groups["sem"].Value} <SSRC>");

            s = s_candidate.Replace(s, "a=candidate:<CANDIDATE>");

            s = s_cryptoInline.Replace(s, "$1<CRYPTO_KEY>");

            s = s_cname.Replace(s, "cname:<CNAME>");

            // msid keeps the structure (one or two tokens) but replaces
            // the random ids with stable labels so two runs match.
            s = s_msid.Replace(s, m =>
            {
                // If the line has a track id (two tokens), keep both as placeholders.
                if (!string.IsNullOrEmpty(m.Groups["rest"].Value))
                {
                    return "a=msid:<MSID> <TRACK_ID>";
                }
                return "a=msid:<MSID>";
            });

            return s;
        }

        /// <summary>
        /// Returns the residual SDP with all blank lines removed. Useful
        /// when serializers vary in trailing whitespace between runs.
        /// </summary>
        public static string NormaliseCompact(string sdp)
        {
            string normalised = Normalise(sdp);
            if (normalised == null) { return null; }
            StringBuilder sb = new StringBuilder(normalised.Length);
            foreach (string line in normalised.Split('\n'))
            {
                if (line.Length == 0) { continue; }
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }
    }
}
