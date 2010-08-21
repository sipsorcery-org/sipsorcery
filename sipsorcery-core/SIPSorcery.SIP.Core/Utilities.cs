//-----------------------------------------------------------------------------
// Filename: Utilities.cs
//
// Description: Useful functions for VoIP protocol implementation.
//
// History:
// 23 May 2005	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of Blue Face Ltd. 
// nor the names of its contributors may be used to endorse or promote products derived from this software without specific 
// prior written permission. 
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, 
// BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. 
// IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, 
// OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, 
// OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, 
// OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE 
// POSSIBILITY OF SUCH DAMAGE.
//-----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

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
