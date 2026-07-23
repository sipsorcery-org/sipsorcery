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

#nullable disable

using System.Collections.Generic;
using System.Linq;

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
            return GetAuthenticationHeader(challenge);
        }

        /// <summary>
        /// Attempts to generate a SIP request authentication header from a digest challenge with a resolvable HA1 digest.
        /// </summary>
        /// <param name="authenticationChallenges">The challenges to authenticate the request against. Typically the challenges come from a
        /// SIP response.</param>
        /// <param name="uri">The URI of the SIP request being authenticated.</param>
        /// <param name="method">The method of the SIP request being authenticated.</param>
        /// <param name="username">The username to authenticate with.</param>
        /// <param name="getHA1Digest">Resolves an HA1 digest for a username, challenge realm and digest algorithm.
        /// Return <c>null</c> when no credential is available for a challenge.</param>
        /// <returns>An authentication header that can be added to a SIP header.</returns>
        public static SIPAuthenticationHeader GetAuthenticationHeader(List<SIPAuthenticationHeader> authenticationChallenges,
            SIPURI uri,
            SIPMethodsEnum method,
            string username,
            GetHA1DigestDelegate getHA1Digest)
        {
            // Prefer SHA-256 when available, matching the existing stored-digest behaviour.
            foreach (DigestAlgorithmsEnum digestAlgorithm in new[] { DigestAlgorithmsEnum.SHA256, DigestAlgorithmsEnum.MD5 })
            {
                foreach (SIPAuthenticationHeader authenticationChallenge in authenticationChallenges.Where(x =>
                    (x.SIPDigest != null) &&
                    (x.SIPDigest.DigestAlgorithm == digestAlgorithm)))
                {
                    SIPAuthorisationDigest challenge = authenticationChallenge.SIPDigest.CopyOf();
                    string ha1Digest = getHA1Digest(username, challenge.Realm, challenge.DigestAlgorithm);

                    if (ha1Digest == null)
                    {
                        continue;
                    }

                    challenge.Username = username;
                    challenge.SetCredentials(ha1Digest, uri.ToString(), method.ToString());
                    return GetAuthenticationHeader(challenge);
                }
            }

            return null;
        }

        public static SIPAuthenticationHeader GetAuthenticationHeader(SIPAuthorisationDigest challenge)
        {
            var authHeader = new SIPAuthenticationHeader(challenge);
            authHeader.SIPDigest.Response = challenge.GetDigest();

            return authHeader;
        }
    }
}
