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

        [GeneratedRegex("^(?<callid>.*?);")]
        private static partial Regex CallIDRegex();

        [GeneratedRegex("to-tag=(?<totag>.*?)(;|$)", RegexOptions.IgnoreCase)]
        private static partial Regex ToTagRegex();

        [GeneratedRegex("from-tag=(?<fromtag>.*?)(;|$)", RegexOptions.IgnoreCase)]
        private static partial Regex FromTagRegex();

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
