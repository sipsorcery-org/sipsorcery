﻿// ============================================================================
// FileName: SIPRegistrationUserAgent.cs
//
// Description:
// A user agent that can register and maintain a binding with a SIP Registrar.
//
// Author(s):
// Aaron Clauson (aaron@sipsorcery.com)
//
// History:
// 03 Mar 2010	Aaron Clauson	Created, Hobart, Australia.
// rj2: some PBX/Trunks need UserDisplayName in SIP-REGISTER
//
// License:
// BSD 3-Clause "New" or "Revised" License, see included LICENSE.md file.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using SIPSorcery.Sys;

namespace SIPSorcery.SIP.App
{
    public class SIPRegistrationUserAgent
    {
        private const int MAX_EXPIRY = 7200;
        private const int MAX_REGISTRATION_ATTEMPT_TIMEOUT = 60;
        private const int REGISTRATION_HEAD_TIME = 5;                // Time in seconds to go to next registration to initate.
        private const int REGISTER_FAILURERETRY_INTERVAL = 300;      // Number of seconds between consecutive register requests in the event of failures or timeouts.
        //rj2: there are PBX which send new Expires header in SIP OK with value lesser than 60 -> set hardcoded minimum to 10, so registration on PBX does not timeout
        private const int REGISTER_MINIMUM_EXPIRY = 10;              // The minimum interval a registration will be accepted for. Anything less than this interval will use this minimum value.
        private const int DEFAULT_REGISTER_EXPIRY = 600;
        private const int MAX_REGISTER_ATTEMPTS = 3;                 // The maximum number of registration attempts that will be made without a failure condition before incurring a temporary failure.

        private static ILogger logger = Log.Logger;

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

        private bool m_isRegistered;
        private int m_cseq;
        private string m_callID;
        private bool m_exit;
        private int m_attempts;
        private ManualResetEvent m_waitForRegistrationMRE = new ManualResetEvent(false);
        private Timer m_registrationTimer;

        public string UserAgent;                // If not null this value will replace the default user agent value in the REGISTER request.
        public string UserDisplayName;			//rj2: if not null, used in fromheader and contactheader

        public event Action<SIPURI, string> RegistrationFailed;
        public event Action<SIPURI, string> RegistrationTemporaryFailure;
        public event Action<SIPURI> RegistrationSuccessful;
        public event Action<SIPURI> RegistrationRemoved;

        /// <summary>
        /// Creates a new SIP registation agent that will attempt to register with a SIP Registrar server.
        /// If the registration fails the agent will retry up to a hard coded maximum number of 3 attempts.
        /// If successful the agent will periodically refresh the registration based on the Expiry time 
        /// returned by the server.
        /// </summary>
        /// <param name="sipTransport">The SIP transport layer to use to send the register request.</param>
        /// <param name="username">The username to use if the server requests authorisation.</param>
        /// <param name="password">The password to use if the server requests authorisation.</param>
        /// <param name="server">The hostname or socket address for the registrat server. Can be in a format of
        /// hostname:port or ipaddress:port, e.g. sipsorcery.com or 67.222.131.147:5060.</param>
        /// <param name="expiry">The expiry value to request for the contact. This value can be rejected or overridden
        /// by the server.</param>
        public SIPRegistrationUserAgent(
            SIPTransport sipTransport,
            string username,
            string password,
            string server,
            int expiry)
        {
            m_sipTransport = sipTransport;
            m_sipAccountAOR = new SIPURI(username, server, null, SIPSchemesEnum.sip, SIPProtocolsEnum.udp);
            m_authUsername = username;
            m_password = password;
            m_registrarHost = server;
            m_expiry = (expiry >= REGISTER_MINIMUM_EXPIRY && expiry <= MAX_EXPIRY) ? expiry : DEFAULT_REGISTER_EXPIRY;
            m_callID = Guid.NewGuid().ToString();

            // Setting the contact to "0.0.0.0" tells the transport layer to populate it at send time.
            m_contactURI = new SIPURI(m_sipAccountAOR.Scheme, IPAddress.Any, 0);

            Log_External = (ev) => logger.LogDebug(ev?.Message);
        }

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
            m_callID = Guid.NewGuid().ToString();

