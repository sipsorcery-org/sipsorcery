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
// 01 Mar 2011  Aaron Clauson   Switched to using concurrent collections.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) Aaron Clauson (aaron@sipsorcery.com), SIP Sorcery Ltd, Hobart, Australia (www.sipsorcery.com)
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are permitted provided that 
// the following conditions are met:
//
// Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer. 
// Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following 
// disclaimer in the documentation and/or other materials provided with the distribution. Neither the name of SIP Sorcery Ltd. 
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Transactions;
using System.Xml;
using SIPSorcery.Net;
using SIPSorcery.Persistence;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using Heijden.DNS;
using log4net;

namespace SIPSorcery.Servers
{
    public class SIPRegistrationAgentCore
    {
        public const int REGISTRATION_RENEWAL_PERIOD = 1000;        // Time in milliseconds between the registration agent checking registrations.
        public const int REGISTRATION_HEAD_TIME = 5;                // Time in seconds to go to next registration to initate.
        public const int REGISTER_FAILURERETRY_INTERVAL = 300;      // Number of seconds between consecutive register requests in the event of failures or timeouts.
        public const int REGISTER_DNSTIMEOUT_RETRY_INTERVAL = 300;  // The number of seconds between consecutive register requests in the event of a DNS timeout or error on previously resolved hostname resolving the registrar server.
        public const int REGISTER_EMPTYDNS_RETRY_INTERVAL = 10;      // When the DNS manager has not yet had time to do the lookup wait this number of seconds and try again.
        public const int REGISTER_CHECKTIME_THRESHOLD = 3;          // Time the user registration checks should be taking less than. If exceeded a log message is produced.
        public const int REGISTER_EXPIREALL_WAITTIME = 2000;        // When stopping the registration agent the time to give after the initial request for all requests to complete.
        public const int REGISTER_DELETION_TIMEOUT = 60;            // Number of seconds a deletion request will timeout after.
        public const int REGISTER_MINIMUM_EXPIRY = 60;              // The minimum interval a registration will be accepted for. Anything less than this interval will use this minimum value.
        public const int REGISTER_MINIMUM_ATTEMPT = 50;             // The minimum interval at which consecutive registration attempts can occur.
        //private const int DNS_SYNCHRONOUS_TIMEOUT = 3;              // For operations that need to so a synchronous DNS lookup such as binding removals the amount of time for the lookup.
        //private const int MAX_DNS_FAILURE_ATTEMPTS = 6;
        //private const string DNS_FAILURE_MESSAGE_PREFIX = "DNS Failure:";
        public const int DNS_FAILURE_RETRY_WINDOW = 180;                        // If a new provider's DNS lookups fail for this length of time the binding will be disabled
        public const int DNS_FAILURE_EXISTING_PROVIDER_RETRY_WINDOW = 2880;     // (2 days) If an existing provider's DNS lookups fail for this length of time the binding will be disabled..
        private const string THREAD_NAME_PREFIX = "regagent-";
        private const int NUMBER_BINDINGS_PER_DB_ROUNDTRIP = 20;
        private const int MAX_NUMBER_INTRANSIT_BINDINGS = 100000;    // The maximum number of in transit REGISTER bindings that will be stored.

        private static ILog logger = AppState.GetLogger("sipregagent");
        private static readonly string m_userAgentString = SIPConstants.SIP_USERAGENT_STRING;
        private static readonly string m_regAgentContactId = SIPProviderBinding.REGAGENT_CONTACT_ID_KEY;

        private bool m_disallowPrivateIPRegistrars;     // If set to true any SIP providers that resolve to a private or loopback IP address will be disabled.
        private bool m_sendRegisters = true;             // While true the register agent thread will send out register requests to maintain it's registrations.
        private int m_bindingsProcessedCount = 0;

        private SIPTransport m_sipTransport;
        private SIPEndPoint m_outboundProxy;

        private ConcurrentDictionary<Guid, SIPProviderBinding> m_inTransitBindings;

        private SIPMonitorLogDelegate StatefulProxyLogEvent_External;
        private SIPAssetGetByIdDelegate<SIPProvider> GetSIPProviderById_External;
        private SIPAssetUpdateDelegate<SIPProvider> UpdateSIPProvider_External;
        private SIPAssetUpdatePropertyDelegate<SIPProvider> UpdateSIPProviderProperty_External;
        private SIPAssetPersistor<SIPProviderBinding> m_bindingPersistor;

