//-----------------------------------------------------------------------------
// Filename: SIPRequestAuthoriser.cs
//
// Description: Central location to handle SIP Request authorisation.
// 
// History:
// 08 Mar 2009	Aaron Clauson	    Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPRequestAuthoriser
    {
        private static ILog logger = AssemblyState.logger;

        private SIPMonitorLogDelegate Log_External = SIPMonitorEvent.DefaultSIPMonitorLogger;   // Function to log messages from this core.
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        private GetSIPAccountDelegate GetSIPAccount_External;                                   // Function in authenticate user outgoing calls.

        public SIPRequestAuthoriser(
            SIPMonitorLogDelegate logSIPMonitorEvent_External,
            GetCanonicalDomainDelegate getCanonicalDomain,
            GetSIPAccountDelegate getSIPAccount)
        {
            Log_External = logSIPMonitorEvent_External ?? Log_External;
            GetCanonicalDomain_External = getCanonicalDomain;
            GetSIPAccount_External = getSIPAccount;
        }

        /// <summary>
        /// Attempts to authorise a SIP request.
        /// </summary>
        public SIPRequestAuthorisationResult AuthoriseSIPRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest)
        {
            try
            {
                SIPAuthenticationHeader reqAuthHeader = sipRequest.Header.AuthenticationHeader;
                if (reqAuthHeader == null)
                {
                    string fromHeaderDomain = (sipRequest.Header.From != null) ? sipRequest.Header.From.FromURI.Host : null;
                    string realm = GetCanonicalDomain_External(fromHeaderDomain);
                    SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, realm, Crypto.GetRandomInt().ToString());
                    return new SIPRequestAuthorisationResult(SIPResponseStatusCodesEnum.Unauthorised, authHeader);
                }
                else
                {
                    // The definitive username and realm are those from the authorisation header NOT the From or any other header.
                    string user = reqAuthHeader.SIPDigest.Username;
                    string realm = reqAuthHeader.SIPDigest.Realm;
                    SIPAccount sipAccount = GetSIPAccount_External(user, GetCanonicalDomain_External(realm));

                    if (sipAccount != null)
                    {
                        if (sipAccount.IsDisabled) {
                            Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Authoriser, SIPMonitorEventTypesEnum.DialPlan, "SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " is disabled for." , sipAccount.Owner));
                            return new SIPRequestAuthorisationResult(SIPResponseStatusCodesEnum.Forbidden, null);
                        }
                        else 
                        {
                            string requestNonce = reqAuthHeader.SIPDigest.Nonce;
                            string uri = reqAuthHeader.SIPDigest.URI;
                            string response = reqAuthHeader.SIPDigest.Response;

                            SIPAuthorisationDigest checkAuthReq = reqAuthHeader.SIPDigest;
                            checkAuthReq.SetCredentials(user, sipAccount.SIPPassword, uri, sipRequest.Method.ToString());
                            string digest = checkAuthReq.Digest;

                            if (digest == response)
                            {
                                // Successfully authenticated
                                return new SIPRequestAuthorisationResult(true, sipAccount.SIPUsername, sipAccount.SIPDomain);
                            }
                            else
                            {
                                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Authoriser, SIPMonitorEventTypesEnum.DialPlan, "Authentication token check failed for realm=" + realm + ", username=" + user + ", uri=" + uri + ", nonce=" + requestNonce + ", method=" + sipRequest.Method + ".", user));
                                SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, realm, Crypto.GetRandomInt().ToString());
                                return new SIPRequestAuthorisationResult(SIPResponseStatusCodesEnum.Unauthorised, authHeader);
                            }
                        }
                    }
                    else
                    {
                        Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Authoriser, SIPMonitorEventTypesEnum.DialPlan, "No configuration found for " + user + " returning 501 ServerError to " + remoteEndPoint + ".", user));
                        return new SIPRequestAuthorisationResult(SIPResponseStatusCodesEnum.Forbidden, null);
                    }
                }
            }
            catch (Exception excp)
            {
                Log_External(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Authoriser, SIPMonitorEventTypesEnum.Error, "Exception AuthoriseSIPRequest. " + excp.Message, null));
                return new SIPRequestAuthorisationResult(SIPResponseStatusCodesEnum.InternalServerError, null);
            }
        }
    }
}
