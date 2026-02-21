//-----------------------------------------------------------------------------
// Filename: SIPAuthChallenge.cs
//
// Description: Common logic when having to add auth to SIP Requests
//
// History:
// 10 May 2021  Anonymous       Created.
// 14 Jul 2021  Aaron Clauson   Added ability to choose from a list of challenges.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using SIPSorcery.SIP;

namespace SIPSorcery.SIP
{
    public static class SIPAuthChallenge
    {
        /// <summary>
        /// Attempts to generate a SIP request authentication header from the most appropriate digest challenge.
        /// </summary>
        /// <param name="authenticationChallenges">The challenges to authenticate the request against. Typically the challenges come from a 
        /// SIP response.</param>
        /// <param name="uri">The URI of the SIP request being authenticated.</param>
        /// <param name="method">The method of the SIP request being authenticated.</param>
        /// <param name="username">The username to authenticate with.</param>
        /// <param name="password">The password to authenticate with.</param>
        /// <param name="digestAlgorithm">The digest algorithm to use in the authentication header.</param>
        /// <returns>An authentication header that can be added to a SIP header.</returns>
        public static SIPAuthenticationHeader GetAuthenticationHeader(List<SIPAuthenticationHeader> authenticationChallenges, 
            SIPURI uri,
            SIPMethodsEnum method,
            string username, 
            string password,
            DigestAlgorithmsEnum digestAlgorithm = DigestAlgorithmsEnum.MD5)
        {
            var challenge = authenticationChallenges.First().SIPDigest.CopyOf();
            challenge.DigestAlgorithm = digestAlgorithm;
            challenge.SetCredentials(username, password,uri.ToString(), method.ToString());

            var authHeader = new SIPAuthenticationHeader(challenge);
            authHeader.SIPDigest.Response = challenge.GetDigest();

            return authHeader;
        }
    }
}
