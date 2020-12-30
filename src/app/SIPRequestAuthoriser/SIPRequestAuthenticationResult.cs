//-----------------------------------------------------------------------------
// Filename: SIPRequestAuthenticationResult.cs
//
// Description: Holds the results of a SIP request authorisation attempt.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Mar 2009	Aaron Clauson   Created, Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.SIP.App
{
    public class SIPRequestAuthenticationResult
    {
        public bool Authenticated;
        public bool WasAuthenticatedByIP;
        public SIPResponseStatusCodesEnum ErrorResponse;
        public SIPAuthenticationHeader AuthenticationRequiredHeader;

        public SIPRequestAuthenticationResult(bool isAuthenticated, bool wasAuthenticatedByIP)
        {
            Authenticated = isAuthenticated;
            WasAuthenticatedByIP = wasAuthenticatedByIP;
        }

        public SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum errorResponse, SIPAuthenticationHeader authenticationRequiredHeader)
        {
            Authenticated = false;
            ErrorResponse = errorResponse;
            AuthenticationRequiredHeader = authenticationRequiredHeader;
        }
    }
}
