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
    public static partial class SdpNormaliser
    {
        // Capture the *positional* fields of an o= line; per RFC 4566:
        //   o=<username> <sess-id> <sess-version> <nettype> <addrtype> <unicast-address>
        private const string OLinePattern = @"^o=(?<user>\S+)\s+\S+\s+\S+\s+(?<net>\S+)\s+(?<addr>\S+)\s+(?<host>\S+)$";

        private const string MLinePattern = @"^m=(?<kind>\S+)\s+(?<port>\d+)(?<rest>(\s+.*)?)$";

        private const string CLinePattern = @"^c=(?<net>\S+)\s+(?<addr>\S+)\s+(?<host>\S+)$";

        private const string IceUfragPattern = @"^a=ice-ufrag:\S+$";

        private const string IcePwdPattern = @"^a=ice-pwd:\S+$";

        private const string FingerprintPattern = @"^a=fingerprint:(?<alg>\S+)\s+\S+$";

        private const string SsrcPattern = @"^a=ssrc:\d+(?<rest>(\s+.*)?)$";

        private const string SsrcGroupPattern = @"^a=ssrc-group:(?<sem>\S+)(?<rest>(\s+\d+)+)$";

        private const string CandidatePattern = @"^a=candidate:.+$";

        // a=crypto:<tag> <suite> inline:<key>|<lifetime>|<mki>
        private const string CryptoInlinePattern = @"^(a=crypto:\S+\s+\S+\s+inline:)\S+";

        // cname:<guid|hash> shows up inside a=ssrc lines and (rarely) on
        // its own a=cname: line. CNAMEs are randomised per-session per RFC
        // 3550 so always volatile.
        private const string CNamePattern = @"cname:\S+";

        // a=msid:<stream-id> <track-id> — both ids are volatile.
        private const string MsidPattern = @"^a=msid:\S+(?<rest>(\s+\S+)*)$";

#if NET7_0_OR_GREATER
        [GeneratedRegex(OLinePattern, RegexOptions.Multiline)]
        private static partial Regex OLineRegex();

        [GeneratedRegex(MLinePattern, RegexOptions.Multiline)]
        private static partial Regex MLineRegex();

        [GeneratedRegex(CLinePattern, RegexOptions.Multiline)]
        private static partial Regex CLineRegex();

        [GeneratedRegex(IceUfragPattern, RegexOptions.Multiline)]
        private static partial Regex IceUfragRegex();

        [GeneratedRegex(IcePwdPattern, RegexOptions.Multiline)]
        private static partial Regex IcePwdRegex();

        [GeneratedRegex(FingerprintPattern, RegexOptions.Multiline)]
        private static partial Regex FingerprintRegex();

        [GeneratedRegex(SsrcPattern, RegexOptions.Multiline)]
        private static partial Regex SsrcRegex();

        [GeneratedRegex(SsrcGroupPattern, RegexOptions.Multiline)]
        private static partial Regex SsrcGroupRegex();

        [GeneratedRegex(CandidatePattern, RegexOptions.Multiline)]
        private static partial Regex CandidateRegex();

        [GeneratedRegex(CryptoInlinePattern, RegexOptions.Multiline)]
        private static partial Regex CryptoInlineRegex();

        [GeneratedRegex(CNamePattern)]
        private static partial Regex CNameRegex();

        [GeneratedRegex(MsidPattern, RegexOptions.Multiline)]
        private static partial Regex MsidRegex();
#else
        private static readonly Regex s_oLine = new Regex(OLinePattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_mLine = new Regex(MLinePattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_cLine = new Regex(CLinePattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_iceUfrag = new Regex(IceUfragPattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_icePwd = new Regex(IcePwdPattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_fingerprint = new Regex(FingerprintPattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_ssrc = new Regex(SsrcPattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_ssrcGroup = new Regex(SsrcGroupPattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_candidate = new Regex(CandidatePattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_cryptoInline = new Regex(CryptoInlinePattern, RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex s_cname = new Regex(CNamePattern, RegexOptions.Compiled);
        private static readonly Regex s_msid = new Regex(MsidPattern, RegexOptions.Multiline | RegexOptions.Compiled);

        private static Regex OLineRegex() => s_oLine;

        private static Regex MLineRegex() => s_mLine;

        private static Regex CLineRegex() => s_cLine;

        private static Regex IceUfragRegex() => s_iceUfrag;

        private static Regex IcePwdRegex() => s_icePwd;

        private static Regex FingerprintRegex() => s_fingerprint;

        private static Regex SsrcRegex() => s_ssrc;

        private static Regex SsrcGroupRegex() => s_ssrcGroup;

        private static Regex CandidateRegex() => s_candidate;

        private static Regex CryptoInlineRegex() => s_cryptoInline;

        private static Regex CNameRegex() => s_cname;

        private static Regex MsidRegex() => s_msid;
#endif

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

            s = OLineRegex().Replace(s, m =>
                $"o={m.Groups["user"].Value} <SID> <SVER> {m.Groups["net"].Value} {m.Groups["addr"].Value} {m.Groups["host"].Value}");

            s = MLineRegex().Replace(s, m =>
                $"m={m.Groups["kind"].Value} <PORT>{m.Groups["rest"].Value}");

            s = CLineRegex().Replace(s, m =>
                $"c={m.Groups["net"].Value} {m.Groups["addr"].Value} <IP>");

            s = IceUfragRegex().Replace(s, "a=ice-ufrag:<UFRAG>");
            s = IcePwdRegex().Replace(s, "a=ice-pwd:<PWD>");

            s = FingerprintRegex().Replace(s, m =>
                $"a=fingerprint:{m.Groups["alg"].Value} <HASH>");

            s = SsrcRegex().Replace(s, m =>
                $"a=ssrc:<SSRC>{m.Groups["rest"].Value}");

            s = SsrcGroupRegex().Replace(s, m =>
                $"a=ssrc-group:{m.Groups["sem"].Value} <SSRC>");

            s = CandidateRegex().Replace(s, "a=candidate:<CANDIDATE>");

            s = CryptoInlineRegex().Replace(s, "$1<CRYPTO_KEY>");

            s = CNameRegex().Replace(s, "cname:<CNAME>");

            // msid keeps the structure (one or two tokens) but replaces
            // the random ids with stable labels so two runs match.
            s = MsidRegex().Replace(s, m =>
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
