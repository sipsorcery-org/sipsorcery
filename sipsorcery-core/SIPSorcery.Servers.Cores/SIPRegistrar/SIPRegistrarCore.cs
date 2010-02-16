// ============================================================================
// FileName: RegistrarCore.cs
//
// Description:
// SIP Registrar that strives to be RFC3822 compliant.
//
// Author(s):
// Aaron Clauson
//
// History:
// 21 Jan 2006	Aaron Clauson	Created.
// 22 Nov 2007  Aaron Clauson   Fixed bug where binding refresh was generating a duplicate exception if the uac endpoint changed but the contact did not.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2006-2007 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public enum RegisterResultEnum
    {
        Unknown = 0,
        Trying = 1,
        Forbidden = 2,
        Authenticated = 3,
        AuthenticationRequired = 4,
        Failed = 5,
        Error = 6,
        RequestWithNoUser = 7,
        RemoveAllRegistrations = 9,
        DuplicateRequest = 10,
        AuthenticatedFromCache = 11,
        RequestWithNoContact = 12,
        NonRegisterMethod = 13,
        DomainNotServiced = 14,
        IntervalTooBrief = 15,
    }

    /// <summary>
    /// The registrar core is the class that actually does the work of receiving registration requests and populating and
    /// maintaining the SIP registrations list.
    /// 
    /// From RFC 3261 Chapter "10.2 Constructing the REGISTER Request"
    /// - Request-URI: The Request-URI names the domain of the location service for which the registration is meant.
    /// - The To header field contains the address of record whose registration is to be created, queried, or modified.  
    ///   The To header field and the Request-URI field typically differ, as the former contains a user name. 
    /// 
    /// [ed Therefore:
    /// - The Request-URI inidcates the domain for the registration and should match the domain in the To address of record.
    /// - The To address of record contians the username of the user that is attempting to authenticate the request.]
    /// 
    /// Method of operation:
    ///  - New SIP messages received by the SIP Transport layer and queued before being sent to RegistrarCode for processing. For requests
    ///    or response that match an existing REGISTER transaction the SIP Transport layer will handle the retransmit or drop the request if
    ///    it's already being processed.
    ///  - Any non-REGISTER requests received by the RegistrarCore are responded to with not supported,
    ///  - If a persistence is being used to store registered contacts there will generally be a number of threads running for the
    ///    persistence class. Of those threads there will be one that runs calling the SIPRegistrations.IdentifyDirtyContacts. This call identifies
    ///    expired contacts and initiates the sending of any keep alive and OPTIONs requests.
    /// </summary>
    public class RegistrarCore
    {
        private const int MAX_REGISTER_QUEUE_SIZE = 1000;
        private const int MAX_PROCESS_REGISTER_SLEEP = 10000;
        private const string REGISTRAR_THREAD_NAME_PREFIX = "sipregistrar-core";

        private static ILog logger = AppState.GetLogger("sipregistrar");

        private int m_minimumBindingExpiry = SIPRegistrarBindingsManager.MINIMUM_EXPIRY_SECONDS;

        private SIPTransport m_sipTransport;
        private SIPRegistrarBindingsManager m_registrarBindingsManager;
        private SIPAssetGetDelegate<SIPAccount> GetSIPAccount_External;
        private GetCanonicalDomainDelegate GetCanonicalDomain_External;
        private SIPAuthenticateRequestDelegate SIPRequestAuthenticator_External;

        private string m_serverAgent = SIPConstants.SIP_SERVER_STRING;
        private bool m_mangleUACContact = false;            // Whether or not to adjust contact URIs that contain private hosts to the value of the bottom via received socket.
        private bool m_strictRealmHandling = false;         // If true the registrar will only accept registration requests for domains it is configured for, otherwise any realm is accepted.
        private event SIPMonitorLogDelegate m_registrarLogEvent;
        private SIPUserAgentConfigurationManager m_userAgentConfigs;
        private Queue<SIPNonInviteTransaction> m_registerQueue = new Queue<SIPNonInviteTransaction>();
        private AutoResetEvent m_registerARE = new AutoResetEvent(false);

        public bool Stop;

        public RegistrarCore(
            SIPTransport sipTransport,
            SIPRegistrarBindingsManager registrarBindingsManager,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            GetCanonicalDomainDelegate getCanonicalDomain,
            bool mangleUACContact,
            bool strictRealmHandling,
            SIPMonitorLogDelegate proxyLogDelegate,
            SIPUserAgentConfigurationManager userAgentConfigs,
            SIPAuthenticateRequestDelegate sipRequestAuthenticator)
        {
            m_sipTransport = sipTransport;
            m_registrarBindingsManager = registrarBindingsManager;
            GetSIPAccount_External = getSIPAccount;
            GetCanonicalDomain_External = getCanonicalDomain;
            m_mangleUACContact = mangleUACContact;
            m_strictRealmHandling = strictRealmHandling;
            m_registrarLogEvent = proxyLogDelegate;
            m_userAgentConfigs = userAgentConfigs;
            SIPRequestAuthenticator_External = sipRequestAuthenticator;

            ThreadPool.QueueUserWorkItem(delegate { ProcessRegisterRequest(REGISTRAR_THREAD_NAME_PREFIX + "1"); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessRegisterRequest(REGISTRAR_THREAD_NAME_PREFIX + "2"); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessRegisterRequest(REGISTRAR_THREAD_NAME_PREFIX + "3"); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessRegisterRequest(REGISTRAR_THREAD_NAME_PREFIX + "4"); });
            ThreadPool.QueueUserWorkItem(delegate { ProcessRegisterRequest(REGISTRAR_THREAD_NAME_PREFIX + "5"); });
        }

        public void AddRegisterRequest(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest registerRequest)
        {
            try
            {
                if (registerRequest.Method != SIPMethodsEnum.REGISTER)
                {
                    SIPResponse notSupportedResponse = GetErrorResponse(registerRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, "Registration requests only");
                    m_sipTransport.SendResponse(notSupportedResponse);
                }
                else
                {
                    int requestedExpiry = GetRequestedExpiry(registerRequest);

                    if (registerRequest.Header.To == null)
                    {
                        logger.Debug("Bad register request, no To header from " + remoteEndPoint + ".");
                        SIPResponse badReqResponse = SIPTransport.GetResponse(registerRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing To header");
                        m_sipTransport.SendResponse(badReqResponse);
                    }
                    else if(registerRequest.Header.To.ToURI.User.IsNullOrBlank())
                    {
                        logger.Debug("Bad register request, no To user from " + remoteEndPoint + ".");
                        SIPResponse badReqResponse = SIPTransport.GetResponse(registerRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing username on To header");
                        m_sipTransport.SendResponse(badReqResponse);
                    }
                    else if (registerRequest.Header.Contact == null || registerRequest.Header.Contact.Count == 0)
                    {
                        logger.Debug("Bad register request, no Contact header from " + remoteEndPoint + ".");
                        SIPResponse badReqResponse = SIPTransport.GetResponse(registerRequest, SIPResponseStatusCodesEnum.BadRequest, "Missing Contact header");
                        m_sipTransport.SendResponse(badReqResponse);
                    }
                    else if (requestedExpiry > 0 && requestedExpiry < m_minimumBindingExpiry)
                    {
                        logger.Debug("Bad register request, no expiry of " + requestedExpiry + " to small from " + remoteEndPoint + ".");
                        SIPResponse tooFrequentResponse = GetErrorResponse(registerRequest, SIPResponseStatusCodesEnum.IntervalTooBrief, null);
                        tooFrequentResponse.Header.MinExpires = m_minimumBindingExpiry;
                        m_sipTransport.SendResponse(tooFrequentResponse);
                    }
                    else
                    {
                        if (m_registerQueue.Count < MAX_REGISTER_QUEUE_SIZE)
                        {
                            SIPNonInviteTransaction registrarTransaction = m_sipTransport.CreateNonInviteTransaction(registerRequest, remoteEndPoint, localSIPEndPoint, null);
                            lock (m_registerQueue)
                            {
                                m_registerQueue.Enqueue(registrarTransaction);
                            }
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingInProgress, "Register queued for " + registerRequest.Header.To.ToURI.ToString() + ".", null));
                        }
                        else
                        {
                            logger.Error("Register queue exceeded max queue size " + MAX_REGISTER_QUEUE_SIZE + ", overloaded response sent.");
                            SIPResponse overloadedResponse = SIPTransport.GetResponse(registerRequest, SIPResponseStatusCodesEnum.TemporarilyNotAvailable, "Registrar overloaded, please try again shortly");
                            m_sipTransport.SendResponse(overloadedResponse);
                        }

                        m_registerARE.Set();
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AddRegisterRequest (" + remoteEndPoint.ToString() + "). " + excp.Message);
            }
        }

        private void ProcessRegisterRequest(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                while (!Stop)
                {
                    if (m_registerQueue.Count > 0)
                    {
                        try
                        {
                            SIPNonInviteTransaction registrarTransaction = null;
                            lock (m_registerQueue)
                            {
                                registrarTransaction = m_registerQueue.Dequeue();
                            }

                            if (registrarTransaction != null)
                            {
                                DateTime startTime = DateTime.Now;
                                RegisterResultEnum result = Register(registrarTransaction);
                                TimeSpan duration = DateTime.Now.Subtract(startTime);
                                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegistrarTiming, "register result=" + result.ToString() + ", time=" + duration.TotalMilliseconds + "ms, user=" + registrarTransaction.TransactionRequest.Header.To.ToURI.User + ".", null));
                            }
                        }
                        catch (Exception regExcp)
                        {
                            logger.Error("Exception ProcessRegisterRequest Register Job. " + regExcp.Message);
                        }
                    }
                    else
                    {
                        m_registerARE.WaitOne(MAX_PROCESS_REGISTER_SLEEP);
                    }
                }

                logger.Warn("ProcessRegisterRequest thread " + Thread.CurrentThread.Name + " stopping.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception ProcessRegisterRequest (" + Thread.CurrentThread.Name + "). " + excp.Message);
            }
        }

        private int GetRequestedExpiry(SIPRequest registerRequest)
        {
            int contactHeaderExpiry = (registerRequest.Header.Contact != null && registerRequest.Header.Contact.Count > 0) ? registerRequest.Header.Contact[0].Expires : -1;
            return (contactHeaderExpiry == -1) ? registerRequest.Header.Expires : contactHeaderExpiry;
        }

        private RegisterResultEnum Register(SIPTransaction registerTransaction)
        {
            try
            {
                SIPRequest sipRequest = registerTransaction.TransactionRequest;
                SIPURI registerURI = sipRequest.URI;
                SIPToHeader toHeader = sipRequest.Header.To;
                string toUser = toHeader.ToURI.User;
                string canonicalDomain = (m_strictRealmHandling) ? GetCanonicalDomain_External(toHeader.ToURI.Host, true) : toHeader.ToURI.Host;
                int requestedExpiry = GetRequestedExpiry(sipRequest);

                if (canonicalDomain == null)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Warn, "Request for " + toHeader.ToURI.Host + " rejected as no matching domain found.", null));
                    SIPResponse noDomainResponse = GetErrorResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, "Domain not serviced");
                    registerTransaction.SendFinalResponse(noDomainResponse);
                    return RegisterResultEnum.DomainNotServiced;
                }

                SIPAccount sipAccount = GetSIPAccount_External(s => s.SIPUsername == toUser && s.SIPDomain == canonicalDomain);
                SIPRequestAuthenticationResult authenticationResult = SIPRequestAuthenticator_External(registerTransaction.LocalSIPEndPoint, registerTransaction.RemoteEndPoint, sipRequest, sipAccount, FireProxyLogEvent);

                if (!authenticationResult.Authenticated)
                {
                    // 401 Response with a fresh nonce needs to be sent.
                    SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                    authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                    registerTransaction.SendFinalResponse(authReqdResponse);

                    if (authenticationResult.ErrorResponse == SIPResponseStatusCodesEnum.Forbidden)
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Warn, "Forbidden " + toUser + "@" + canonicalDomain + " does not exist, " + sipRequest.Header.Vias.BottomViaHeader.ReceivedFromAddress + ", " + sipRequest.Header.UserAgent + ".", null));
                        return RegisterResultEnum.Forbidden;
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Registrar, "Authentication required for " + toUser + "@" + canonicalDomain + " from " + registerTransaction.RemoteEndPoint + ".", toUser));
                        return RegisterResultEnum.AuthenticationRequired;
                    }
                }
                else
                {
                    // Authenticated.
                    if (sipRequest.Header.Contact == null || sipRequest.Header.Contact.Count == 0)
                    {
                        // No contacts header to update bindings with, return a list of the current bindings.
                        List<SIPRegistrarBinding> bindings = m_registrarBindingsManager.GetBindings(sipAccount.Id);
                        //List<SIPContactHeader> contactsList = m_registrarBindingsManager.GetContactHeader(); // registration.GetContactHeader(true, null);
                        if (bindings != null)
                        {
                            sipRequest.Header.Contact = GetContactHeader(bindings);
                        }

                        SIPResponse okResponse = GetOkResponse(sipRequest);
                        registerTransaction.SendFinalResponse(okResponse);
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegisterSuccess, "Empty registration request successful for " + toUser + "@" + canonicalDomain + " from " + registerTransaction.RemoteEndPoint + ".", toUser));
                    }
                    else
                    {
                        SIPEndPoint uacRemoteEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : registerTransaction.RemoteEndPoint;
                        SIPEndPoint proxySIPEndPoint = (!sipRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedOn) : null;
                        SIPEndPoint registrarEndPoint = registerTransaction.LocalSIPEndPoint;

                        SIPResponseStatusCodesEnum updateResult = SIPResponseStatusCodesEnum.Ok;
                        string updateMessage = null;

                        DateTime startTime = DateTime.Now;

                        List<SIPRegistrarBinding> bindingsList = m_registrarBindingsManager.UpdateBinding(
                            sipAccount,
                            proxySIPEndPoint,
                            uacRemoteEndPoint,
                            registrarEndPoint,
                            sipRequest.Header.Contact[0].ContactURI.CopyOf(),
                            sipRequest.Header.CallId,
                            sipRequest.Header.CSeq,
                            sipRequest.Header.Contact[0].Expires,
                            sipRequest.Header.Expires,
                            sipRequest.Header.UserAgent,
                            out updateResult,
                            out updateMessage);

                        int bindingExpiry = GetBindingExpiry(bindingsList, sipRequest.Header.Contact[0].ContactURI.ToString());
                        TimeSpan duration = DateTime.Now.Subtract(startTime);
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegistrarTiming, "Binding update time for " + toUser + "@" + canonicalDomain + " took " + duration.TotalMilliseconds + "ms.", null));

                        if (updateResult == SIPResponseStatusCodesEnum.Ok)
                        {
                            string proxySocketStr = (proxySIPEndPoint != null) ? " (proxy=" + proxySIPEndPoint.ToString() + ")" : null;
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegisterSuccess, "Registration successful for " + toUser + "@" + canonicalDomain + " from " + uacRemoteEndPoint + proxySocketStr + ", expiry " + bindingExpiry + "s.", toUser));
                            FireProxyLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate, toUser, uacRemoteEndPoint, sipAccount.Id.ToString()));

                            // The standard states that the Ok response should contain the list of current bindings but that breaks some UAs. As a 
                            // compromise the list is returned with the Contact that UAC sent as the first one in the list.
                            bool contactListSupported = m_userAgentConfigs.GetUserAgentContactListSupport(sipRequest.Header.UserAgent);
                            if (contactListSupported)
                            {
                                sipRequest.Header.Contact = GetContactHeader(bindingsList);
                            }
                            else
                            {
                                // Some user agents can't match the contact header if the expiry is added to it.
                                sipRequest.Header.Contact[0].Expires = bindingExpiry;
                            }

                            SIPResponse okResponse = GetOkResponse(sipRequest);
                            registerTransaction.SendFinalResponse(okResponse);
                        }
                        else
                        {
                            // The binding update failed even though the REGISTER request was authorised. This is probably due to a 
                            // temporary problem connecting to the bindings data store. SendOk but set the binding expiry to the minimum so
                            // that the UA will try again as soon as possible.
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, "Registration request successful but binding update failed for " + toUser + "@" + canonicalDomain + " from " + registerTransaction.RemoteEndPoint + ".", toUser));
                            sipRequest.Header.Contact[0].Expires = m_minimumBindingExpiry;
                            SIPResponse okResponse = GetOkResponse(sipRequest);
                            registerTransaction.SendFinalResponse(okResponse);
                        }
                    }

                    return RegisterResultEnum.Authenticated;
                }
            }
            catch (Exception excp)
            {
                string regErrorMessage = "Exception registrarcore registering. " + excp.Message + "\r\n" + registerTransaction.TransactionRequest.ToString();
                logger.Error(regErrorMessage);
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, regErrorMessage, null));

                try
                {
                    SIPResponse errorResponse = GetErrorResponse(registerTransaction.TransactionRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                    registerTransaction.SendFinalResponse(errorResponse);
                }
                catch { }

                return RegisterResultEnum.Error;
            }
        }

        private int GetBindingExpiry(List<SIPRegistrarBinding> bindings, string bindingURI)
        {
            if (bindings == null || bindings.Count == 0)
            {
                return -1;
            }
            else
            {
                foreach (SIPRegistrarBinding binding in bindings)
                {
                    if (binding.ContactURI == bindingURI)
                    {
                        return binding.Expiry;
                    }
                }
                return -1;
            }
        }

        /// <summary>
        /// Gets a SIP contact header for this address-of-record based on the bindings list.
        /// </summary>
        /// <returns></returns>
        private List<SIPContactHeader> GetContactHeader(List<SIPRegistrarBinding> bindings)
        {
            if (bindings != null && bindings.Count > 0)
            {
                List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();

                foreach (SIPRegistrarBinding binding in bindings)
                {
                    SIPContactHeader bindingContact = new SIPContactHeader(null, binding.ContactSIPURI);
                    bindingContact.Expires = Convert.ToInt32(binding.ExpiryTime.Subtract(DateTime.UtcNow).TotalSeconds % Int32.MaxValue);
                    contactHeaderList.Add(bindingContact);
                }

                return contactHeaderList;
            }
            else
            {
                return null;
            }
        }

        private SIPResponse GetOkResponse(SIPRequest sipRequest)
        {
            try
            {
                SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                SIPHeader requestHeader = sipRequest.Header;
                okResponse.Header = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);

                // RFC3261 has a To Tag on the example in section "24.1 Registration".
                if (okResponse.Header.To.ToTag == null || okResponse.Header.To.ToTag.Trim().Length == 0)
                {
                    okResponse.Header.To.ToTag = CallProperties.CreateNewTag();
                }

                okResponse.Header.CSeqMethod = requestHeader.CSeqMethod;
                okResponse.Header.Vias = requestHeader.Vias;
                okResponse.Header.Server = m_serverAgent;
                okResponse.Header.MaxForwards = Int32.MinValue;
                okResponse.Header.SetDateHeader();

                return okResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetOkResponse. " + excp.Message);
                throw excp;
            }
        }

        private SIPResponse GetAuthReqdResponse(SIPRequest sipRequest, string nonce, string realm)
        {
            try
            {
                SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Unauthorised, null);
                SIPAuthenticationHeader authHeader = new SIPAuthenticationHeader(SIPAuthorisationHeadersEnum.WWWAuthenticate, realm, nonce);
                SIPHeader requestHeader = sipRequest.Header;
                SIPHeader unauthHeader = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);

                if (unauthHeader.To.ToTag == null || unauthHeader.To.ToTag.Trim().Length == 0)
                {
                    unauthHeader.To.ToTag = CallProperties.CreateNewTag();
                }

                unauthHeader.CSeqMethod = requestHeader.CSeqMethod;
                unauthHeader.Vias = requestHeader.Vias;
                unauthHeader.AuthenticationHeader = authHeader;
                unauthHeader.Server = m_serverAgent;
                unauthHeader.MaxForwards = Int32.MinValue;

                authReqdResponse.Header = unauthHeader;

                return authReqdResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetAuthReqdResponse. " + excp.Message);
                throw excp;
            }
        }

        private SIPResponse GetErrorResponse(SIPRequest sipRequest, SIPResponseStatusCodesEnum errorResponseCode, string errorMessage)
        {
            try
            {
                SIPResponse errorResponse = SIPTransport.GetResponse(sipRequest, errorResponseCode, null);
                if (errorMessage != null)
                {
                    errorResponse.ReasonPhrase = errorMessage;
                }

                SIPHeader requestHeader = sipRequest.Header;
                SIPHeader errorHeader = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);

                if (errorHeader.To.ToTag == null || errorHeader.To.ToTag.Trim().Length == 0)
                {
                    errorHeader.To.ToTag = CallProperties.CreateNewTag();
                }

                errorHeader.CSeqMethod = requestHeader.CSeqMethod;
                errorHeader.Vias = requestHeader.Vias;
                errorHeader.Server = m_serverAgent;
                errorHeader.MaxForwards = Int32.MinValue;

                errorResponse.Header = errorHeader;

                return errorResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetErrorResponse. " + excp.Message);
                throw excp;
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            if (m_registrarLogEvent != null)
            {
                try
                {
                    m_registrarLogEvent(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireProxyLogEvent RegistrarCore. " + excp.Message);
                }
            }
        }
    }
}
