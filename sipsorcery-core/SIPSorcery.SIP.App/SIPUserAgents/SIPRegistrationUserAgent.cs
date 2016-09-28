// ============================================================================
// FileName: SIPRegistrationUserAgent.cs
//
// Description:
// A user agent that can register and maintain a binding with a SIP Registrar.
//
// Author(s):
// Aaron Clauson
//
// History:
// 03 Mar 2010	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2010 Aaron Clauson (aaron@sipsorcery.com), SIPSorcery Ltd, London, UK (www.sipsorcery.com)
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
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPRegistrationUserAgent
    {
        private const int MAX_EXPIRY = 7200;
        private const int MAX_REGISTRATION_ATTEMPT_TIMEOUT = 60;
        private const int REGISTRATION_HEAD_TIME = 5;                // Time in seconds to go to next registration to initate.
        private const int REGISTER_FAILURERETRY_INTERVAL = 300;      // Number of seconds between consecutive register requests in the event of failures or timeouts.
        private const int REGISTER_MINIMUM_EXPIRY = 60;              // The minimum interval a registration will be accepted for. Anything less than this interval will use this minimum value.
        private const int DEFAULT_REGISTER_EXPIRY = 600;
        private const int MAX_REGISTER_ATTEMPTS = 3;                 // The maximum number of registration attempts that will be made without a failure condition before incurring a temporary failure.

        private static ILog logger = AppState.logger;

        private static readonly string m_userAgent = SIPConstants.SIP_USERAGENT_STRING;

        private SIPMonitorLogDelegate Log_External;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;
        private SIPEndPoint m_localEndPoint;
        private SIPURI m_sipAccountAOR;
        private string m_authUsername;
        private string m_password;
        private string m_realm;
        private string m_registrarHost;
        private SIPURI m_contactURI;
        private int m_expiry;
        private string m_owner;
        private string m_adminMemberID;

        private bool m_isRegistered;
        private int m_cseq;
        private string m_callID;
        private bool m_exit;
        private int m_attempts;
        private string m_lastServerNonce;
        private ManualResetEvent m_waitForRegistrationMRE = new ManualResetEvent(false);

        public string UserAgent;                // If not null this value will replace the default user agent value in the REGISTER request.
        //public bool RequestSwitchboardToken;    // If set to true a header will be set on the REGISTER request that asks the server to return a toekn that can be used by the switchboard for 3rd party authorisation.
        //public string SwitchboardToken;         // If a switchboard token is provided by the server it will be placed here.

        public event Action<SIPURI, string> RegistrationFailed;
        public event Action<SIPURI, string> RegistrationTemporaryFailure;
        public event Action<SIPURI> RegistrationSuccessful;
        public event Action<SIPURI> RegistrationRemoved;

        public SIPRegistrationUserAgent(
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPEndPoint localEndPoint,
            SIPURI sipAccountAOR,
            string authUsername,
            string password,
            string realm,
            string registrarHost,
            SIPURI contactURI,
            int expiry,
            string owner,
            string adminMemberID,
            SIPMonitorLogDelegate logDelegate)
        {
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_localEndPoint = localEndPoint;
            m_sipAccountAOR = sipAccountAOR;
            m_authUsername = authUsername;
            m_password = password;
            m_realm = realm;
            m_registrarHost = registrarHost;
            m_contactURI = contactURI;
            m_expiry = (expiry >= REGISTER_MINIMUM_EXPIRY && expiry <= MAX_EXPIRY) ? expiry : DEFAULT_REGISTER_EXPIRY;
            m_owner = owner;
            m_adminMemberID = adminMemberID;
            m_callID = Guid.NewGuid().ToString();

            Log_External = logDelegate;
        }

        public void Start()
        {
            ThreadPool.QueueUserWorkItem(delegate { StartRegistration(); });
        }

        public void StartRegistration()
        {
            try
            {
                logger.Debug("Starting SIPRegistrationUserAgent for " + m_sipAccountAOR.ToString() + ".");

                while (!m_exit)
                {
                    m_waitForRegistrationMRE.Reset();
                    m_attempts = 0;

                    SendInitialRegister();

                    if (!m_waitForRegistrationMRE.WaitOne(MAX_REGISTRATION_ATTEMPT_TIMEOUT * 1000))
                    {
                        m_isRegistered = false;

                        if (!m_exit && RegistrationTemporaryFailure != null)
                        {
                            RegistrationTemporaryFailure(m_sipAccountAOR, "Registration to " + m_registrarHost + " for " + m_sipAccountAOR.ToString() + " timed out.");
                        }
                    }

                    if (!m_exit && m_isRegistered)
                    {
                        logger.Debug("SIPRegistrationUserAgent was successful, scheduling next registration to " + m_sipAccountAOR.ToString() + " in " + (m_expiry - REGISTRATION_HEAD_TIME) + "s.");
                        Thread.Sleep((m_expiry - REGISTRATION_HEAD_TIME) * 1000);
                    }
                    else
                    {
                        logger.Debug("SIPRegistrationUserAgent temporarily failed, scheduling next registration to " + m_sipAccountAOR.ToString() + " in " + REGISTER_FAILURERETRY_INTERVAL + "s.");
                        Thread.Sleep(REGISTER_FAILURERETRY_INTERVAL * 1000);
                    }
                }

                logger.Debug("SIPRegistrationUserAgent for " + m_sipAccountAOR.ToString() + " stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationUserAgent Start. " + excp.Message);
            }
        }

        public void Stop()
        {
            try
            {
                if (!m_exit)
                {
                    logger.Debug("Stopping SIP registration user agent for " + m_sipAccountAOR.ToString() + ".");

                    m_exit = true;
                    m_waitForRegistrationMRE.Set();

                    if (m_isRegistered)
                    {
                        m_attempts = 0;
                        m_expiry = 0;
                        ThreadPool.QueueUserWorkItem(delegate { SendInitialRegister(); });
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationUserAgent Stop. " + excp.Message);
            }
        }

        private void SendInitialRegister()
        {
            try
            {
                if (m_attempts >= MAX_REGISTER_ATTEMPTS)
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration to " + m_sipAccountAOR.ToString() + " reached the maximum number of allowed attempts without a failure condition.", m_owner));
                    m_isRegistered = false;
                    if (RegistrationTemporaryFailure != null)
                    {
                        RegistrationTemporaryFailure(m_sipAccountAOR, "Registration reached the maximum number of allowed attempts.");
                    }
                    m_waitForRegistrationMRE.Set();
                }
                else
                {
                    m_attempts++;

                    SIPEndPoint registrarSIPEndPoint = m_outboundProxy ;
                    if(registrarSIPEndPoint == null) 
                    {
                        SIPDNSLookupResult lookupResult = m_sipTransport.GetHostEndPoint(m_registrarHost, false);
                        if (lookupResult.LookupError != null)
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Could not resolve " + m_registrarHost + ", " + lookupResult.LookupError, m_owner));
                        }
                        else
                        {
                            registrarSIPEndPoint = lookupResult.GetSIPEndPoint();
                        }
                    }

                    if (registrarSIPEndPoint == null && RegistrationFailed != null)
                    {
                        RegistrationFailed(m_sipAccountAOR, "Could not resolve " + m_registrarHost + ".");
                    }
                    else
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Initiating registration to " + m_registrarHost + " at " + registrarSIPEndPoint.ToString() + " for " + m_sipAccountAOR.ToString() + ".", m_owner));
                        SIPRequest regRequest = GetRegistrationRequest(m_localEndPoint);

                        SIPNonInviteTransaction regTransaction = m_sipTransport.CreateNonInviteTransaction(regRequest, registrarSIPEndPoint, m_localEndPoint, m_outboundProxy);
                        // These handlers need to be on their own threads to take the processing off the SIP transport layer.
                        regTransaction.NonInviteTransactionFinalResponseReceived += (lep, rep, tn, rsp) => { ThreadPool.QueueUserWorkItem(delegate { ServerResponseReceived(lep, rep, tn, rsp); }); };
                        regTransaction.NonInviteTransactionTimedOut += (tn) => { ThreadPool.QueueUserWorkItem(delegate { RegistrationTimedOut(tn); }); };

                        m_sipTransport.SendSIPReliable(regTransaction);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendInitialRegister to " + m_registrarHost + ". " + excp.Message);
                if (RegistrationFailed != null)
                {
                    RegistrationFailed(m_sipAccountAOR, "Exception SendInitialRegister to " + m_registrarHost + ". " + excp.Message);
                }
            }
        }

        private void RegistrationTimedOut(SIPTransaction sipTransaction)
        {
            m_isRegistered = false;
            if (RegistrationTemporaryFailure != null)
            {
                RegistrationTemporaryFailure(m_sipAccountAOR, "Registration transaction to " + m_registrarHost + " for " + m_sipAccountAOR.ToString() + " timed out.");
            }
            m_waitForRegistrationMRE.Set();
        }

        /// <summary>
        /// The event handler for responses to the initial register request.
        /// </summary>
        private void ServerResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Server response " + sipResponse.Status + " received for " + m_sipAccountAOR.ToString() + ".", m_owner));

                if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    if (sipResponse.Header.AuthenticationHeader != null)
                    {
                        if (m_attempts >= MAX_REGISTER_ATTEMPTS)
                        {
                            Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration to " + m_sipAccountAOR.ToString() + " reached the maximum number of allowed attempts without a failure condition.", m_owner));
                            m_isRegistered = false;
                            if (RegistrationTemporaryFailure != null)
                            {
                                RegistrationTemporaryFailure(m_sipAccountAOR, "Registration reached the maximum number of allowed attempts.");
                            }
                            m_waitForRegistrationMRE.Set();
                        }
                        else
                        {
                            m_attempts++;
                            SIPRequest authenticatedRequest = GetAuthenticatedRegistrationRequest(sipTransaction.TransactionRequest, sipResponse);
                            SIPNonInviteTransaction regAuthTransaction = m_sipTransport.CreateNonInviteTransaction(authenticatedRequest, sipTransaction.RemoteEndPoint, localSIPEndPoint, m_outboundProxy);
                            regAuthTransaction.NonInviteTransactionFinalResponseReceived += (lep, rep, tn, rsp) => { ThreadPool.QueueUserWorkItem(delegate { AuthResponseReceived(lep, rep, tn, rsp); }); };
                            regAuthTransaction.NonInviteTransactionTimedOut += (tn) => { ThreadPool.QueueUserWorkItem(delegate { RegistrationTimedOut(tn); }); };
                            m_sipTransport.SendSIPReliable(regAuthTransaction);
                        }
                    }
                    else
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " but no authentication header was supplied for " + m_sipAccountAOR.ToString() + ".", m_owner));
                        m_isRegistered = false;
                        if (RegistrationTemporaryFailure != null)
                        {
                            RegistrationTemporaryFailure(m_sipAccountAOR, "Registration failed with " + sipResponse.Status + " but no authentication header was supplied.");
                        }
                        m_waitForRegistrationMRE.Set();
                    }
                }
                else
                {
                    if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
                    {
                        if (m_expiry > 0)
                        {
                            m_isRegistered = true;
                            m_expiry = GetUpdatedExpiry(sipResponse);
                            //if (sipResponse.Header.SwitchboardToken != null && m_lastServerNonce != null)
                            //{
                            //    SwitchboardToken = Crypto.SymmetricDecrypt(m_password, m_lastServerNonce, sipResponse.Header.SwitchboardToken);
                            //}
                            RegistrationSuccessful(m_sipAccountAOR);
                        }
                        else
                        {
                            m_isRegistered = false;
                            RegistrationRemoved(m_sipAccountAOR);
                        }

                        m_waitForRegistrationMRE.Set();
                    }
                    else if (sipResponse.Status == SIPResponseStatusCodesEnum.Forbidden || sipResponse.Status == SIPResponseStatusCodesEnum.NotFound)
                    {
                        // SIP account does not appear to exist.
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + m_sipAccountAOR.ToString() + ", no further registration attempts will be made.", m_owner));
                        string reasonPhrase = (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase;
                        if (RegistrationFailed != null)
                        {
                            RegistrationFailed(m_sipAccountAOR, "Registration failed with " + (int)sipResponse.Status + " " + reasonPhrase + ".");
                        }
                        m_exit = true;
                        m_waitForRegistrationMRE.Set();
                    }
                    else if (sipResponse.Status == SIPResponseStatusCodesEnum.IntervalTooBrief && m_expiry != 0)
                    {
                        m_expiry = GetUpdatedExpiryForIntervalTooBrief(sipResponse);
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Registration for " + m_sipAccountAOR.ToString() + " had a too short expiry, updated to +" + m_expiry + " and trying again.", m_owner));
                        SendInitialRegister();
                    }
                    else
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + m_sipAccountAOR.ToString() + ".", m_owner));
                        m_isRegistered = false;
                        if (RegistrationTemporaryFailure != null)
                        {
                            RegistrationTemporaryFailure(m_sipAccountAOR, "Registration failed with " + sipResponse.Status + ".");
                        }
                        m_waitForRegistrationMRE.Set();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationUserAgent ServerResponseReceived (" + remoteEndPoint + "). " + excp.Message);
            }
        }

        /// <summary>
        /// The event handler for responses to the authenticated register request.
        /// </summary>
        private void AuthResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Server auth response " + sipResponse.Status + " received for " + m_sipAccountAOR.ToString() + ".", m_owner));

                if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
                {
                    if (m_expiry > 0)
                    {
                        m_isRegistered = true;
                        m_expiry = GetUpdatedExpiry(sipResponse);
                        //if (sipResponse.Header.SwitchboardToken != null && m_lastServerNonce != null)
                        //{
                        //    SwitchboardToken = Crypto.SymmetricDecrypt(m_password, m_lastServerNonce, sipResponse.Header.SwitchboardToken);
                        //}

                        if (RegistrationSuccessful != null)
                        {
                            RegistrationSuccessful(m_sipAccountAOR);
                        }
                    }
                    else
                    {
                        m_isRegistered = false;

                        if (RegistrationRemoved != null)
                        {
                            RegistrationRemoved(m_sipAccountAOR);
                        }
                    }

                    m_waitForRegistrationMRE.Set();
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.IntervalTooBrief && m_expiry != 0)
                {
                    m_expiry = GetUpdatedExpiryForIntervalTooBrief(sipResponse);
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Registration for " + m_sipAccountAOR.ToString() + " had a too short expiry, updated to +" + m_expiry + " and trying again.", m_owner));
                    SendInitialRegister();
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.Forbidden || sipResponse.Status == SIPResponseStatusCodesEnum.NotFound || sipResponse.Status == SIPResponseStatusCodesEnum.PaymentRequired)
                {
                    // SIP account does not appear to exist.
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + m_sipAccountAOR.ToString() + ", no further registration attempts will be made.", m_owner));
                    string reasonPhrase = (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase;
                    if (RegistrationFailed != null)
                    {
                        RegistrationFailed(m_sipAccountAOR, "Registration failed with " + (int)sipResponse.Status + " " + reasonPhrase + ".");
                    }
                    m_exit = true;
                    m_waitForRegistrationMRE.Set();
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    // SIP account credentials failed.
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + m_sipAccountAOR.ToString() + ", no further registration attempts will be made.", m_owner));
                    string reasonPhrase = (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase;
                    if (RegistrationFailed != null)
                    {
                        RegistrationFailed(m_sipAccountAOR, "Registration failed with " + (int)sipResponse.Status + " " + reasonPhrase + ".");
                    }
                    m_exit = true;
                    m_waitForRegistrationMRE.Set();
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + m_sipAccountAOR.ToString() + ".", m_owner));
                    m_isRegistered = false;
                    if (RegistrationTemporaryFailure != null)
                    {
                        RegistrationTemporaryFailure(m_sipAccountAOR, "Registration failed with " + sipResponse.Status + ".");
                    }
                    m_waitForRegistrationMRE.Set();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationUserAgent AuthResponseReceived. " + excp.Message);
            }
        }

        private int GetUpdatedExpiryForIntervalTooBrief(SIPResponse sipResponse)
        {
            int newExpiry = sipResponse.Header.MinExpires;
            
            if (newExpiry != 0 && newExpiry > m_expiry)
            {
                if(newExpiry > MAX_EXPIRY)
                {
                    return MAX_EXPIRY;
                }
                else
                {
                    return newExpiry;
                }
            }
            else if(m_expiry < MAX_EXPIRY)
            {
                return m_expiry * 2;
            }

            return m_expiry;
        }

        private int GetUpdatedExpiry(SIPResponse sipResponse)
        {
            // Find the contact in the list that matches the one being maintained by this agent in order to determine the expiry value.
            int serverExpiry = m_expiry;
            int headerExpires = sipResponse.Header.Expires;
            int contactExpires = -1;
            if (sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0)
            {
                if (sipResponse.Header.Contact.Count == 1)
                {
                    contactExpires = sipResponse.Header.Contact[0].Expires;
                }
                else
                {
                    foreach (SIPContactHeader contactHeader in sipResponse.Header.Contact)
                    {
                        if (contactHeader.ContactURI == m_sipAccountAOR)
                        {
                            contactExpires = contactHeader.Expires;
                            break;
                        }
                    }
                }
            }

            if (contactExpires != -1)
            {
                serverExpiry = contactExpires;
            }
            else if (headerExpires != -1)
            {
                serverExpiry = headerExpires;
            }

            if (serverExpiry < REGISTER_MINIMUM_EXPIRY)
            {
                // Make sure we don't do a 3CX and send registration floods.
                return REGISTER_MINIMUM_EXPIRY;
            }
            else if (serverExpiry > MAX_EXPIRY)
            {
                return MAX_EXPIRY;
            }
            else
            {
                return serverExpiry;
            }
        }

        private SIPRequest GetRegistrationRequest(SIPEndPoint localSIPEndPoint)
        {
            try
            {
                string realm = (m_realm != null) ? m_realm : IPSocket.ParseHostFromSocket(m_registrarHost);

                SIPURI registerURI = m_sipAccountAOR.CopyOf();
                registerURI.User = null;

                SIPRequest registerRequest = m_sipTransport.GetRequest(
                    SIPMethodsEnum.REGISTER, 
                    registerURI,
                    new SIPToHeader(null, m_sipAccountAOR, null),
                    localSIPEndPoint);

                registerRequest.Header.From = new SIPFromHeader(null, m_sipAccountAOR, CallProperties.CreateNewTag());
                registerRequest.Header.Contact[0] = new SIPContactHeader(null, m_contactURI);
                registerRequest.Header.CSeq = ++m_cseq;
                registerRequest.Header.CallId = m_callID;
                registerRequest.Header.UserAgent = (!UserAgent.IsNullOrBlank()) ? UserAgent : m_userAgent;
                registerRequest.Header.Expires = m_expiry;

                return registerRequest;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetRegistrationRequest. " + excp.Message);
                throw excp;
            }
        }

        private SIPRequest GetAuthenticatedRegistrationRequest(SIPRequest registerRequest, SIPResponse sipResponse)
        {
            try
            {
                SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                m_lastServerNonce = authRequest.Nonce;
                string username = (m_authUsername != null) ? m_authUsername : m_sipAccountAOR.User;
                authRequest.SetCredentials(username, m_password, registerRequest.URI.ToString(), SIPMethodsEnum.REGISTER.ToString());

                SIPRequest regRequest = registerRequest.Copy();
                regRequest.LocalSIPEndPoint = registerRequest.LocalSIPEndPoint;
                regRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                regRequest.Header.From.FromTag = CallProperties.CreateNewTag();
                regRequest.Header.To.ToTag = null;
                regRequest.Header.CSeq = ++m_cseq;

                regRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
                regRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;

                //if (RequestSwitchboardToken)
                //{
                //    regRequest.Header.SwitchboardTokenRequest = m_expiry;
                //}

                return regRequest;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetAuthenticatedRegistrationRequest. " + excp.Message);
                throw excp;
            }
        }
    }
}