        public SIPRegistrationAgentCore(
            SIPMonitorLogDelegate logDelegate,
            SIPTransport sipTransport,
            SIPEndPoint outboundProxy,
            SIPAssetGetByIdDelegate<SIPProvider> getSIPProviderById,
            SIPAssetUpdateDelegate<SIPProvider> updateSIPProvider,
            SIPAssetUpdatePropertyDelegate<SIPProvider> updateSIPProviderProperty,
            SIPAssetPersistor<SIPProviderBinding> bindingPersistor,
            bool disAllowPrivateIPRegistrars)
        {
            StatefulProxyLogEvent_External = logDelegate;
            GetSIPProviderById_External = getSIPProviderById;
            UpdateSIPProvider_External = updateSIPProvider;
            UpdateSIPProviderProperty_External = updateSIPProviderProperty;
            m_bindingPersistor = bindingPersistor;
            m_sipTransport = sipTransport;
            m_outboundProxy = outboundProxy;
            m_disallowPrivateIPRegistrars = disAllowPrivateIPRegistrars;
        }

        public void Start(int threadCount)
        {
            logger.Debug("SIPRegistrationAgent thread started with " + threadCount + " threads.");

            m_inTransitBindings = new ConcurrentDictionary<Guid, SIPProviderBinding>(threadCount, MAX_NUMBER_INTRANSIT_BINDINGS);

            for (int index = 1; index <= threadCount; index++)
            {
                string threadSuffix = index.ToString();
                ThreadPool.QueueUserWorkItem(delegate { MonitorRegistrations(THREAD_NAME_PREFIX + threadSuffix); });
            }
        }

        public void Stop()
        {
            m_sendRegisters = false;
        }

