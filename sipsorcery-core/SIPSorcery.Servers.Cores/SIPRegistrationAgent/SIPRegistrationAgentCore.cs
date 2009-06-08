// ============================================================================
// FileName: SIPRegistrationAgent.cs
//
// Description:
// Registration agent daemon to maintain SIP registrations with multiple SIP
// Registrar servers.
//
// Author(s):
// Aaron Clauson
//
// History:
// 13 Nov 2006	Aaron Clauson	Created.
// 19 Oct 2007  Aaron Clauson   Incorporated DNS management to stop sipswitch stalling on invalid host names.
// 17 May 2008  Aaron Clauson   Refactored UserRegistration class into its own class file.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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

//extern alias syscore;
//using syscore::System.Linq.Expressions;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using Heijden.DNS;
using log4net;

//using SysCoreExpression = syscore.System.Linq.Expressions;
//using SysCoreFunc = syscore.System;

#if UNITTEST
using NUnit.Framework;
#endif

namespace SIPSorcery.Servers
{
    public class SIPRegistrationAgentCore
	{
        public const int REGISTRATION_RENEWAL_PERIOD = 1000;        // Time in milliseconds between the registration agent checking registrations.
        public const int REGISTRATION_HEAD_TIME = 5;                // Time in seconds to go to next registration to initate.
        public const int REGISTER_FAILURERETRY_INTERVAL = 300;      // Number of seconds between consecutive register requests in the event of failures or timeouts.
        public const int REGISTER_FAILEDDNS_RETRY_INTERVAL = 180;   // Minimum number of seconds between consecutive register requests in the event of a DNS failure resolving the registrar server.
        public const int REGISTER_EMPTYDNS_RETRY_INTERVAL = 5;      // When the DNS manager has not yet had time to do the lookup wait this number of seconds and try again.
        public const int REGISTER_CHECKTIME_THRESHOLD = 3;          // Time the user registration checks should be taking less than. If exceeded a log message is produced.
        public const int REGISTER_EXPIREALL_WAITTIME = 2000;        // When stopping the registration agent the time to give after the initial request for all requests to complete.
        public const int REGISTER_DELETION_TIMEOUT = 60;            // Number of seconds a deletion request will timeout after.
        public const int REGISTER_MINIMUM_EXPIRY = 60;              // The minimum interval a registration will be accepted for. Anything less than this interval will use this minimum value.
        public const int REGISTER_MINIMUM_ATTEMPT = 50;             // The minimum interval at which consecutive registration attempts can occur.
        private const int DNS_SYNCHRONOUS_TIMEOUT = 3;              // For operations that need to so a synchronous DNS lookup such as binding removals the amount of time for the lookup.
        private const int MAX_DNS_FAILURE_ATTEMPTS = 6;
        private const string DNS_FAILURE_MESSAGE_FORMAT = "DNS resolution failed, attempts";

        private static ILog logger = AppState.GetLogger("sipregagent");
        private static readonly string m_userAgentString = SIPConstants.SIP_USERAGENT_STRING;
        private static readonly string m_regAgentContactId = SIPProviderBinding.REGAGENT_CONTACT_ID_KEY;
        
        private bool m_sendRegisters = true;    // While true the register agent thread will send out register requests to maintain it's registrations.

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;

        private Dictionary<Guid, SIPProviderBinding> m_inTransitBindings = new Dictionary<Guid, SIPProviderBinding>();

        private SIPMonitorLogDelegate StatefulProxyLogEvent_External;
        private SIPAssetGetByIdDelegate<SIPProvider> GetSIPProviderById_External;
        private SIPAssetUpdateDelegate<SIPProvider> UpdateSIPProvider_External;
        private SIPAssetPersistor<SIPProviderBinding> m_bindingPersistor;

        //private Expression<Func<SIPProviderBinding, bool>> m_getNextBinding = b => b.NextRegistrationTime <= DateTime.Now && b.LastRegisterAttempt <= DateTime.Now.AddSeconds(REGISTER_MINIMUM_ATTEMPT);

