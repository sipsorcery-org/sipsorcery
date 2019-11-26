//-----------------------------------------------------------------------------
// Filename: SIPRequestAuthorisationResult.cs
//
// Description: Holds the results of a SIP request authorisation attempt.
//
// Author(s):
// Aaron Clauson
// 
// History:
// 08 Mar 2009	Aaron Clauson   Created (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie).
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

namespace SIPSorcery.SIP
{
    public class SIPRequestAuthorisationResult
    {
        public bool Authorised;
        public string SIPUsername;
        public string SIPDomain;
        public SIPResponseStatusCodesEnum ErrorResponse;
        public SIPAuthenticationHeader AuthenticationRequiredHeader;

        public SIPRequestAuthorisationResult(bool authorised, string sipUsername, string sipDomain)
        {
            Authorised = authorised;
            SIPUsername = sipUsername;
            SIPDomain = sipDomain;
        }

        public SIPRequestAuthorisationResult(SIPResponseStatusCodesEnum errorResponse, SIPAuthenticationHeader authenticationRequiredHeader)
        {
            ErrorResponse = errorResponse;
            AuthenticationRequiredHeader = authenticationRequiredHeader;
        }
    }
}
