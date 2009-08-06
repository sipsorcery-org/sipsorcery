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
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
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
		//private const int CACHE_EXPIRY_TIME = 10;			        // Time in minutes a SIP account can exist in the cache and use a previous registration before a new auth will be requested, balance with agressive NAT timeouts.
															        // a random element is also used in conjunction with this to attempt to mitigate registration spikes.
        //public const string PROXY_VIA_PARAMETER_NAME = "proxy";     // A proxy forwarding REGISTER requests will add a parameter with this name and a value of the socket it received the request on.

		private static ILog logger = AppState.GetLogger("sipregistrar");

        private string m_sipExpiresParameterKey = SIPContactHeader.EXPIRES_PARAMETER_KEY;
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

        public RegistrarCore(
            SIPTransport sipTransport,
            SIPRegistrarBindingsManager registrarBindingsManager,
            SIPAssetGetDelegate<SIPAccount> getSIPAccount,
            GetCanonicalDomainDelegate getCanonicalDomain,
            bool mangleUACContact,
            bool strictRealmHandling,
            SIPMonitorLogDelegate proxyLogDelegate,
            SIPUserAgentConfigurationManager userAgentConfigs,
            SIPAuthenticateRequestDelegate sipRequestAuthenticator) {

            m_sipTransport = sipTransport;
            m_registrarBindingsManager = registrarBindingsManager;
            GetSIPAccount_External = getSIPAccount;
            GetCanonicalDomain_External = getCanonicalDomain;
            m_mangleUACContact = mangleUACContact;
            m_strictRealmHandling = strictRealmHandling;
            m_registrarLogEvent = proxyLogDelegate;
            m_userAgentConfigs = userAgentConfigs;
            SIPRequestAuthenticator_External = sipRequestAuthenticator;
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
                    SIPNonInviteTransaction registrarTransaction = m_sipTransport.CreateNonInviteTransaction(registerRequest, remoteEndPoint, localSIPEndPoint, null);

                    DateTime registerStartTime = DateTime.Now;
                    RegisterResultEnum result = Register(registrarTransaction);
                    TimeSpan registerTime = DateTime.Now.Subtract(registerStartTime);

                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegistrarTiming, "register result=" + result.ToString() + ", time=" + registerTime.TotalMilliseconds.ToString("0") + "ms, user=" + registrarTransaction.TransactionRequest.Header.To.ToURI.User + ".", null));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AddRegisterRequest (" + remoteEndPoint.ToString() + "). " + excp.Message);
            }
		}

        private RegisterResultEnum Register(SIPTransaction registerTransaction)
        {
            SIPRequest sipRequest = registerTransaction.TransactionRequest;
            SIPURI registerURI = sipRequest.URI;
            SIPToHeader toHeader = sipRequest.Header.To;
            //bool authenticated = false;
            SIPParameterlessURI addressOfRecord = null;
            string canonicalDomain = (m_strictRealmHandling) ? GetCanonicalDomain_External(toHeader.ToURI.Host, true) : toHeader.ToURI.Host;

            try
            {
                #region Validate the request.

                // Check that register request is valid.
                if (toHeader.ToURI.User == null || toHeader.ToURI.User.Trim().Length == 0)
                {
                    // Ignore empty usernames.
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Warn, "Request had empty user responding with Forbidden, to header=" + toHeader.ToString() + ".", null));
                    SIPResponse forbiddenResponse = GetErrorResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, "Forbidden, the username was empty");
                    registerTransaction.SendFinalResponse(forbiddenResponse);
                    return RegisterResultEnum.RequestWithNoUser;
                }

                if (canonicalDomain == null)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Warn, "Request for " + toHeader.ToURI.Host + " rejected as no matching domain found.", null));
                    SIPResponse noDomainResponse = GetErrorResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, "Domain not serviced");
                    registerTransaction.SendFinalResponse(noDomainResponse);
                    return RegisterResultEnum.DomainNotServiced;
                }

                #endregion

                addressOfRecord = new SIPParameterlessURI(SIPSchemesEnum.sip, canonicalDomain, toHeader.ToURI.User);

                //FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorServerTypesEnum.Registrar, "Registration for " + username + ", contact header received " + origContact + " using " + contactStr + ".", null, null, null, null));

                // Check cache for recent registrations.
                //SIPRegistrarRecord registration = m_registrationsStore.Lookup(addressOfRecord);
                SIPAccount sipAccount = GetSIPAccount_External(s => s.SIPUsername == addressOfRecord.User && s.SIPDomain == addressOfRecord.Host);
                /*if (registration != null && registration.LastAuthenticationTime != DateTime.MinValue &&
                    DateTime.Now.Subtract(registration.LastAuthenticationTime).TotalMinutes < CACHE_EXPIRY_TIME)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorServerTypesEnum.RegistrarCache, "authenticating user=" + addressOfRecord.User + " from cache.", null, null, null, null));
                    authenticated = true;
                }
                else
                {
                // Authenticate from request headers.
                if (sipAccount == null)
                    {
                        // SIP account does not exist so send 403.
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Warn, "Forbidden " + addressOfRecord.ToString() + " does not exist, " + sipRequest.Header.Vias.BottomViaHeader.ReceivedFromAddress + ", " + sipRequest.Header.UserAgent + ".", null));
                        SIPResponse forbiddenResponse = GetErrorResponse(sipRequest, SIPResponseStatusCodesEnum.Forbidden, null);
                        registerTransaction.SendFinalResponse(forbiddenResponse);
                        return RegisterResultEnum.Forbidden;
                    }*/
                //else if (sipRequest.Header.AuthenticationHeader != null)
                    //{

                SIPRequestAuthenticationResult authenticationResult = SIPRequestAuthenticator_External(registerTransaction.LocalSIPEndPoint, registerTransaction.RemoteEndPoint, sipRequest, sipAccount, FireProxyLogEvent);

                        /*SIPAuthenticationHeader reqAuthHeader = sipRequest.Header.AuthenticationHeader;
                        string requestNonce = (reqAuthHeader != null) ? reqAuthHeader.SIPDigest.Nonce : null;

                        #region Checking digest.

                        if (sipAccount.SIPPassword.IsNullOrBlank())
                        {
                            // Allow blank password.
                            authenticated = true;
                        }
                        else
                        {
                            //string realm = reqAuthHeader.AuthRequest.Realm;
                            string uri = reqAuthHeader.SIPDigest.URI;
                            string response = reqAuthHeader.SIPDigest.Response;
                            string authUsername = addressOfRecord.User;
                            string secret = sipAccount.SIPPassword;

                            SIPAuthorisationDigest checkAuthReq = sipRequest.Header.AuthenticationHeader.SIPDigest;
                            checkAuthReq.SetCredentials(authUsername, secret, uri, SIPMethodsEnum.REGISTER.ToString());
                            string digest = checkAuthReq.Digest;

                            authenticated = (digest == response);

                            if (!authenticated)
                            {
                                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegisterFail, "Registration authentication failed for " + addressOfRecord.ToString() + " from " + registerTransaction.RemoteEndPoint + ".", null));
                            }
                        }

                        #endregion
                         
                    }*/

                if (!authenticationResult.Authenticated)
                    {
                        // 401 Response with a fresh nonce needs to be sent.
                        //SIPResponse authReqdResponse = GetAuthReqdResponse(sipRequest, Crypto.GetRandomInt().ToString(), canonicalDomain);
                        //registerTransaction.SendFinalResponse(authReqdResponse);
                    SIPResponse authReqdResponse = SIPTransport.GetResponse(sipRequest, authenticationResult.ErrorResponse, null);
                    authReqdResponse.Header.AuthenticationHeader = authenticationResult.AuthenticationRequiredHeader;
                        registerTransaction.SendFinalResponse(authReqdResponse);

                        if (authenticationResult.ErrorResponse == SIPResponseStatusCodesEnum.Forbidden) {
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Warn, "Forbidden " + addressOfRecord.ToString() + " does not exist, " + sipRequest.Header.Vias.BottomViaHeader.ReceivedFromAddress + ", " + sipRequest.Header.UserAgent + ".", null));
                            return RegisterResultEnum.Forbidden;
                        }
                        else {
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Registrar, "Authentication required for " + addressOfRecord.ToString() + " from " + registerTransaction.RemoteEndPoint + ".", addressOfRecord.User));
                            return RegisterResultEnum.AuthenticationRequired;
                        }
                    }
                    else // Authenticated.
                    {
                        //registration.ProxyEndPoint = remoteEndPoint;
                        //registration.LastAuthenticationTime = DateTime.Now;

                        if (sipRequest.Header.Contact == null || sipRequest.Header.Contact.Count == 0)
                        {
                            // No contacts header to update bindings with, return a list of the current bindings.
                            List<SIPContactHeader> contactsList = m_registrarBindingsManager.GetContactHeader(new Guid(sipAccount.Id)); // registration.GetContactHeader(true, null);
                            if (contactsList != null)
                            {
                                sipRequest.Header.Contact = contactsList;
                            }

                            SIPResponse okResponse = GetOkResponse(sipRequest);
                            registerTransaction.SendFinalResponse(okResponse);
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegisterSuccess, "Empty registration request successful for " + addressOfRecord.ToString() + " from " + registerTransaction.RemoteEndPoint + ".", addressOfRecord.User));
                        }
                        else
                        {
                            //logger.Debug("Recording registration record for " + addressOfRecord.ToString() + ".");

                            SIPEndPoint uacRemoteEndPoint = (!sipRequest.Header.ProxyReceivedFrom.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedFrom) : registerTransaction.RemoteEndPoint;
                            SIPEndPoint proxySIPEndPoint = (!sipRequest.Header.ProxyReceivedOn.IsNullOrBlank()) ? SIPEndPoint.ParseSIPEndPoint(sipRequest.Header.ProxyReceivedOn) : null;
                            SIPEndPoint registrarEndPoint = registerTransaction.LocalSIPEndPoint;

                            int contactHeaderExpiry = -1;
                            if (sipRequest.Header.Contact[0].ContactParameters.Has(m_sipExpiresParameterKey)) {
                                Int32.TryParse(sipRequest.Header.Contact[0].ContactParameters.Get(m_sipExpiresParameterKey), out contactHeaderExpiry);
                            }
                            SIPResponseStatusCodesEnum updateResult = SIPResponseStatusCodesEnum.Ok;
                            string updateMessage = null;

                            int bindingExpiry = m_registrarBindingsManager.UpdateBinding(
                                sipAccount,
                                proxySIPEndPoint,
                                uacRemoteEndPoint,
                                registrarEndPoint,
                                sipRequest.Header.Contact[0].ContactURI,
                                sipRequest.Header.CallId,
                                sipRequest.Header.CSeq,
                                contactHeaderExpiry,
                                sipRequest.Header.Expires,
                                sipRequest.Header.UserAgent,
                                out updateResult,
                                out updateMessage);

                            if (updateResult == SIPResponseStatusCodesEnum.Ok)
                            {
                                string proxySocketStr = (proxySIPEndPoint != null) ? " (proxy=" + proxySIPEndPoint.ToString() + ")" : null;
                                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegisterSuccess, "Registration successful for " + addressOfRecord.ToString() + " from " + uacRemoteEndPoint + proxySocketStr + ", expiry " + bindingExpiry + "s.", addressOfRecord.User));
                                FireProxyLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate, addressOfRecord.User, uacRemoteEndPoint, sipAccount.Id));

                                // The standard states that the Ok response should contain the list of current bindings but that breaks a lot of UAs. As a 
                                // compromise the list is returned with the Contact that UAC sent as the first one in the list.
                                bool contactListSupported = m_userAgentConfigs.GetUserAgentContactListSupport(sipRequest.Header.UserAgent);
                                if (contactListSupported)
                                {
                                    sipRequest.Header.Contact = m_registrarBindingsManager.GetContactHeader(new Guid(sipAccount.Id));
                                }
                                else
                                {
                                    sipRequest.Header.Contact[0].Expires = bindingExpiry;
                                }

                                SIPResponse okResponse = GetOkResponse(sipRequest);
                                registerTransaction.SendFinalResponse(okResponse);
                            }
                            else
                            {
                                // The binding update failed even though the REGISTER request was authorised. This is probably due to a 
                                // temporary problem connecting to the bdingins data store. Send ok but set the binding expiry to the minimum so
                                // that the UA will try again as soon qas possible.
                                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, "Registration request successful but binding update failed for " + addressOfRecord.ToString() + " from " + registerTransaction.RemoteEndPoint + ".", addressOfRecord.User));
                                //SIPResponse errorResponse = GetErrorResponse(sipRequest, updateResult, updateMessage);
                                //registerTransaction.SendFinalResponse(errorResponse);
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
                string regErrorMessage = "Exception registrarcore registering. " + excp.Message + "\r\n" + sipRequest.ToString();
                logger.Error(regErrorMessage);
                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, regErrorMessage, null));

                try
                {
                    SIPResponse errorResponse = GetErrorResponse(sipRequest, SIPResponseStatusCodesEnum.InternalServerError, null);
                    registerTransaction.SendFinalResponse(errorResponse);
                }
                catch { }

                return RegisterResultEnum.Error;
            }
        }

        private SIPResponse GetOkResponse(SIPRequest sipRequest)
		{	
			try
			{
				SIPResponse okResponse = SIPTransport.GetResponse(sipRequest, SIPResponseStatusCodesEnum.Ok, null);
                SIPHeader requestHeader = sipRequest.Header;
                okResponse.Header = new SIPHeader(requestHeader.Contact, requestHeader.From, requestHeader.To, requestHeader.CSeq, requestHeader.CallId);

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
			catch(Exception excp)
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
			catch(Exception excp)
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
			catch(Exception excp)
			{
				logger.Error("Exception GetErrorResponse. " + excp.Message);
				throw excp;
			}
		}

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent) {
            if (m_registrarLogEvent != null) {
                try {
                    m_registrarLogEvent(monitorEvent);
                }
                catch (Exception excp) {
                    logger.Error("Exception FireProxyLogEvent RegistrarCore. " + excp.Message);
                }
            }
        }
	}
}