            Log_External = logDelegate;
        }

        public void Start()
        {
            if (m_registrationTimer != null)
            {
                throw new ApplicationException("SIPRegistrationUserAgent is already running, try Stop() at first!");
            }

            int callbackPeriod = (m_expiry - REGISTRATION_HEAD_TIME) * 1000;
            logger.LogDebug($"Starting SIPRegistrationUserAgent for {m_sipAccountAOR.ToString()}, callback period {callbackPeriod}ms.");

            m_registrationTimer = new Timer(DoRegistration, null, 0, callbackPeriod);
        }

        private void DoRegistration(object state)
        {
            if (Monitor.TryEnter(m_waitForRegistrationMRE))
            {
                try
                {
                    logger.LogDebug("DoRegistration for " + m_sipAccountAOR.ToString() + ".");

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
                        logger.LogDebug("SIPRegistrationUserAgent was successful, scheduling next registration to " + m_sipAccountAOR.ToString() + " in " + (m_expiry - REGISTRATION_HEAD_TIME) + "s.");
                        m_registrationTimer.Change((m_expiry - REGISTRATION_HEAD_TIME) * 1000, Timeout.Infinite);
                    }
                    else
                    {
                        logger.LogDebug("SIPRegistrationUserAgent temporarily failed, scheduling next registration to " + m_sipAccountAOR.ToString() + " in " + REGISTER_FAILURERETRY_INTERVAL + "s.");
                        m_registrationTimer.Change((m_expiry - REGISTRATION_HEAD_TIME) * 1000, Timeout.Infinite);
                    }
                }
                catch (Exception excp)
                {
                    logger.LogError("Exception DoRegistration Start. " + excp.Message);
                }
                finally
                {
                    Monitor.Exit(m_waitForRegistrationMRE);
                }
            }
        }

        /// <summary>
        /// Allows the registration expiry setting to be adjusted after the instance has been created.
        /// </summary>
        /// <param name="expiry">The new expiry value.</param>
        public void SetExpiry(int expiry)
        {
            int newExpiry = (expiry >= REGISTER_MINIMUM_EXPIRY && expiry <= MAX_EXPIRY) ? expiry : DEFAULT_REGISTER_EXPIRY;

            if (newExpiry != m_expiry)
            {
                logger.LogInformation($"Expiry for registration agent for {m_sipAccountAOR.ToString()} updated from {m_expiry} to {newExpiry}.");

                m_expiry = newExpiry;

                // Schedule an immediate registration for the new value.
                m_registrationTimer.Change(0, Timeout.Infinite);
            }
        }

        public void Stop()
        {
            try
            {
                if (!m_exit)
                {
                    logger.LogDebug("Stopping SIP registration user agent for " + m_sipAccountAOR.ToString() + ".");

                    m_exit = true;
                    m_waitForRegistrationMRE.Set();

                    if (m_isRegistered)
                    {
                        m_attempts = 0;
                        m_expiry = 0;
                        ThreadPool.QueueUserWorkItem(delegate
                        { SendInitialRegister(); });
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPRegistrationUserAgent Stop. " + excp.Message);
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
                    RegistrationTemporaryFailure?.Invoke(m_sipAccountAOR, "Registration reached the maximum number of allowed attempts.");
                    m_waitForRegistrationMRE.Set();
                }
                else
                {
                    m_attempts++;

                    SIPEndPoint registrarSIPEndPoint = m_outboundProxy;
                    if (registrarSIPEndPoint == null)
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

                    if (registrarSIPEndPoint == null)
                    {
                        logger.LogWarning("SIPRegistrationAgent could not resolve " + m_registrarHost + ".");

                        RegistrationFailed?.Invoke(m_sipAccountAOR, "Could not resolve " + m_registrarHost + ".");
                    }
                    else
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Initiating registration to " + m_registrarHost + " at " + registrarSIPEndPoint.ToString() + " for " + m_sipAccountAOR.ToString() + ".", m_owner));
                        SIPRequest regRequest = GetRegistrationRequest();

                        SIPNonInviteTransaction regTransaction = new SIPNonInviteTransaction(m_sipTransport, regRequest, m_outboundProxy);
                        // These handlers need to be on their own threads to take the processing off the SIP transport layer.
                        regTransaction.NonInviteTransactionFinalResponseReceived += (lep, rep, tn, rsp) => { ThreadPool.QueueUserWorkItem(delegate { ServerResponseReceived(lep, rep, tn, rsp); }); };
                        regTransaction.NonInviteTransactionTimedOut += (tn) => { ThreadPool.QueueUserWorkItem(delegate { RegistrationTimedOut(tn); }); };

                        m_sipTransport.SendTransaction(regTransaction);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SendInitialRegister to " + m_registrarHost + ". " + excp.Message);
                RegistrationFailed?.Invoke(m_sipAccountAOR, "Exception SendInitialRegister to " + m_registrarHost + ". " + excp.Message);
            }
        }

        private void RegistrationTimedOut(SIPTransaction sipTransaction)
        {
            m_isRegistered = false;
            RegistrationTemporaryFailure?.Invoke(m_sipAccountAOR, "Registration transaction to " + m_registrarHost + " for " + m_sipAccountAOR.ToString() + " timed out.");
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
                            RegistrationTemporaryFailure?.Invoke(m_sipAccountAOR, "Registration reached the maximum number of allowed attempts.");
                            m_waitForRegistrationMRE.Set();
                        }
                        else
                        {
                            m_attempts++;
                            SIPRequest authenticatedRequest = GetAuthenticatedRegistrationRequest(sipTransaction.TransactionRequest, sipResponse);
                            SIPNonInviteTransaction regAuthTransaction = new SIPNonInviteTransaction(m_sipTransport, authenticatedRequest, m_outboundProxy);
                            regAuthTransaction.NonInviteTransactionFinalResponseReceived += (lep, rep, tn, rsp) => { ThreadPool.QueueUserWorkItem(delegate { AuthResponseReceived(lep, rep, tn, rsp); }); };
                            regAuthTransaction.NonInviteTransactionTimedOut += (tn) => { ThreadPool.QueueUserWorkItem(delegate { RegistrationTimedOut(tn); }); };
                            m_sipTransport.SendTransaction(regAuthTransaction);
                        }
                    }
                    else
                    {
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " but no authentication header was supplied for " + m_sipAccountAOR.ToString() + ".", m_owner));
                        m_isRegistered = false;
                        RegistrationTemporaryFailure?.Invoke(m_sipAccountAOR, "Registration failed with " + sipResponse.Status + " but no authentication header was supplied.");
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
                            RegistrationSuccessful?.Invoke(m_sipAccountAOR);
                        }
                        else
                        {
                            m_isRegistered = false;
                            RegistrationRemoved?.Invoke(m_sipAccountAOR);
                        }

                        m_waitForRegistrationMRE.Set();
                    }
                    else if (sipResponse.Status == SIPResponseStatusCodesEnum.Forbidden || sipResponse.Status == SIPResponseStatusCodesEnum.NotFound)
                    {
                        // SIP account does not appear to exist.
                        Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + m_sipAccountAOR.ToString() + ", no further registration attempts will be made.", m_owner));
                        string reasonPhrase = (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase;
                        RegistrationFailed?.Invoke(m_sipAccountAOR, "Registration failed with " + (int)sipResponse.Status + " " + reasonPhrase + ".");
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
                        RegistrationTemporaryFailure?.Invoke(m_sipAccountAOR, "Registration failed with " + sipResponse.Status + ".");
                        m_waitForRegistrationMRE.Set();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPRegistrationUserAgent ServerResponseReceived (" + remoteEndPoint + "). " + excp.Message);
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
                        RegistrationSuccessful?.Invoke(m_sipAccountAOR);
                    }
                    else
                    {
                        m_isRegistered = false;
                        RegistrationRemoved?.Invoke(m_sipAccountAOR);
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
                    RegistrationFailed?.Invoke(m_sipAccountAOR, "Registration failed with " + (int)sipResponse.Status + " " + reasonPhrase + ".");
                    m_exit = true;
                    m_waitForRegistrationMRE.Set();
                }
                else if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                {
                    // SIP account credentials failed.
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + m_sipAccountAOR.ToString() + ", no further registration attempts will be made.", m_owner));
                    string reasonPhrase = (sipResponse.ReasonPhrase.IsNullOrBlank()) ? sipResponse.Status.ToString() : sipResponse.ReasonPhrase;
                    RegistrationFailed?.Invoke(m_sipAccountAOR, "Registration failed with " + (int)sipResponse.Status + " " + reasonPhrase + ".");
                    m_exit = true;
                    m_waitForRegistrationMRE.Set();
                }
                else
                {
                    Log_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.UserAgentClient, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + m_sipAccountAOR.ToString() + ".", m_owner));
                    m_isRegistered = false;
                    RegistrationTemporaryFailure?.Invoke(m_sipAccountAOR, "Registration failed with " + sipResponse.Status + ".");
                    m_waitForRegistrationMRE.Set();
                }
            }
            catch (Exception excp)
            {
                logger.LogError("Exception SIPRegistrationUserAgent AuthResponseReceived. " + excp.Message);
            }
        }

        private int GetUpdatedExpiryForIntervalTooBrief(SIPResponse sipResponse)
        {
            int newExpiry = sipResponse.Header.MinExpires;

            if (newExpiry != 0 && newExpiry > m_expiry)
            {
                if (newExpiry > MAX_EXPIRY)
                {
                    return MAX_EXPIRY;
                }
                else
                {
                    return newExpiry;
                }
            }
            else if (m_expiry < MAX_EXPIRY)
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
                        if (contactHeader.ContactURI.ToParameterlessString() == m_contactURI.ToParameterlessString())
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

        private SIPRequest GetRegistrationRequest()
        {
            try
            {
                string realm = (m_realm != null) ? m_realm : IPSocket.ParseHostFromSocket(m_registrarHost);

                SIPURI registerURI = m_sipAccountAOR.CopyOf();
                registerURI.User = null;

                SIPRequest registerRequest = SIPRequest.GetRequest(
                    SIPMethodsEnum.REGISTER,
                    registerURI,
                    new SIPToHeader(this.UserDisplayName, m_sipAccountAOR, null),
                    new SIPFromHeader(this.UserDisplayName, m_sipAccountAOR, CallProperties.CreateNewTag()));

                registerRequest.Header.Contact = new List<SIPContactHeader> { new SIPContactHeader(this.UserDisplayName, m_contactURI) };
                registerRequest.Header.CSeq = ++m_cseq;
                registerRequest.Header.CallId = m_callID;
                registerRequest.Header.UserAgent = (!UserAgent.IsNullOrBlank()) ? UserAgent : m_userAgent;
                registerRequest.Header.Expires = m_expiry;

                return registerRequest;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GetRegistrationRequest. " + excp.Message);
                throw excp;
            }
        }

        private SIPRequest GetAuthenticatedRegistrationRequest(SIPRequest registerRequest, SIPResponse sipResponse)
        {
            try
            {
                SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                string username = (m_authUsername != null) ? m_authUsername : m_sipAccountAOR.User;
                authRequest.SetCredentials(username, m_password, registerRequest.URI.ToString(), SIPMethodsEnum.REGISTER.ToString());

                SIPRequest regRequest = registerRequest.Copy();
                regRequest.SetSendFromHints(registerRequest.LocalSIPEndPoint);

                regRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                regRequest.Header.From.FromTag = CallProperties.CreateNewTag();
                regRequest.Header.To.ToTag = null;
                regRequest.Header.CSeq = ++m_cseq;

                regRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
                regRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;

                return regRequest;
            }
            catch (Exception excp)
            {
                logger.LogError("Exception GetAuthenticatedRegistrationRequest. " + excp.Message);
                throw excp;
            }
        }
    }
}
