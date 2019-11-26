//-----------------------------------------------------------------------------
// Filename: SIPRequestAuthenticationResult.cs
//
// Description: Holds the results of a SIP request authorisation attempt.
// 
// History:
// 08 Mar 2009	Aaron Clauson   Created (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.SIP
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