        public SIPRegistrationAgentCore(
            SIPMonitorLogDelegate logDelegate, 
            SIPTransport sipTransport, 
            SIPEndPoint outboundProxy, 
            SIPAssetGetByIdDelegate<SIPProvider> getSIPProviderById,
            SIPAssetUpdateDelegate<SIPProvider> updateSIPProvider,
            SIPAssetPersistor<SIPProviderBinding> bindingPersistor)
		{
            StatefulProxyLogEvent_External = logDelegate;
            GetSIPProviderById_External = getSIPProviderById;
            UpdateSIPProvider_External = updateSIPProvider;
            m_bindingPersistor = bindingPersistor;
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
        }

		public void Start()
		{
            logger.Debug("SIPRegistrationAgent thread started.");

            Thread regThread = new Thread(new ThreadStart(MonitorRegistrations));
	        regThread.Start();	
		}

        public void Stop()
        {
            try
            {
                m_sendRegisters = false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationAgent Stop. " + excp.Message);
            }
        }

        public void SendContactRemovalRequest(SIPProvider sipProvider)
        {
            if (sipProvider != null)
            {
                SIPProviderBinding binding = m_bindingPersistor.Get(b => b.Id == sipProvider.Id);
                if (binding != null && binding.IsRegistered) {
                    ThreadPool.QueueUserWorkItem(new WaitCallback(SendContactRemovalRequestAsync), sipProvider);
                }
            }
        }

