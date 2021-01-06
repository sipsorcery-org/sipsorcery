//-----------------------------------------------------------------------------
// Filename: CallProperties.cs
//
// Description: Helper functions for setting SIP headers.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
// 
// History:
// 23 May 2005	Aaron Clauson	Created, Dublin, Ireland.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP
{
    public class CallProperties
    {
        public static string CreateNewCallId()
        {
            Guid callIdGuid = Guid.NewGuid();

            string callIdStr = Regex.Replace(callIdGuid.ToString(), "-", "");

            return callIdStr;
        }

        public static string CreateNewTag()
        {
            return Crypto.GetRandomString(10);
        }

        /// <summary>
        /// From RFC 3261, Section 16.6, Step 8.
        /// The value placed in this part of the branch parameter SHOULD reflect all of those fields (including any Route, Proxy-Require and Proxy-
        /// Authorization header fields).  This is to ensure that if the request is routed back to the proxy and one of those fields
        /// changes, it is treated as a spiral and not a loop (see Section 16.3). 
        /// A common way to create this value is to compute a cryptographic hash of the To tag, From tag, Call-ID header
        /// field, the Request-URI of the request received (before translation), the topmost Via header, and the sequence number
        ///  from the CSeq header field, in addition to any Proxy-Require algorithm used to compute the hash is implementation-dependent,
        /// but MD5 (RFC 1321 [35]), expressed in hexadecimal, is a  reasonable choice.  (Base64 is not permissible for a token.)
        /// </summary>
        /// <returns></returns>
        public static string CreateBranchId(string magicCookie, string toTag, string fromTag, string callId, string uri, string topVia, int cSeq, string route, string proxyRequire, string proxyAuth)
        {
            string plainTextBranch = toTag + fromTag + callId + uri + topVia + cSeq.ToString() + route + proxyRequire + proxyAuth;

            string cypherTextBranch = Crypto.GetSHAHashAsHex(plainTextBranch);

            string branchId = magicCookie + cypherTextBranch;

            return branchId;
        }

        public static string CreateBranchId()
        {
            return SIPConstants.SIP_BRANCH_MAGICCOOKIE + Regex.Replace(Guid.NewGuid().ToString(), "-", "");
        }
    }
}
