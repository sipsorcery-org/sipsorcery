//-----------------------------------------------------------------------------
// Filename: SIPRequestAuthenticator.cs
//
// Description: Central location to handle SIP Request authorisation.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 08 Mar 2009	Aaron Clauson   Created Hobart, Australia.
//
// License: 
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
//-----------------------------------------------------------------------------

using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SIPRequestAuthenticator
    {
        private const int NONCE_REFRESH_SECONDS = 120;

        private static ILogger logger = Log.Logger;

        private static string m_previousNoncePrefix = null;
        private static string m_currentNoncePrefix = null;
        private static DateTime m_lastNoncePrefixUpdate = DateTime.Now;

        /// <summary>
        /// Authenticates a SIP request.
        /// </summary>
        public static SIPRequestAuthenticationResult AuthenticateSIPRequest(
            SIPEndPoint localSIPEndPoint, 
            SIPEndPoint remoteEndPoint, 
            SIPRequest sipRequest, 
            ISIPAccount sipAccount)
        {
            try
            {
                if (sipAccount == null)
                {
                    return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Forbidden, null);
                }
                else if (sipAccount.IsDisabled)
                {
                    logger.LogWarning($"SIP account {sipAccount.SIPUsername}@{sipAccount.SIPDomain} is disabled for {sipRequest.Method}.");

                    return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Forbidden, null);
                }
                else
                {
                    SIPAuthenticationHeader reqAuthHeader = sipRequest.Header.AuthenticationHeader;
                    if (reqAuthHeader == null)
                    {
                        // Check for IP address authentication.
                        //if (!sipAccount.IPAddressACL.IsNullOrBlank())
                        //{
                        //    SIPEndPoint uaEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : remoteEndPoint;
                        //    if (Regex.Match(uaEndPoint.GetIPEndPoint().ToString(), sipAccount.IPAddressACL).Success)
                        //    {
                        //        // Successfully authenticated
                        //        return new SIPRequestAuthenticationResult(true, true);
                        //    }
                        //}

                        SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, sipAccount.SIPDomain, GetNonce());
                        return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Unauthorised, authHeader);
                    }
                    else
                    {
                        // Check for IP address authentication.
                        //if (!sipAccount.IPAddressACL.IsNullOrBlank())
                        //{
                        //    SIPEndPoint uaEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : remoteEndPoint;
                        //    if (Regex.Match(uaEndPoint.GetIPEndPoint().ToString(), sipAccount.IPAddressACL).Success)
                        //    {
                        //        // Successfully authenticated
                        //        return new SIPRequestAuthenticationResult(true, true);
                        //    }
                        //}

                        string requestNonce = reqAuthHeader.SIPDigest.Nonce;
                        string uri = reqAuthHeader.SIPDigest.URI;
                        string response = reqAuthHeader.SIPDigest.Response;

                        // Check for stale nonces.
                        if (IsNonceStale(requestNonce))
                        {
                            logger.LogWarning($"Authentication failed stale nonce for realm={sipAccount.SIPDomain}, username={sipAccount.SIPUsername}, uri={uri}, nonce={requestNonce}, method={sipRequest.Method}.");

                            SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, sipAccount.SIPDomain, GetNonce());
                            return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Unauthorised, authHeader);
                        }
                        else
                        {
                            SIPAuthorisationDigest checkAuthReq = reqAuthHeader.SIPDigest;

                            if (sipAccount.SIPPassword != null)
                            {
                                checkAuthReq.SetCredentials(sipAccount.SIPUsername, sipAccount.SIPPassword, uri, sipRequest.Method.ToString());
                            }
                            else if(sipAccount.HA1Digest != null)
                            {
                                checkAuthReq.SetCredentials(sipAccount.HA1Digest, uri, sipRequest.Method.ToString());
                            }
                            else
                            {
                                throw new ApplicationException("SIP authentication cannot be attempted as neither a password or HA1 digest are available.");
                            }

                            string digest = checkAuthReq.Digest;

                            if (digest == response)
                            {
                                // Successfully authenticated
                                return new SIPRequestAuthenticationResult(true, false);
                            }
                            else
                            {
                                logger.LogWarning("Authentication token check failed for realm=" + sipAccount.SIPDomain + ", username=" + sipAccount.SIPUsername + ", uri=" + uri + ", nonce=" + requestNonce + ", method=" + sipRequest.Method + ".");
                                
                                SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, sipAccount.SIPDomain, GetNonce());
                                return new SIPRequestAuthenticationResult(SIPResponseStatusCodesEnum.Unauthorised, authHeader);
                            }
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError(0, excp, "Exception AuthoriseSIPRequest. " + excp.Message);
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