		/// <summary>
		/// Retrieve a list of accounts that the agent will register for from the database and then monitor them and any additional ones inserte.
		/// </summary>
        private void MonitorRegistrations()
        {
            try
            {
                while (m_sendRegisters)
                {
                    try {
                        SIPProviderBinding binding = m_bindingPersistor.Get(b => b.NextRegistrationTime <= DateTime.Now);

                        while (binding != null) {
                            // Get the SIPProvider for the binding.
                            SIPProvider provider = GetSIPProviderById_External(new Guid(binding.ProviderId));

                            if (binding.BindingExpiry != 0 && (provider == null || !provider.RegisterEnabled || !provider.RegisterAdminEnabled)) {
                                // The SIP Provider entry has been removed or disabled, delete the binding. It's CRITICAL that this check is done to
                                // prevent bindings being maintained after a user has deleted the provider or turned off registrations for it.
                                m_bindingPersistor.Delete(binding);
                            }
                            //else if (binding.BindingExpiry != 0 && binding.LastRegisterAttempt > DateTime.Now.AddSeconds(REGISTER_MINIMUM_ATTEMPT * -1)) {
                            else if (binding.BindingExpiry != 0 && binding.LastRegisterAttempt != null && binding.LastRegisterAttempt.Value > DateTime.Now.AddSeconds(REGISTER_MINIMUM_ATTEMPT * -1)) {
                                // Registration requests too frequent. The attempt will be delayed.
                                double lastAttemptSecs = DateTime.Now.Subtract(binding.LastRegisterAttempt.Value).TotalSeconds;
                                int delaySeconds = REGISTER_MINIMUM_ATTEMPT - (int)lastAttemptSecs;
                                binding.RegistrationFailureMessage = "Registration attempts too frequent, delaying " + delaySeconds + "s.";
                                binding.NextRegistrationTime = DateTime.Now.AddSeconds(delaySeconds);
                                m_bindingPersistor.Update(binding);
                                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRefresh, "SIP Provider registration request for " + binding.ProviderName + " too frequent, delaying by " + delaySeconds + "s to " + binding.NextRegistrationTime.ToString("dd MMM yy HH:mm:ss") + ".", binding.Owner));
                            }
                            else {
                                // If the binding expiry is 0 the agent is removing an existing binding in which case it should use the original settings
                                // it sent the registration with.
                                if (binding.BindingExpiry != 0) {
                                    binding.SetProviderFields(provider);
                                }
                                else {
                                    // This is an expired binding and now that it is loaded for the expiration attempt it can be deleted. It's still in the local cache for any authorisations required.
                                    m_bindingPersistor.Delete(binding);
                                    if (binding.RegistrarServer == null) {
                                        // Can't expire a binding if the settings are not known, just delete it and worst case it will be removed after the expiration period.
                                        continue;
                                    }
                                }

                                try {
                                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Checking SIP Provider registration for " + binding.ProviderName + ".", binding.Owner));

                                    SIPEndPoint registrarEndPoint = SIPDNSManager.Resolve(binding.RegistrarServer, false);

                                    if (registrarEndPoint == null) {
                                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "DNS Manager does not currently have an entry for " + binding.RegistrarServer.ToString() + ", delaying " + REGISTER_EMPTYDNS_RETRY_INTERVAL + "s.", binding.Owner));
                                        binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_EMPTYDNS_RETRY_INTERVAL);
                                        if (binding.RegistrationFailureMessage == null) {
                                            binding.RegistrationFailureMessage = DNS_FAILURE_MESSAGE_FORMAT + " 1.";
                                        }
                                        else {
                                            if (Regex.Match(binding.RegistrationFailureMessage, DNS_FAILURE_MESSAGE_FORMAT).Success) {
                                                int attempts = Convert.ToInt32(Regex.Match(binding.RegistrationFailureMessage, @"(?<attempts>\d)\.$").Result("${attempts}"));
                                                if (attempts >= MAX_DNS_FAILURE_ATTEMPTS) {
                                                    provider.RegisterEnabled = false;
                                                    provider.RegisterDisabledReason = "Could not resolve registrar.";
                                                    UpdateSIPProvider_External(provider);
                                                    RemoveCachedBinding(new Guid(binding.Id));
                                                    m_bindingPersistor.Delete(binding);
                                                }
                                                else {
                                                    attempts++;
                                                    binding.RegistrationFailureMessage = DNS_FAILURE_MESSAGE_FORMAT + " " + attempts + ".";
                                                }
                                            }
                                            else {
                                                binding.RegistrationFailureMessage = DNS_FAILURE_MESSAGE_FORMAT + " 1.";
                                            }
                                        }
                                        m_bindingPersistor.Update(binding);
                                    }
                                    /*else if (dnsEntry.Error != null || dnsEntry.RecordsA == null || dnsEntry.RecordsA.Length == 0) {
                                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.DNS, "Error resolving " + registrarServer + " please check. " + dnsEntry.Error, binding.Owner));

                                        // For SIP Registrars that can't be found in DNS only retry at a longer interval.
                                        binding.RegistrationFailureMessage = "Could not resolve " + registrarServer + ". " + dnsEntry.Error;
                                        binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_FAILEDDNS_RETRY_INTERVAL);
                                        m_bindingPersistor.Update(binding);
                                    }*/
                                    else {
                                        binding.RegistrarSIPEndPoint = registrarEndPoint;
                                        binding.LastRegisterAttempt = DateTime.Now;
                                        binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                                        binding.LocalSIPEndPoint = (m_outboundProxy != null) ? m_sipTransport.GetDefaultSIPEndPoint(m_outboundProxy.SIPProtocol) : m_sipTransport.GetDefaultSIPEndPoint(binding.RegistrarServer.Protocol);
                                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Sending initial register for " + binding.Owner + " and " + binding.ProviderName + " to " + registrarEndPoint.ToString() + ".", binding.Owner));
                                        if (binding.BindingExpiry != 0) {
                                            m_bindingPersistor.Update(binding);
                                        }

                                        // Cache the binding details for responses to this request ONLY. When a new request is due the details must be populated
                                        // from the SIPProvider directly to pick up any updates.
                                        lock (m_inTransitBindings) {
                                            RemoveCachedBinding(new Guid(binding.Id));
                                            m_inTransitBindings.Add(new Guid(binding.Id), binding);
                                        }

                                        SendInitialRegister(binding, binding.LocalSIPEndPoint, binding.RegistrarSIPEndPoint, binding.BindingExpiry);
                                    }
                                }
                                catch (Exception regExcp) {
                                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Error, "Exception attempting to register provider " + binding.ProviderName + ". " + regExcp.Message, binding.Owner));

