//-----------------------------------------------------------------------------
// Filename: SIPReplacesParameter.cs
//
// Description: Represents the Replaces parameter on a Refer-To header. The Replaces parameter
// is used to identify involved in a transfer operation.
//
// Author(s):
// Aaron Clauson
//
// History:
// 26 Sep 2011	Aaron Clauson	Created (aaron@sipsorcery.com), SIPSorcery Ltd, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace SIPSorcery.SIP
{
    public partial class SIPReplacesParameter
    {
        public string CallID;
        public string ToTag;
        public string FromTag;
        private const string CALL_ID_REGEX_PATTERN = "^(?<callid>.*?);";
        private const string TO_TAG_REGEX_PATTERN = "to-tag=(?<totag>.*?)(;|$)";
        private const string FROM_TAG_REGEX_PATTERN = "from-tag=(?<fromtag>.*?)(;|$)";

#if NET7_0_OR_GREATER
        [GeneratedRegex(CALL_ID_REGEX_PATTERN)]
        private static partial Regex CallIDRegex();

        [GeneratedRegex(TO_TAG_REGEX_PATTERN, RegexOptions.IgnoreCase)]
        private static partial Regex ToTagRegex();

        [GeneratedRegex(FROM_TAG_REGEX_PATTERN, RegexOptions.IgnoreCase)]
        private static partial Regex FromTagRegex();
#else
        private static readonly Regex m_callIdRegex = new Regex(CALL_ID_REGEX_PATTERN, RegexOptions.Compiled);
        private static readonly Regex m_toTagRegex = new Regex(TO_TAG_REGEX_PATTERN, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex m_fromTagRegex = new Regex(FROM_TAG_REGEX_PATTERN, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static Regex CallIDRegex() => m_callIdRegex;
        private static Regex ToTagRegex() => m_toTagRegex;
        private static Regex FromTagRegex() => m_fromTagRegex;
#endif

        public static SIPReplacesParameter Parse(string replaces)
        {
            var callIDMatch = CallIDRegex().Match(replaces);
            if (replaces.IndexOf(';') != -1)
            {
                var toTagMatch = ToTagRegex().Match(replaces);
                var fromTagMatch = FromTagRegex().Match(replaces);

                if (toTagMatch.Success && fromTagMatch.Success)
                {
                    SIPReplacesParameter replacesParam = new SIPReplacesParameter();
                    replacesParam.CallID = replaces.Substring(0, replaces.IndexOf(';'));
                    replacesParam.ToTag = toTagMatch.Result("${totag}");
                    replacesParam.FromTag = fromTagMatch.Result("${fromtag}");

                    return replacesParam;
                }
            }

            return null;
        }
    }
}