        /// <summary>
        /// Retrieve a list of accounts that the agent will register for from the database and then monitor them and any additional ones inserted.
        /// </summary>
        private void MonitorRegistrations(string threadName)
        {
            try
            {
                Thread.CurrentThread.Name = threadName;

                while (m_sendRegisters)
                {
                    try
                    {
                        List<SIPProviderBinding> bindingsList = GetNextBindings(NUMBER_BINDINGS_PER_DB_ROUNDTRIP);

                        while (bindingsList != null && bindingsList.Count > 0)
                        {
                            ConcurrentBag<SIPProviderBinding> bindings = new ConcurrentBag<SIPProviderBinding>(bindingsList);

                            foreach (SIPProviderBinding binding in bindings)
                            {
                                DateTime startTime = DateTime.Now;

                                // Remove any previously in progress bindings from the in transit list. The pevious attempt should be well gone but make sure anyway.
                                RemoveCachedBinding(binding.Id);

                                // Get the SIPProvider for the binding.
                                SIPProvider provider = GetSIPProviderById_External(binding.ProviderId);
                                //SIPProvider provider = GetSIPProviderByDirectQuery_External(m_selectSIPProvider, new SqlParameter("1", binding.ProviderId));

                                if (provider == null || !provider.RegisterEnabled || !provider.RegisterAdminEnabled || binding.BindingExpiry == 0)
                                {
                                    // The SIP Provider entry has been removed or disabled: send a zero expiry register and delete the binding.
                                    // It's CRITICAL that this check is done to prevent bindings being maintained after a user has deleted the 
                                    // provider or turned off registrations for it.
                                    if (binding.IsRegistered && provider != null)
                                    {
                                        // Set the binding fields from the provider so the zero expiry register request can be sent.
                                        binding.SetProviderFields(provider);

                                        // If the binding expiry is 0 the agent is removing an existing binding in which case it should use the original settings
                                        // it sent the registration with.
                                        if (binding.RegistrarSIPEndPoint != null)
                                        {
                                            binding.LocalSIPEndPoint = (m_outboundProxy != null) ? m_sipTransport.GetDefaultSIPEndPoint(m_outboundProxy.Protocol) : m_sipTransport.GetDefaultSIPEndPoint(binding.RegistrarSIPEndPoint.Protocol);

                                            // Want to remove this binding, send a register with a 0 expiry.
                                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Sending zero expiry register for " + binding.Owner + " and " + binding.ProviderName + " to " + binding.RegistrarSIPEndPoint.ToString() + ".", binding.Owner));
                                            SendInitialRegister(provider, binding, binding.LocalSIPEndPoint, binding.RegistrarSIPEndPoint, 0);
                                        }
                                    }

                                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRemoval, "Deleting SIP Provider Binding for " + binding.Owner + " and " + binding.ProviderName + " for SIP Provider " + binding.ProviderName + ".", binding.Owner));
                                    m_bindingPersistor.Delete(binding);
                                }
                                else if (binding.LastRegisterAttempt != null && binding.LastRegisterAttempt.Value > DateTimeOffset.UtcNow.AddSeconds(REGISTER_MINIMUM_ATTEMPT * -1))
                                {
                                    // Registration requests too frequent. The attempt will be delayed.
                                    // Set the binding fields from the provider in case any have changed since the binding was last stored.
                                    binding.SetProviderFields(provider);
                                    double lastAttemptSecs = DateTimeOffset.UtcNow.Subtract(binding.LastRegisterAttempt.Value).TotalSeconds;
                                    int delaySeconds = REGISTER_MINIMUM_ATTEMPT - (int)lastAttemptSecs;
                                    binding.RegistrationFailureMessage = "Registration attempts too frequent, delaying " + delaySeconds + "s.";
                                    binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
                                    m_bindingPersistor.Update(binding);
                                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRefresh, "SIP Provider registration request for " + binding.ProviderName + " too frequent, delaying by " + delaySeconds + "s to " + binding.NextRegistrationTime.ToString("o") + ".", binding.Owner));
                                }
                                else
                                {
                                    // Set the binding fields from the provider in case any have changed since the binding was last stored.
                                    binding.SetProviderFields(provider);

                                    try
                                    {
                                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Checking SIP Provider registration for " + binding.ProviderName + ".", binding.Owner));
                                        SIPDNSLookupResult lookupResult = SIPDNSManager.ResolveSIPService(binding.RegistrarServer, true);
                                        if (lookupResult.LookupError != null)
                                        {
                                            if (binding.LastRegisterTime == null)
                                            {
                                                // If this binding has never been successful then the DNS error indicates the registrar cannot be resolved, permanently disable it.
                                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "DNS resolution for " + binding.RegistrarServer.ToString() + " failed. " + lookupResult.LookupError + ". DISABLING.", binding.Owner));
                                                DisableSIPProviderRegistration(provider.Id, "DNS resolution for registrar " + binding.RegistrarServer.ToString() + " returned an error. DNS " + lookupResult.LookupError);
                                                m_bindingPersistor.Delete(binding);
                                            }
                                            //else if (DateTimeOffset.UtcNow.Subtract(provider.LastUpdate).TotalMinutes > DNS_FAILURE_EXISTING_PROVIDER_RETRY_WINDOW)
                                            //{
                                            //    // A previously registering provider has now failed to be resolved for a long time so assume that the hostname is invalid.
                                            //    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Could not resolve registrar " + binding.RegistrarServer.ToString() + " after trying for " + DNS_FAILURE_EXISTING_PROVIDER_RETRY_WINDOW + " minutes. DISABLING.", binding.Owner));
                                            //    DisableSIPProviderRegistration(provider.Id, "DNS resolution for registrar " + binding.RegistrarServer.ToString() + " still had an error of, " + lookupResult.LookupError + " after trying for " + DNS_FAILURE_EXISTING_PROVIDER_RETRY_WINDOW + " minutes.");
                                            //    m_bindingPersistor.Delete(binding);
                                            //}
                                            else
                                            {
                                                // This binding was able to previously register so the problem could be a transient DNS probem. Delay the registration to give the problem a chance to clear up.
                                                var errorMinutes = DateTimeOffset.UtcNow.Subtract(provider.LastUpdate).TotalMinutes;
                                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "DNS resolution for " + binding.RegistrarServer.ToString() + " has been in error for " + errorMinutes + " minutes, delaying next attempt by " + REGISTER_DNSTIMEOUT_RETRY_INTERVAL + "s.", binding.Owner));
                                                binding.RegistrationFailureMessage = "DNS resolution for " + binding.RegistrarServer.ToString() + " has been in error for " + errorMinutes + " minutes. " + lookupResult.LookupError + " Delaying next attempt by " + REGISTER_DNSTIMEOUT_RETRY_INTERVAL + "s.";
                                                binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_DNSTIMEOUT_RETRY_INTERVAL);
                                                m_bindingPersistor.Update(binding);
                                            }
                                        }
                                        else if (lookupResult.ATimedoutAt != null)
                                        {
                                            if (binding.LastRegisterTime == null && DateTimeOffset.UtcNow.Subtract(provider.LastUpdate).TotalMinutes > DNS_FAILURE_RETRY_WINDOW)
                                            {
                                                // The DNS retry window has expired and the binding has never successfully registered so it's highly likely it's an invalid hostname.
                                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Could not resolve registrar " + binding.RegistrarServer.ToString() + " after trying for " + DNS_FAILURE_RETRY_WINDOW + " minutes. DISABLING.", binding.Owner));
                                                DisableSIPProviderRegistration(provider.Id, "Could not resolve registrar " + binding.RegistrarServer.ToString() + " after trying for " + DNS_FAILURE_RETRY_WINDOW + " minutes.");
                                                m_bindingPersistor.Delete(binding);
                                            }
                                            //else if (DateTimeOffset.UtcNow.Subtract(provider.LastUpdate).TotalMinutes > DNS_FAILURE_EXISTING_PROVIDER_RETRY_WINDOW)
                                            //{
                                            //    // A previously registering provider has now failed to be resolved for a long time so assume that the hostname is invalid.
                                            //    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Could not resolve registrar " + binding.RegistrarServer.ToString() + " after trying for " + DNS_FAILURE_EXISTING_PROVIDER_RETRY_WINDOW + " minutes. DISABLING.", binding.Owner));
                                            //    DisableSIPProviderRegistration(provider.Id, "Could not resolve registrar " + binding.RegistrarServer.ToString() + " after trying for " + DNS_FAILURE_EXISTING_PROVIDER_RETRY_WINDOW + " minutes.");
                                            //    m_bindingPersistor.Delete(binding);
                                            //}
                                            else
                                            {
                                                // DNS timeouts can be caused by network or server issues. Delay the registration to give the problem a chance to clear up.
                                                var timedoutMinutes = DateTimeOffset.UtcNow.Subtract(provider.LastUpdate).TotalMinutes;
                                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "DNS resolution for " + binding.RegistrarServer.ToString() + " has timed out for " + timedoutMinutes.ToString("0") + " minutes, delaying next attempt by " + REGISTER_DNSTIMEOUT_RETRY_INTERVAL + "s.", binding.Owner));
                                                binding.RegistrationFailureMessage = "DNS resolution for " + binding.RegistrarServer.ToString() + " has timed out for " + timedoutMinutes + " minutes.";
                                                binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_DNSTIMEOUT_RETRY_INTERVAL);
                                                m_bindingPersistor.Update(binding);
                                            }
                                        }
                                        else if (lookupResult.Pending)
                                        {
                                            // DNS lookup is pending, delay the registration attempt until the lookup is likely to have been completed.
                                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "DNS Manager does not currently have an entry for " + binding.RegistrarServer.ToString() + ", delaying next attempt by " + REGISTER_EMPTYDNS_RETRY_INTERVAL + "s.", binding.Owner));
                                            binding.RegistrationFailureMessage = "DNS resolution for " + binding.RegistrarServer.ToString() + " is pending.";
                                            binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_EMPTYDNS_RETRY_INTERVAL);
                                            m_bindingPersistor.Update(binding);
                                        }
                                        else
                                        {
                                            binding.RegistrarSIPEndPoint = lookupResult.GetSIPEndPoint();
                                            string ipAddress = binding.RegistrarSIPEndPoint.Address.ToString();

                                            if (m_disallowPrivateIPRegistrars &&
                                                (IPAddress.IsLoopback(binding.RegistrarSIPEndPoint.Address) ||
                                                IPSocket.IsPrivateAddress(ipAddress) ||
                                                binding.RegistrarSIPEndPoint.Address.ToString() == SIPTransport.BlackholeAddress.ToString()))
                                            {
                                                // The registrar resolved to a private, loopback or 0.0.0.0 address, delete the binding and disable the provider registration.
                                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "DNS resolution for " + binding.RegistrarServer.ToString() + " resolved to a private address of " + ipAddress + ". DISABLING.", binding.Owner));
                                                DisableSIPProviderRegistration(provider.Id, "Registrar resolved to a disallowed private IP address of " + ipAddress + ".");
                                                m_bindingPersistor.Delete(binding);
                                            }
                                            else
                                            {
                                                binding.LastRegisterAttempt = DateTimeOffset.UtcNow;
                                                binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                                                binding.LocalSIPEndPoint = (m_outboundProxy != null) ? m_sipTransport.GetDefaultSIPEndPoint(m_outboundProxy.Protocol) : m_sipTransport.GetDefaultSIPEndPoint(binding.RegistrarServer.Protocol);
                                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Sending initial register for " + binding.Owner + " and " + binding.ProviderName + " to " + binding.RegistrarSIPEndPoint.ToString() + ".", binding.Owner));
                                                m_bindingPersistor.Update(binding);

                                                SendInitialRegister(provider, binding, binding.LocalSIPEndPoint, binding.RegistrarSIPEndPoint, binding.BindingExpiry);
                                            }
                                        }
                                    }
                                    catch (Exception regExcp)
                                    {
                                        logger.Error("Exception attempting to register provider " + binding.ProviderName + ". " + regExcp.Message + " for " + binding.Owner + ".");
                                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Error, "Exception attempting to register provider " + binding.ProviderName + ". " + regExcp.Message, binding.Owner));

                                        try
                                        {
                                            binding.LastRegisterAttempt = DateTimeOffset.UtcNow;
                                            binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                                            binding.RegistrationFailureMessage = (regExcp.Message != null && regExcp.Message.Length > 1000) ? "Exception: " + regExcp.Message.Substring(0, 1000) : "Exception: " + regExcp.Message;
                                            m_bindingPersistor.Update(binding);
                                        }
                                        catch (Exception persistExcp)
                                        {
                                            logger.Error("Exception SIPRegistrationAgent persisting after exception. " + persistExcp.Message);
                                        }
                                    }
                                }

                                //logger.Debug("Binding entry processing took " + DateTime.Now.Subtract(startTime).TotalMilliseconds.ToString("0") + "ms.");

                                m_bindingsProcessedCount++;
                            }

                            bindingsList = GetNextBindings(NUMBER_BINDINGS_PER_DB_ROUNDTRIP);
                        }
                    }
                    catch (Exception persistExcp)
                    {
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

        private List<SIPProviderBinding> GetNextBindings(int count)
        {
            try
            {
                // No point having two threads try and use database at the same time.
                lock (this)
                {
                    DateTime startTime = DateTime.Now;

                    List<SIPProviderBinding> bindings = null;

                    using (var trans = new TransactionScope())
                    {
                        bindings = m_bindingPersistor.Get(b => b.NextRegistrationTime <= DateTimeOffset.UtcNow, "nextregistrationtime", 0, count);
                        if (bindings != null && bindings.Count > 0)
                        {
                            foreach (SIPProviderBinding binding in bindings)
                            {
                                m_bindingPersistor.UpdateProperty(binding.Id, "NextRegistrationTime", DateTimeOffset.UtcNow.AddSeconds(REGISTER_FAILURERETRY_INTERVAL));
                            }
                        }

                        trans.Complete();
                    }

                    //logger.Debug("GetNextBindings took " + DateTime.Now.Subtract(startTime).TotalMilliseconds.ToString("0") + ".");

                    return bindings;
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetNextBindings (" + excp.GetType().ToString() + "). " + excp.Message);
                return null;
            }
        }

        private void SendInitialRegister(SIPProvider sipProvider, SIPProviderBinding binding, SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, int expirySeconds)
        {
            try
            {
                m_inTransitBindings.AddOrUpdate(binding.Id, binding, (s, i) => binding);

                binding.CSeq++;

                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Initiating registration for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ".", binding.Owner));
                SIPRequest regRequest = GetRegistrationRequest(sipProvider, binding, localSIPEndPoint, expirySeconds, remoteEndPoint);

                SIPNonInviteTransaction regTransaction = m_sipTransport.CreateNonInviteTransaction(regRequest, binding.RegistrarSIPEndPoint, localSIPEndPoint, m_outboundProxy);
                regTransaction.NonInviteTransactionFinalResponseReceived += (lep, rep, tn, rsp) => { ThreadPool.QueueUserWorkItem(delegate { ServerResponseReceived(lep, rep, tn, rsp); }); };
                regTransaction.NonInviteTransactionTimedOut += (tn) => { ThreadPool.QueueUserWorkItem(delegate { RegistrationTimedOut(tn); }); };

                m_sipTransport.SendSIPReliable(regTransaction);

                SIPSorceryPerformanceMonitor.IncrementCounter(SIPSorceryPerformanceMonitor.REGISTRATION_AGENT_REGISTRATIONS_PER_SECOND);
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendInitialRegister for " + binding.Owner + " and " + binding.RegistrarServer.ToString() + ". " + excp.Message);
                RemoveCachedBinding(binding.Id);
            }
        }

        private void RegistrationTimedOut(SIPTransaction sipTransaction)
        {
            try
            {
                SIPRequest sipRequest = sipTransaction.TransactionRequest;
                Guid callIdGuid = new Guid(sipRequest.Header.CallId);
                SIPProviderBinding binding = GetBinding(callIdGuid);

                if (binding != null && binding.BindingExpiry != 0)
                {
                    RemoveCachedBinding(binding.Id);
                    int retryInterval = REGISTER_FAILURERETRY_INTERVAL + Crypto.GetRandomInt(0, REGISTER_FAILURERETRY_INTERVAL);
                    binding.RegistrationFailureMessage = "Registration to " + binding.RegistrarSIPEndPoint.ToString() + " timed out.";
                    binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(retryInterval);
                    binding.IsRegistered = false;
                    m_bindingPersistor.Update(binding);
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration timed out for " + binding.Owner + " and provider " + binding.ProviderName + " registering to " + binding.RegistrarSIPEndPoint.ToString() + ", next attempt in " + retryInterval + "s.", binding.Owner));
                    FireProxyLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingUpdate, binding.Owner, binding.RegistrarSIPEndPoint, binding.ProviderId.ToString()));
                }
                else
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Warn, "Registration request timed for unmatched call originally to " + sipTransaction.RemoteEndPoint + ".", null));
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RegistrationTimedOut. " + excp.Message);
                RemoveCachedBinding(sipTransaction.TransactionRequest.Header.CallId);
            }
        }

        /// <summary>
        /// The event handler for responses to the initial register request.
        /// </summary>
        private void ServerResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                string fromURI = (sipResponse.Header.From != null && sipResponse.Header.From.FromURI != null) ? sipResponse.Header.From.FromURI.ToString() : "NO FROM URI";
                string toURI = (sipResponse.Header.To != null && sipResponse.Header.To.ToURI != null) ? sipResponse.Header.To.ToURI.ToString() : "NO TO URI";
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Server response " + sipResponse.Status + " received for " + fromURI + " and " + toURI + ".", null));

                if (sipResponse.Header.CallId.IsNullOrBlank())
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Warn, "An " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " from " + remoteEndPoint + " was received for with an empty or missing Call-ID header.", null));
                }
                else
                {
                    Guid callIdGuid = new Guid(sipResponse.Header.CallId);
                    SIPProviderBinding binding = GetBinding(callIdGuid);

                    if (binding != null)
                    {
                        if (sipResponse.Status == SIPResponseStatusCodesEnum.ProxyAuthenticationRequired || sipResponse.Status == SIPResponseStatusCodesEnum.Unauthorised)
                        {
                            if (sipResponse.Header.AuthenticationHeader != null)
                            {
                                SIPRequest authenticatedRequest = GetAuthenticatedRegistrationRequest(binding, sipTransaction.TransactionRequest, sipResponse);
                                SIPNonInviteTransaction regAuthTransaction = m_sipTransport.CreateNonInviteTransaction(authenticatedRequest, binding.RegistrarSIPEndPoint, localSIPEndPoint, m_outboundProxy);
                                regAuthTransaction.NonInviteTransactionFinalResponseReceived += (lep, rep, tn, rsp) => { ThreadPool.QueueUserWorkItem(delegate { AuthResponseReceived(lep, rep, tn, rsp); }); };
                                regAuthTransaction.NonInviteTransactionTimedOut += (tn) => { ThreadPool.QueueUserWorkItem(delegate { RegistrationTimedOut(tn); }); };
                                m_sipTransport.SendSIPReliable(regAuthTransaction);
                            }
                            else if (binding.BindingExpiry != 0)
                            {
                                RemoveCachedBinding(binding.Id);
                                binding.IsRegistered = false;
                                binding.RegistrationFailureMessage = "Server did not provide auth header, check realm.";
                                binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                                m_bindingPersistor.Update(binding);
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", the server did not respond with an authentication header, check realm.", binding.Owner));
                            }
                        }
                        else if (binding.BindingExpiry != 0)
                        {
                            // Non 401 or 407 responses mean the registration attempt is finished and the call-id will be removed and state updated.
                            if (sipResponse.Status == SIPResponseStatusCodesEnum.Ok)
                            {
                                // Successful registration.
                                OkResponseReceived(sipTransaction, remoteEndPoint, sipResponse);
                            }
                            else if (sipResponse.Status == SIPResponseStatusCodesEnum.Forbidden || sipResponse.Status == SIPResponseStatusCodesEnum.NotFound)
                            {
                                // SIP account does not appear to exist. Disable registration attempts until user intervenes to correct.
                                RemoveCachedBinding(binding.Id);
                                m_bindingPersistor.Delete(binding);
                                DisableSIPProviderRegistration(binding.ProviderId, "Authentication failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").");
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", DISABLING.", binding.Owner));
                            }
                            else if (sipResponse.Status == SIPResponseStatusCodesEnum.IntervalTooBrief ||
                                     (sipResponse.Status == SIPResponseStatusCodesEnum.BusyEverywhere && sipResponse.ReasonPhrase == "Too Frequent Requests")) // FWD uses this to indicate it doesn't like the timing.
                            {
                                RemoveCachedBinding(binding.Id);
                                binding.IsRegistered = false;
                                binding.RegistrationFailureMessage = "Registration failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").";
                                binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_FAILURERETRY_INTERVAL + Crypto.GetRandomInt(0, REGISTER_FAILURERETRY_INTERVAL));
                                m_bindingPersistor.Update(binding);
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + " due to " + sipResponse.ReasonPhrase + ".", binding.Owner));
                            }
                            else
                            {
                                RemoveCachedBinding(binding.Id);
                                binding.IsRegistered = false;
                                binding.RegistrationFailureMessage = "Registration failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").";
                                binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                                m_bindingPersistor.Update(binding);
                                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ".", binding.Owner));
                            }
                        }
                    }
                    else
                    {
                        FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Warn, "An " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " from " + remoteEndPoint + " was received for an unknown registration.", null));
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationAgent ServerResponseReceived (" + remoteEndPoint + "). " + excp.Message);
                RemoveCachedBinding(sipResponse.Header.CallId);
            }
        }

        /// <summary>
        /// The event handler for responses to the authenticated register request.
        /// </summary>
        private void AuthResponseReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPTransaction sipTransaction, SIPResponse sipResponse)
        {
            try
            {
                string fromURI = (sipResponse.Header.From != null && sipResponse.Header.From.FromURI != null) ? sipResponse.Header.From.FromURI.ToString() : "NO FROM URI";
                string toURI = (sipResponse.Header.To != null && sipResponse.Header.To.ToURI != null) ? sipResponse.Header.To.ToURI.ToString() : "NO TO URI";
                FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterInProgress, "Server auth response " + sipResponse.Status + " received for " + fromURI + " and " + toURI + ".", null));

                if (sipResponse.Header.CallId.IsNullOrBlank())
                {
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.Warn, "An " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + " from " + remoteEndPoint + " was received for with an empty or missing Call-ID header.", null));
                }
                else
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
                            RemoveCachedBinding(binding.Id);
                            binding.IsRegistered = false;
                            binding.RegistrationFailureMessage = "Registration failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").";
                            binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_FAILURERETRY_INTERVAL + Crypto.GetRandomInt(0, REGISTER_FAILURERETRY_INTERVAL));
                            m_bindingPersistor.Update(binding);
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + " due to " + sipResponse.ReasonPhrase + ".", binding.Owner));
                        }
                        else if (sipResponse.Status == SIPResponseStatusCodesEnum.Forbidden || sipResponse.Status == SIPResponseStatusCodesEnum.NotFound)
                        {
                            // SIP account does not appear to exist. Disable registration attempts until user intervenes to correct.
                            RemoveCachedBinding(binding.Id);
                            m_bindingPersistor.Delete(binding);
                            DisableSIPProviderRegistration(binding.ProviderId, "Username was rejected by SIP Provider with " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed with " + sipResponse.Status + " for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", DISABLING.", binding.Owner));
                        }
                        else
                        {
                            RemoveCachedBinding(binding.Id);
                            binding.IsRegistered = false;
                            binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(REGISTER_FAILURERETRY_INTERVAL);
                            binding.RegistrationFailureMessage = "Registration failed (" + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ").";
                            m_bindingPersistor.Update(binding);
                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegisterFailed, "Registration failed for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + " with " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".", binding.Owner));
                        }

                        FireProxyLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingUpdate, binding.Owner, binding.RegistrarSIPEndPoint, binding.ProviderId.ToString()));
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationAgent AuthResponseReceived. " + excp.Message);
                RemoveCachedBinding(sipResponse.Header.CallId);
            }
        }

        private void OkResponseReceived(SIPTransaction sipTransaction, SIPEndPoint remoteEndPoint, SIPResponse sipResponse)
        {
            try
            {
                Guid callIdGuid = new Guid(sipResponse.Header.CallId);
                SIPProviderBinding binding = GetBinding(callIdGuid);
                RemoveCachedBinding(callIdGuid);

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

                    if (binding.BindingExpiry < REGISTER_MINIMUM_EXPIRY)
                    {
                        // Make sure we don't do a 3CX and send registration floods.
                        binding.BindingExpiry = REGISTER_MINIMUM_EXPIRY;
                    }

                    binding.NextRegistrationTime = DateTimeOffset.UtcNow.AddSeconds(binding.BindingExpiry - REGISTRATION_HEAD_TIME);
                    binding.ContactsList = sipResponse.Header.Contact;
                    binding.IsRegistered = true;
                    binding.LastRegisterTime = DateTimeOffset.UtcNow;
                    binding.RegistrationFailureMessage = null;
                    m_bindingPersistor.Update(binding);
                    UpdateSIPProviderOutboundProxy(binding, sipResponse.Header.ProxyReceivedOn);
                    FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.ContactRegistered, "Contact successfully registered for " + binding.Owner + " on " + binding.RegistrarServer.ToString() + ", expiry " + binding.BindingExpiry + "s.", binding.Owner));

                    if (binding.SendMWISubscribe)
                    {
                        try
                        {
                            var mwiURI = SIPURI.ParseSIPURIRelaxed(binding.ProviderAuthUsername + "@" + binding.RegistrarRealm);

                            FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.MWI, "Sending MWI subscription request for provider " + binding.ProviderName + " with URI " + mwiURI.ToParameterlessString() + ".", binding.Owner));

                            SIPNotifierClient<SIPEvent> mwiSubscriber = new SIPNotifierClient<SIPEvent>(FireProxyLogEvent, m_sipTransport, m_outboundProxy, SIPEventPackage.MessageSummary, mwiURI, binding.ProviderAuthUsername, binding.RegistrarRealm, binding.ProviderPassword, binding.BindingExpiry, null);
                            mwiSubscriber.SubscriptionFailed += (resourceURI, failureStatus, errorMessage) => { FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.MWI, "MWI failed for " + resourceURI.ToParameterlessString() + ", " + errorMessage, binding.Owner)); };
                            mwiSubscriber.SubscriptionSuccessful += (resourceURI) => { FireProxyLogEvent(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.RegisterAgent, SIPMonitorEventTypesEnum.MWI, "MWI subscription successful for " + resourceURI.ToParameterlessString() + ".", binding.Owner)); };
                            mwiSubscriber.Subscribe(mwiURI, binding.BindingExpiry, SIPEventPackage.MessageSummary, CallProperties.CreateNewCallId(), binding.BindingSIPURI);
                        }
                        catch (Exception excp)
                        {
                            logger.Error("Exception SIPRegistrationAgent MWI Subscription. " + excp.Message);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegistrationAgent OkResponseReceived. " + excp.Message);
                RemoveCachedBinding(sipResponse.Header.CallId);
            }
        }

        private SIPRequest GetRegistrationRequest(SIPProvider sipProvider, SIPProviderBinding binding, SIPEndPoint localSIPEndPoint, int expiry, SIPEndPoint registrarEndPoint)
        {
            try
            {
                if (!binding.BindingSIPURI.Parameters.Has(m_regAgentContactId))
                {
                    binding.BindingSIPURI.Parameters.Set(m_regAgentContactId, Crypto.GetRandomString(6));
                }

                string realm = binding.RegistrarRealm;

                SIPURI registerURI = SIPURI.ParseSIPURIRelaxed(realm);
                SIPURI regUserURI = SIPURI.ParseSIPURIRelaxed(sipProvider.ProviderUsername + "@" + realm);

                SIPFromHeader fromHeader = new SIPFromHeader(null, regUserURI, CallProperties.CreateNewTag());
                SIPToHeader toHeader = new SIPToHeader(null, regUserURI, null);
                SIPContactHeader contactHeader = new SIPContactHeader(null, binding.BindingSIPURI);
                //contactHeader.Expires = binding.BindingExpiry;
                string callId = binding.Id.ToString();
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

                if (sipProvider != null && !sipProvider.CustomHeaders.IsNullOrBlank())
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
                }

                registerRequest.Header = header;
                return registerRequest;
            }
            catch (Exception excp)
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
                authRequest.SetCredentials(binding.ProviderAuthUsername, binding.ProviderPassword, registerRequest.URI.ToString(), SIPMethodsEnum.REGISTER.ToString());

                SIPRequest regRequest = registerRequest.Copy();
                regRequest.LocalSIPEndPoint = registerRequest.LocalSIPEndPoint;
                regRequest.Header.Vias.TopViaHeader.Branch = CallProperties.CreateBranchId();
                regRequest.Header.From.FromTag = CallProperties.CreateNewTag();
                regRequest.Header.To.ToTag = null;
                regRequest.Header.CSeq = ++binding.CSeq;

                //if (SIPProviderMagicJack.IsMagicJackRequest(sipResponse))
                //{
                //    regRequest.Header.AuthenticationHeader = SIPProviderMagicJack.GetAuthenticationHeader(sipResponse);
                //}
                //else
                //{
                    regRequest.Header.AuthenticationHeader = new SIPAuthenticationHeader(authRequest);
                    regRequest.Header.AuthenticationHeader.SIPDigest.Response = authRequest.Digest;
                //}

                return regRequest;
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetAuthenticatedRegistrationRequest. " + excp.Message);
                throw excp;
            }
        }

        private void RemoveCachedBinding(string bindingId)
        {
            Guid bindingIDGUID = Guid.Empty;
            if (Guid.TryParse(bindingId, out bindingIDGUID))
            {
                RemoveCachedBinding(bindingIDGUID);
            }
        }

        private void RemoveCachedBinding(Guid bindingId)
        {
            if (m_inTransitBindings.ContainsKey(bindingId))
            {
                SIPProviderBinding removedBinding = null;
                m_inTransitBindings.TryRemove(bindingId, out removedBinding);
            }
        }

        private SIPProviderBinding GetBinding(Guid bindingId)
        {
            SIPProviderBinding binding = null;

            // If the binding is in the local cache use that.
            if (m_inTransitBindings.ContainsKey(bindingId))
            {
                binding = m_inTransitBindings[bindingId];
            }

            // If binding wasn't found in the cache try and load from persistence store.
            if (binding == null)
            {
                binding = m_bindingPersistor.Get(bindingId);
                //binding = m_bindingPersistor.GetFromDirectQuery(m_selectBinding, new SqlParameter("1", bindingId));
            }

            return binding;
        }

        private void DisableSIPProviderRegistration(Guid providerId, string disabledReason)
        {
            try
            {
                SIPProvider sipProvider = GetSIPProviderById_External(providerId);
                sipProvider.RegisterEnabled = false;
                sipProvider.RegisterDisabledReason = (!disabledReason.IsNullOrBlank()) ? disabledReason : "disabled with no reason given";
                UpdateSIPProvider_External(sipProvider);
            }
            catch (Exception excp)
            {
                logger.Error("Exception DisableSIPProviderRegistration. " + excp.Message);
            }
        }

        private void UpdateSIPProviderOutboundProxy(SIPProviderBinding binding, string outboundProxy)
        {
            try
            {
                if (binding.ProviderOutboundProxy != outboundProxy)
                {
                    UpdateSIPProviderProperty_External(binding.ProviderId, "ProviderOutboundProxy", outboundProxy);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception UpdateSIPProviderOutboundProxy. " + excp.Message);
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
