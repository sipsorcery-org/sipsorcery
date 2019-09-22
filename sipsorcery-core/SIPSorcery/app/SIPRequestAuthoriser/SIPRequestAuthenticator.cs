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
// Copyright (c) 2009 Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery PTY LTD, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery PTY LTD. 
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
using System.Text.RegularExpressions;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPRequestAuthenticator
    {
        private const int NONCE_REFRESH_SECONDS = 120;

        private static ILog logger = AppState.logger;

        private static string m_previousNoncePrefix = null;
        private static string m_currentNoncePrefix = null;
        private static DateTime m_lastNoncePrefixUpdate = DateTime.Now;

        /// <summary>
        /// Authenticates a SIP request.
        /// </summary>
        public static SIPRequestAuthenticationResult AuthenticateSIPRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest, SIPAccount sipAccount, SIPMonitorLogDelegate logSIPMonitorEvent)
        {
            try
            {
                if (sipAccount == null)
                {
                    return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Forbidden, null);
                }
                else if (sipAccount.IsDisabled)
                {
                    if (logSIPMonitorEvent != null)
                    {
                        logSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Authoriser, SIPMonitorEventTypesEnum.DialPlan, "SIP account " + sipAccount.SIPUsername + "@" + sipAccount.SIPDomain + " is disabled for " + sipRequest.Method + ".", sipAccount.Owner));
                    }
                    return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Forbidden, null);
                }
                else
                {
                    SIPAuthenticationHeader reqAuthHeader = sipRequest.Header.AuthenticationHeader;
                    if (reqAuthHeader == null)
                    {
                        // Check for IP address authentication.
                        if (!sipAccount.IPAddressACL.IsNullOrBlank())
                        {
                            SIPEndPoint uaEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : remoteEndPoint;
                            if (Regex.Match(uaEndPoint.GetIPEndPoint().ToString(), sipAccount.IPAddressACL).Success)
                            {
                                // Successfully authenticated
                                return new SIPRequestAuthenticationResult(true, true);
                            }
                        }

                        SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, sipAccount.SIPDomain, GetNonce());
                        return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Unauthorised, authHeader);
                    }
                    else
                    {
                        // Check for IP address authentication.
                        if (!sipAccount.IPAddressACL.IsNullOrBlank())
                        {
                            SIPEndPoint uaEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : remoteEndPoint;
                            if (Regex.Match(uaEndPoint.GetIPEndPoint().ToString(), sipAccount.IPAddressACL).Success)
                            {
                                // Successfully authenticated
                                return new SIPRequestAuthenticationResult(true, true);
                            }
                        }

                        string requestNonce = reqAuthHeader.SIPDigest.Nonce;
                        string uri = reqAuthHeader.SIPDigest.URI;
                        string response = reqAuthHeader.SIPDigest.Response;

                        // Check for stale nonces.
                        if (IsNonceStale(requestNonce))
                        {
                            if (logSIPMonitorEvent != null)
                            {
                                logSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Authoriser, SIPMonitorEventTypesEnum.Warn, "Authentication failed stale nonce for realm=" + sipAccount.SIPDomain + ", username=" + sipAccount.SIPUsername + ", uri=" + uri + ", nonce=" + requestNonce + ", method=" + sipRequest.Method + ".", null));
                            }
                            SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, sipAccount.SIPDomain, GetNonce());
                            return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Unauthorised, authHeader);
                        }
                        else
                        {
                            SIPAuthorisationDigest checkAuthReq = reqAuthHeader.SIPDigest;
                            checkAuthReq.SetCredentials(sipAccount.SIPUsername, sipAccount.SIPPassword, uri, sipRequest.Method.ToString());
                            string digest = checkAuthReq.Digest;

                            if (digest == response)
                            {
                                // Successfully authenticated
                                return new SIPRequestAuthenticationResult(true, false);
                            }
                            else
                            {
                                if (logSIPMonitorEvent != null)
                                {
                                    logSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Authoriser, SIPMonitorEventTypesEnum.Warn, "Authentication token check failed for realm=" + sipAccount.SIPDomain + ", username=" + sipAccount.SIPUsername + ", uri=" + uri + ", nonce=" + requestNonce + ", method=" + sipRequest.Method + ".", sipAccount.Owner));
                                }
                                SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, sipAccount.SIPDomain, GetNonce());
                                return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Unauthorised, authHeader);
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                if (logSIPMonitorEvent != null)
                {
                    logSIPMonitorEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Authoriser, SIPMonitorEventTypesEnum.Error, "Exception AuthoriseSIPRequest. " + excp.Message, null));
                }
                return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.InternalServerError, null);
            }
        }

        public static string GetNonce()
        {
            if (m_currentNoncePrefix == null || DateTime.Now.Subtract(m_lastNoncePrefixUpdate).TotalSeconds > NONCE_REFRESH_SECONDS)
            {
                m_lastNoncePrefixUpdate = DateTime.Now;
                m_previousNoncePrefix = m_currentNoncePrefix;
                m_currentNoncePrefix = Crypto.GetRandomInt().ToString();
            }

            return m_currentNoncePrefix + Crypto.GetRandomInt().ToString();
        }

        private static bool IsNonceStale(string nonce)
        {
            if (nonce.IsNullOrBlank())
            {
                return true;
            }
            else if (m_currentNoncePrefix != null && nonce.StartsWith(m_currentNoncePrefix))
            {
                return false;
            }
            else if (m_previousNoncePrefix != null && nonce.StartsWith(m_previousNoncePrefix))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
