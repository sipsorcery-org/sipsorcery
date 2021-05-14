//-----------------------------------------------------------------------------
// Filename: SIPAuthChallenge.cs
//
// Description: Common logic when having to add auth to SIP Requests
//
// History:
// 10 May 2021 : Created 
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------
using System.Linq;
using SIPSorcery.SIP;


namespace SIPSorcery.App.SIPUserAgents.Behaviours
{
    public static class SIPAuthChallenge
    {
        public static SIPRequest AddAuthenticationHeaderToRequest(SIPRequest previousSipRequest, SIPResponse sipResponse, string username, string password)
        {
            // Resend Invite with credentials.
            SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
            
            authRequest.SetCredentials(username, password, previousSipRequest.URI.ToString(), previousSipRequest.Method.ToString());

            previousSipRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
            previousSipRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;
            previousSipRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
            previousSipRequest.Header.CSeq = previousSipRequest.Header.CSeq + 1;

            return previousSipRequest;
        }
    }
}