                                    try {
                                        // If a register attempt generates an exception then disable it.
                                        if (provider != null) {
                                            provider.RegisterEnabled = false;
                                            provider.RegisterDisabledReason = regExcp.Message;
                                            UpdateSIPProvider_External(provider);
                                        }

                                        RemoveCachedBinding(new Guid(binding.Id));
                                        m_bindingPersistor.Delete(binding);
                                    }
                                    catch (Exception persistExcp) {
                                        logger.Error("Exception SIPRegistrationAgent persisting after exception. " + persistExcp.Message);
                                    }
                                }
                            }

                            binding = m_bindingPersistor.Get(b => b.NextRegistrationTime <= DateTime.Now);
                        }
                    }
                    catch (Exception persistExcp) {
                        // Need this now with nHibernate going belly up all the time.
                        logger.Error("Exception MonitorRegistrations GettingBinding. " + persistExcp.Message);
                    }
                    
                    Thread.Sleep(REGISTRATION_RENEWAL_PERIOD);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception MonitorRegistrations. " + excp.Message);
            }
        }

        private void SendInitialRegister(SIPProviderBinding binding, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, int expirySeconds)
        {
            try
            {
                binding.CSeq++;

                FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Initiating registration for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ".", binding.Owner));
                SIPRequest regRequest = GetRegistrationRequest(binding, localSIPEndPoint, expirySeconds, remoteEndPoint);

                SIPNonInviteTransaction regTransaction = m_sipTransport.CreateNonInviteTransaction(regRequest, binding.RegistrarSIPEndPoint, localSIPEndPoint, m_outboundProxy);
                regTransaction.NonInviteTransactionFinalResponseReceived += ServerResponseReceived;
                regTransaction.NonInviteTransactionTimedOut += regTransaction_TransactionTimedOut;

                m_sipTransport.SendSIPReliable(regTransaction);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendInitialRegister for " + binding.Owner + " and " + binding.RegistrarServer.ToString() + ". " + excp.Message);
            }
        }

        private void regTransaction_TransactionTimedOut(SIPTransaction sipTransaction)
        {
            try
            {
                SIPRequest sipRequest = sipTransaction.TransactionRequest;
                Guid callIdGuid = new Guid(sipRequest.Header.CallId);
                SIPProviderBinding binding = m_bindingPersistor.Get(callIdGuid);

                if (binding != null && binding.BindingExpiry != 0) {
                    binding.RegistrationFailureMessage = "Registration to " + binding.RegistrarSIPEndPoint.ToString() + " timed out.";
                    binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                    binding.IsRegistered = false;
                    m_bindingPersistor.Update(binding);
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration timed out for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", next attempt in " + REGISTER_FAILURERETRY_INTERVAL + "s.", binding.Owner));
                    FireProxyLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingUpdate, binding.Owner, binding.RegistrarSIPEndPoint, binding.ProviderId.ToString()));

                    RemoveCachedBinding(new Guid(binding.Id));
                }
                else {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Warn, "Registration request timed for unmatched call originally to " + sipTransaction.RemoteEndPoint + ".", null));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception regTransaction_TransactionTimedOut. " + excp.Message);
            }
        }

        /// <summary>
        /// The event handler for responses to the initial register request.
        /// </summary>
        private void ServerResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse) {
            try {
                Guid callIdGuid = new Guid(sipResponse.Header.CallId);
                SIPProviderBinding binding = GetBinding(callIdGuid);

                if (binding != null) {
                    if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised) {
                        if (sipResponse.Header.AuthenticationHeader != null) {
                            SIPRequest authenticatedRequest = GetAuthenticatedRegistrationRequest(binding, sipTransaction.TransactionRequest, sipResponse);
                            SIPNonInviteTransaction regAuthTransaction = m_sipTransport.CreateNonInviteTransaction(authenticatedRequest, binding.RegistrarSIPEndPoint, localSIPEndPoint, m_outboundProxy);
                            regAuthTransaction.NonInviteTransactionFinalResponseReceived += AuthResponseReceived;
                            regAuthTransaction.NonInviteTransactionTimedOut += regTransaction_TransactionTimedOut;
                            m_sipTransport.SendSIPReliable(regAuthTransaction);
                        }
                        else if(binding.BindingExpiry != 0){
                            binding.IsRegistered = false;
                            binding.RegistrationFailureMessage = "Server did not provide auth header, check realm.";
                            binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                            m_bindingPersistor.Update(binding);
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", the server did not respond with an authentication header, check realm.", binding.Owner));
                            RemoveCachedBinding(new Guid(binding.Id));
                        }
                    }
                    else if(binding.BindingExpiry != 0){
                        // Non 401 or 407 responses mean the registration attempt is finished and the call-id will be removed and state updated.
                        if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok) {
                            // Successful registration.
                            OkResponseReceived(sipTransaction, remoteEndPoint, sipResponse);
                        }
                        else if (sipResponse.Status == SIPResponseStatusCodesEnum.Forbidden || sipResponse.Status == SIPResponseStatusCodesEnum.NotFound) {
                            // SIP account does not appear to exist. Disable registration attempts until user intervenes to correct.
                            m_bindingPersistor.Delete(binding);
                            DisableSIPProviderRegistration(new Guid(binding.ProviderId), "Authentication failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").");
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", DISABLING.", binding.Owner));
                            RemoveCachedBinding(new Guid(binding.Id));
                        }
                        else if (sipResponse.Status == SIPResponseStatusCodesEnum.IntervalTooBrief ||
                                 (sipResponse.Status == SIPResponseStatusCodesEnum.BusyEverywhere && sipResponse.ReasonPhrase == "Too Frequent Requests")) // FWD uses this to indicate it doesn't like the timing.
                        {
                            binding.IsRegistered = false;
                            binding.RegistrationFailureMessage = "Registration failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").";
                            binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_FAILURERETRY_INTERVAL + Crypto.GetRandomInt(0, REGISTER_FAILURERETRY_INTERVAL));
                            m_bindingPersistor.Update(binding);
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + " due to " + sipResponse.ReasonPhrase + ".", binding.Owner));
                            RemoveCachedBinding(new Guid(binding.Id));
                        }
                        else {
                            binding.IsRegistered = false;
                            binding.RegistrationFailureMessage = "Registration failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").";
                            binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                            m_bindingPersistor.Update(binding);
                            FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ".", binding.Owner));
                            RemoveCachedBinding(new Guid(binding.Id));
                        }
                    }
                }
                else {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Warn, "An " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " from " + remoteEndPoint + " was received for an unknown call.", null));
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPRegistrationAgent ServerResponseReceived (" + remoteEndPoint + "). " + excp.Message);
            }
        }

        /// <summary>
        /// The event handler for responses to the authenticated register request.
        /// </summary>
        private void AuthResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                Guid callIdGuid = new Guid(sipResponse.Header.CallId);
                SIPProviderBinding binding = GetBinding(callIdGuid);

                if (binding != null && binding.BindingExpiry != 0)
                {
                    if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
                    {
                        OkResponseReceived(sipTransaction, remoteEndPoint, sipResponse);
                    }
                    else if (sipResponse.Status == SIPResponseStatusCodesEnum.IntervalTooBrief)
                    {
                        binding.IsRegistered = false;
                        binding.RegistrationFailureMessage = "Registration failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").";
                        binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_FAILURERETRY_INTERVAL + Crypto.GetRandomInt(0, REGISTER_FAILURERETRY_INTERVAL));
                        m_bindingPersistor.Update(binding);
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + " due to " + sipResponse.ReasonPhrase + ".", binding.Owner));
                        RemoveCachedBinding(new Guid(binding.Id));
                    }
                    else if (sipResponse.Status == SIPResponseStatusCodesEnum.Forbidden || sipResponse.Status == SIPResponseStatusCodesEnum.NotFound)
                    {
                        // SIP account does not appear to exist. Disable registration attempts until user intervenes to correct.
                        m_bindingPersistor.Delete(binding);
                        DisableSIPProviderRegistration(new Guid(binding.ProviderId), "Username was rejected by SIP Provider with " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", DISABLING.", binding.Owner));
                        RemoveCachedBinding(new Guid(binding.Id));
                    }
                    else
                    {
                        binding.IsRegistered = false;
                        binding.NextRegistrationTime = DateTime.Now.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                        binding.RegistrationFailureMessage = "Registration failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").";
                        m_bindingPersistor.Update(binding);
                        FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + " with " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".", binding.Owner));
                        RemoveCachedBinding(new Guid(binding.Id));
                    }

                    FireProxyLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingUpdate, binding.Owner, binding.RegistrarSIPEndPoint, binding.ProviderId.ToString()));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationAgent AuthResponseReceived. " + excp.Message);
            }
        }

        private void OkResponseReceived(SIPTransaction sipTransaction, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            try
            {
                Guid callIdGuid = new Guid(sipResponse.Header.CallId);
                SIPProviderBinding binding = GetBinding(callIdGuid);

                if (binding != null)
                {
                    // Updated contacts list.
                    // Find the contact in the list that matches the one being maintained by this agent in order to determine the expiry value.
                    int headerExpires = sipResponse.Header.Expires;
                    int contactExpires = -1;
                    if (sipResponse.Header.Contact != null && sipResponse.Header.Contact.Count > 0)
                    {
                        foreach (SIPContactHeader contactHeader in sipResponse.Header.Contact)
                        {
                            if (contactHeader.ContactURI.Parameters.Get(m_regAgentContactId) == binding.BindingSIPURI.Parameters.Get(m_regAgentContactId))
                            {
                                contactExpires = contactHeader.Expires;
                                break;
                            }
                        }
                    }

                    if (contactExpires != -1)
                    {
                        binding.BindingExpiry = contactExpires;
                    }
                    else if (headerExpires != -1)
                    {
                        binding.BindingExpiry = headerExpires;
                    }

                    if (binding.BindingExpiry < REGISTER_MINIMUM_EXPIRY) {
                        // Make sure we don't do a 3CX and sned registration floods.
                        binding.BindingExpiry = REGISTER_MINIMUM_EXPIRY;
                    }

                    binding.NextRegistrationTime = DateTime.Now.AddSeconds(binding.BindingExpiry - REGISTRATION_HEAD_TIME);
                    binding.ContactsList = sipResponse.Header.Contact;
                    binding.IsRegistered = true;
                    binding.LastRegisterTime = DateTime.Now;
                    binding.RegistrationFailureMessage = null;
                    m_bindingPersistor.Update(binding);
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegistered, "Contact successfully registered for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", expiry " + binding.BindingExpiry + "s.", binding.Owner));
                    RemoveCachedBinding(new Guid(binding.Id));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationAgent OkResponseReceived. " + excp.Message);
            }
        }

        private SIPRequest GetRegistrationRequest(SIPProviderBinding binding, SIPEndPoint localSIPEndPoint, int expiry, SIPEndPoint registrarEndPoint)
		{	
			try
			{
                if (!binding.BindingSIPURI.Parameters.Has(m_regAgentContactId)) {
                    binding.BindingSIPURI.Parameters.Set(m_regAgentContactId, Crypto.GetRandomString(6));
                }

                string realm = binding.RegistrarRealm;

                SIPURI registerURI = SIPURI.ParseSIPURIRelaxed(realm);
                SIPURI regUserURI = SIPURI.ParseSIPURIRelaxed(binding.ProviderUsername + "@" + realm);

                SIPFromHeader fromHeader = new SIPFromHeader(null, regUserURI, CallProperties.CreateNewTag());
                SIPToHeader toHeader = new SIPToHeader(null, regUserURI, null);
                SIPContactHeader contactHeader = new SIPContactHeader(null, binding.BindingSIPURI);
                contactHeader.Expires = binding.BindingExpiry;
                string callId = binding.Id;
                int cseq = ++binding.CSeq;	
				
                SIPRequest registerRequest = new SIPRequest(SIPMethodsEnum.REGISTER, registerURI);
                registerRequest.LocalSIPEndPoint = localSIPEndPoint;
				SIPHeader header = new SIPHeader(contactHeader, fromHeader, toHeader, cseq, callId);
				header.CSeqMethod = SIPMethodsEnum.REGISTER;
                header.UserAgent = m_userAgentString;
                header.Expires = binding.BindingExpiry;

                SIPViaHeader viaHeader = new SIPViaHeader(localSIPEndPoint, CallProperties.CreateBranchId());
				header.Vias.PushViaHeader(viaHeader);

                SIPRoute registrarRoute = new SIPRoute(new SIPURI(binding.RegistrarServer.Scheme, registrarEndPoint), true);
                header.Routes.PushRoute(registrarRoute);

                /*if (sipProvider.CustomHeaders != null && sipProvider.CustomHeaders.Trim().Length > 0)
                {
                    string[] customerHeadersList = sipProvider.CustomHeaders.Split(SIPProvider.CUSTOM_HEADERS_SEPARATOR);

                    if (customerHeadersList != null && customerHeadersList.Length > 0)
                    {
                        foreach (string customHeader in customerHeadersList)
                        {
                            if (customHeader.IndexOf(':') == -1)
                            {
                                logger.Debug("Skipping custom header due to missing colon, " + customHeader + ".");
                                continue;
                            }
                            else
                            {
                                string headerName = customHeader.Substring(0, customHeader.IndexOf(':'));
                                if (headerName != null && Regex.Match(headerName.Trim(), "(Via|From|To|Contact|CSeq|Call-ID|Max-Forwards|Content)", RegexOptions.IgnoreCase).Success)
                                {
                                    logger.Debug("Skipping custom header due to an non-permitted string in header name, " + customHeader + ".");
                                    continue;
                                }
                                else
                                {
                                    if (headerName == SIPConstants.SIP_USERAGENT_STRING)
                                    {
                                        header.UserAgent = customHeader.Substring(customHeader.IndexOf(':') + 1);
                                    }
                                    else
                                    {
                                        header.UnknownHeaders.Add(customHeader.Trim());
                                    }
                                }
                            }
                        }
                    }
                }*/

                registerRequest.Header = header;
                return registerRequest;
			}
			catch(Exception excp)
			{
                logger.Error("Exception GetRegistrationRequest. " + excp.Message);
				throw excp;
			}
		}

        private SIPRequest GetAuthenticatedRegistrationRequest(SIPProviderBinding binding, SIPRequest registerRequest, SIPResponse sipResponse)
        {
            try
            {
                SIPAuthorisationDigest authRequest = sipResponse.Header.AuthenticationHeader.SIPDigest;
                string username = binding.ProviderUsername;
                authRequest.SetCredentials(username, binding.ProviderPassword, registerRequest.URI.ToString(), SIPMethodsEnum.REGISTER.ToString());

                SIPRequest regRequest = registerRequest.Copy();
                regRequest.LocalSIPEndPoint = registerRequest.LocalSIPEndPoint;
                regRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                regRequest.Header.From.FromTag = CallProperties.CreateNewTag();
                regRequest.Header.To.ToTag = null;
                regRequest.Header.CSeq = ++binding.CSeq;
                regRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
                regRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;

                return regRequest;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetAuthenticatedRegistrationRequest. " + excp.Message);
                throw excp;
            }
        }

        private void SendContactRemovalRequestAsync(object state)
        {
            try
            {
                SIPProviderBinding binding = (SIPProviderBinding)state;
                SIPEndPoint registrarEndPoint = SIPDNSManager.Resolve(binding.RegistrarServer, true);
                
                if (registrarEndPoint == null)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRemoval, "Could not submit SIP Provider binding removal request for " + binding.ProviderName + ", DNS lookup for " + binding.RegistrarServer.ToString() + " failed.", binding.Owner));
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRemoval, "Submitting SIP Provider binding removal request for " + binding.ProviderName + ".", binding.Owner));

                    SIPEndPoint localSIPEndPoint = (binding.LocalSIPEndPoint != null) ? binding.LocalSIPEndPoint : m_sipTransport.GetDefaultSIPEndPoint(registrarEndPoint.SIPProtocol);
                    binding.CSeq = ++binding.CSeq;

                    SIPRequest regRequest = GetRegistrationRequest(binding, localSIPEndPoint, 0, registrarEndPoint);
                    regRequest.LocalSIPEndPoint = binding.LocalSIPEndPoint;
                    regRequest.Header.Expires = 0;
                    SIPContactHeader contactHeader = new SIPContactHeader(null, binding.BindingSIPURI);
                    regRequest.Header.Contact = new List<SIPContactHeader>();
                    regRequest.Header.Contact.Add(contactHeader);
                    regRequest.Header.Contact[0].Expires = 0;

                    SIPNonInviteTransaction regTransaction = m_sipTransport.CreateNonInviteTransaction(regRequest, m_outboundProxy, localSIPEndPoint, m_outboundProxy);
                    regTransaction.NonInviteTransactionFinalResponseReceived += new SIPTransactionResponseReceivedDelegate(
                      (localEndPoint, remoteEndPoint, transaction, sipResponse) =>
                      {
                          // If requested authenticate the binding removal registration request.
                          if ((sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised) && sipResponse.Header.AuthenticationHeader != null)
                          {
                              SIPRequest authenticatedRequest = GetAuthenticatedRegistrationRequest(binding, transaction.TransactionRequest, sipResponse);
                              SIPNonInviteTransaction regAuthTransaction = m_sipTransport.CreateNonInviteTransaction(authenticatedRequest, m_outboundProxy, localSIPEndPoint, m_outboundProxy);
                              m_sipTransport.SendSIPReliable(regAuthTransaction);
                          }
                      }
                    );

                    m_sipTransport.SendSIPReliable(regTransaction);

                    FireProxyLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingRemoval, binding.Owner, registrarEndPoint, binding.ProviderId.ToString()));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendContactRemovalRequest. " + excp.Message);
            }
        }

        /// <summary>
        /// Sends a registration request with an empty contact list. The SIP Registrar should return a list of all the currently registered contacts allowing 
        /// the list held to be refreshed.
        /// </summary>
        /// <param name="registrationId"></param>
        private void SendEmptyRegistrationRequest(SIPProviderBinding binding)
        {
            try
            {
                if (binding.RegistrarSIPEndPoint == null)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Warn, "Could not submit SIP Provider Binding status request for " + binding.ProviderName + ", the Registrar socket was empty.", binding .Owner));
                }
                else if (binding.LocalSIPEndPoint == null)
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Warn, "Could not submit SIP Provider Binding status request for " + binding.ProviderName + ", the local socket to send from was empty.", binding.Owner));
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRefresh, "Submitting registration refresh request for " + binding.Owner + " and " + binding.RegistrarServer.ToString() + ".", binding.Owner));

                    binding.CSeq = binding.CSeq++;

                    SIPRequest regRequest = GetRegistrationRequest(binding, binding.LocalSIPEndPoint, 0, binding.RegistrarSIPEndPoint);
                    regRequest.LocalSIPEndPoint = binding.LocalSIPEndPoint;
                    regRequest.Header.Expires = -1;                             // Stops the Expires header being sent.
                    regRequest.Header.Contact = new List<SIPContactHeader>();   // Stops the Contact header being sent.

                    SIPNonInviteTransaction regTransaction = m_sipTransport.CreateNonInviteTransaction(regRequest, m_outboundProxy, binding.LocalSIPEndPoint, m_outboundProxy);
                    regTransaction.NonInviteTransactionFinalResponseReceived += new SIPTransactionResponseReceivedDelegate(ServerResponseReceived);

                    m_sipTransport.SendSIPReliable(regTransaction);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendEmptyRegistrationRequest. " + excp.Message);
            }
        }

        private void RemoveCachedBinding(Guid bindingId) {
            lock (m_inTransitBindings) {
                if (m_inTransitBindings.ContainsKey(bindingId)) {
                    m_inTransitBindings.Remove(bindingId);
                }
            }
        }

        private SIPProviderBinding GetBinding(Guid bindingId) {
            SIPProviderBinding binding = null;

            // If the binding is in the local cache use that.
            lock (m_inTransitBindings) {
                if (m_inTransitBindings.ContainsKey(bindingId)) {
                    binding = m_inTransitBindings[bindingId];
                }
            }

            // If binding wasn't found in the cache try and load from persistence store.
            if (binding == null) {
                binding = m_bindingPersistor.Get(bindingId);
            }

            return binding;
        }

        private void DisableSIPProviderRegistration(Guid providerId, string disabledReason)
        {
            try {
                SIPProvider sipProvider = GetSIPProviderById_External(providerId);
                sipProvider.RegisterEnabled = false;
                sipProvider.RegisterDisabledReason = disabledReason;
                UpdateSIPProvider_External(sipProvider);
            }
            catch (Exception excp) {
                logger.Error("Exception DisableSIPProviderRegistration. " + excp.Message);
            }
        }

        private void FireProxyLogEvent(SIPMonitorEvent monitorEvent)
        {
            if (StatefulProxyLogEvent_External != null)
            {
                try
                {
                    StatefulProxyLogEvent_External(monitorEvent);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception FireProxyLogEvent SIPRegistrationAgent. " + excp.Message);
                }
            }
        }
	}
}
