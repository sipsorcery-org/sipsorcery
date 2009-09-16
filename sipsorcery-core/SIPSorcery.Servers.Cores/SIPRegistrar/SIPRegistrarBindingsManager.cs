// ============================================================================
// FileName: SIPRegistrarBindingsManager.cs
//
// Description:
// Manages the storing, updating and retrieval of bindings for a SIP Registrar.
//
// Author(s):
// Aaron Clauson
//
// History:
// 21 May 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2008 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers {

    public class SIPRegistrarBindingsManager {

        private struct NATKeepAliveJob {

            public string BindingId;
            public SIPEndPoint ProxyEndPoint;   // The proxy socket the NAT keep-alive packet should be sent from;
            public SIPEndPoint RemoteEndPoint;  // Where the NAT keep-alive packet should be sent by the proxy.
            public DateTime? NextSendTime;      
            public DateTime EndTime;
            public string Owner;
            public bool Cancel;                 // If set to true indicates the NAT keep alive job should be removed.

            public NATKeepAliveJob(string bindingId, SIPEndPoint proxyEndPoint, SIPEndPoint remoteEndPoint, DateTime endTime, string owner) {
                BindingId = bindingId;
                ProxyEndPoint = proxyEndPoint;
                RemoteEndPoint = remoteEndPoint;
                NextSendTime = null;
                EndTime = endTime;
                Owner = owner;
                Cancel = false;
            }

            public void CancelJob() {
                Cancel = true;
            }

            public void Update(SIPEndPoint proxyEndPoint, SIPEndPoint remoteEndPoint, DateTime endTime) {
                ProxyEndPoint = proxyEndPoint;
                RemoteEndPoint = remoteEndPoint;
                NextSendTime = null;
                EndTime = endTime;
            }
        }

        private const string EXPIRE_BINDINGS_THREAD_NAME = "sipregistrar-expirebindings";
        private const string SEND_KEEPALIVES_THREAD_NAME = "sipregistrar-natkeepalives";
        private const int CHECK_REGEXPIRY_DURATION = 1000;            // Period at which to check for expired bindings.
        public const int NATKEEPALIVE_DEFAULTSEND_INTERVAL = 15;
        private const int MAX_USERAGENT_LENGTH = 128;
        public const int MINIMUM_EXPIRY_SECONDS = 60;
        private const int DEFAULT_BINDINGS_PER_USER = 1;              // The default maixmim number of bindings that will be allowed for each unique SIP account.
        private const int REMOVE_EXPIRED_BINDINGS_INTERVAL = 3000;    // The interval in seconds at which to check for and remove expired bindings.
        private const int SEND_NATKEEPALIVES_INTERVAL = 1000;
        private const int BINDING_EXPIRY_GRACE_PERIOD = 10;

        private string m_sipRegisterRemoveAll = SIPConstants.SIP_REGISTER_REMOVEALL;
        private string m_sipExpiresParameterKey = SIPContactHeader.EXPIRES_PARAMETER_KEY;
        private string m_selectBindingsSQLQuery = SIPRegistrarBinding.SelectBindingsQuery;
        private string m_selectExpiredBindingsSQLQuery = SIPRegistrarBinding.SelectExpiredBindingsQuery;

        private static ILog logger = AppState.GetLogger("sipregistrar");

        private SIPMonitorLogDelegate SIPMonitorEventLog_External;
        private SendNATKeepAliveDelegate SendNATKeepAlive_External;

        private SIPAssetPersistor<SIPRegistrarBinding> m_bindingsPersistor;
        private SIPUserAgentConfigurationManager m_userAgentConfigs;
        private Dictionary<string, NATKeepAliveJob> m_natKeepAliveJobs = new Dictionary<string, NATKeepAliveJob>();
        private int m_maxBindingsPerAccount;
        private bool m_stop;
           
        public SIPRegistrarBindingsManager(
            SIPMonitorLogDelegate sipMonitorEventLog,
            SIPAssetPersistor<SIPRegistrarBinding> bindingsPersistor,
            SendNATKeepAliveDelegate sendNATKeepAlive,
            int maxBindingsPerAccount,
            SIPUserAgentConfigurationManager userAgentConfigs)
        {
            SIPMonitorEventLog_External = sipMonitorEventLog;
            m_bindingsPersistor = bindingsPersistor;
            SendNATKeepAlive_External = sendNATKeepAlive;
            m_maxBindingsPerAccount = (maxBindingsPerAccount != 0) ? maxBindingsPerAccount : DEFAULT_BINDINGS_PER_USER;
            m_userAgentConfigs = userAgentConfigs;
        }

        public void Start() {
            ThreadPool.QueueUserWorkItem(delegate { ExpireBindings(); });
            ThreadPool.QueueUserWorkItem(delegate { SendNATKeepAlives(); });
        }

        public void Stop() {
            m_stop = true;
        }

        private void ExpireBindings() {
            try {
                Thread.CurrentThread.Name = EXPIRE_BINDINGS_THREAD_NAME;

                DateTime expiryTime = DateTime.Now.ToUniversalTime().AddSeconds(BINDING_EXPIRY_GRACE_PERIOD * -1);

                while (!m_stop) {
                    try {
                        expiryTime = DateTime.Now.ToUniversalTime().AddSeconds(BINDING_EXPIRY_GRACE_PERIOD * -1);
                        //List<SIPRegistrarBinding> expiredBindings = m_bindingsPersistor.Get(b => b.ExpiryTimeUTC < expiryTime, null, 0, Int32.MaxValue);
                        List<SIPRegistrarBinding> expiredBindings = m_bindingsPersistor.GetListFromDirectQuery(m_selectExpiredBindingsSQLQuery, new SqlParameter("1", expiryTime));
                        if (expiredBindings != null && expiredBindings.Count > 0) {
                            foreach (SIPRegistrarBinding binding in expiredBindings) {
                                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingExpired, "Deleting expired binding for " + binding.SIPAccountName + " and " + binding.MangledContactURI + ", last register " + binding.LastUpdateUTC.ToString("HH:mm:ss") + ".", binding.Owner));
                                m_bindingsPersistor.Delete(binding);
                            }
                        }
                    }
                    catch (Exception expireExcp) {
                        logger.Error("Exception ExpireBindings Delete. " + expireExcp.Message);
                    }

                    Thread.Sleep(REMOVE_EXPIRED_BINDINGS_INTERVAL);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception ExpireBindings. " + excp.Message);
            }
            finally {
                logger.Warn("Thread " + EXPIRE_BINDINGS_THREAD_NAME + " stopped!");
            }
        }

        /// <summary>
        /// Updates the bindings list for a registrar's address-of-records.
        /// </summary>
        /// <param name="proxyEndPoint">If the request arrived at this registrar via a proxy then this will contain the end point of the proxy.</param>
        /// <param name="uacRecvdEndPoint">The public end point the UAC REGISTER request was deemded to have originated from.</param>
        /// <param name="registrarEndPoint">The registrar end point the registration request was received on.</param>
        /// <param name="maxAllowedExpiry">The maximum allowed expiry that can be granted to this binding request.</param>
        /// <returns>If the binding update was successful the expiry time for it is returned otherwise 0.</returns>
        public List<SIPRegistrarBinding> UpdateBinding(
            SIPAccount sipAccount,
            SIPEndPoint proxySIPEndPoint,
            SIPEndPoint remoteSIPEndPoint,
            SIPEndPoint registrarSIPEndPoint,
            SIPURI bindingURI,
            string callId,
            int cseq,
            int contactHeaderExpiresValue,
            int expiresHeaderValue,
            string userAgent,
            out SIPResponseStatusCodesEnum responseStatus,
            out string responseMessage) {

            logger.Debug("UpdateBinding " + bindingURI.ToString() + ".");
            
            int bindingExpiry = 0;
            int maxAllowedExpiry = m_userAgentConfigs.GetMaxAllowedExpiry(userAgent);
            responseMessage = null;
            string sipAccountAOR = sipAccount.SIPUsername + "@" + sipAccount.SIPDomain;

            try {
                userAgent = (userAgent != null && userAgent.Length > MAX_USERAGENT_LENGTH) ? userAgent.Substring(0, MAX_USERAGENT_LENGTH) : userAgent;

                //List<SIPRegistrarBinding> bindings = m_bindingsPersistor.Get(b => b.SIPAccountId == sipAccount.Id, null, 0, Int32.MaxValue);
                List<SIPRegistrarBinding> bindings = m_bindingsPersistor.GetListFromDirectQuery(m_selectBindingsSQLQuery, new SqlParameter("1", sipAccount.Id));

                if (bindingURI.Host == m_sipRegisterRemoveAll) {

                    #region Process remove all bindings.

                    if (expiresHeaderValue == 0) {
                        // Removing all bindings for user.
                        FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingRemoval, "Remove all bindings requested for " + sipAccountAOR + ".", sipAccount.SIPUsername));
                       
                        // Mark all the current bindings as expired.
                        if (bindings != null && bindings.Count > 0) {
                            for (int index = 0; index < bindings.Count; index++) {
                                bindings[index].RemovalReason = SIPBindingRemovalReason.ClientExpiredAll;
                                bindings[index].Expiry = 0;
                                m_bindingsPersistor.Update(bindings[index]);

                                // Remove the NAT keep-alive job if present.
                                if (m_natKeepAliveJobs.ContainsKey(bindings[index].Id)) {
                                    m_natKeepAliveJobs[bindings[index].Id].CancelJob();
                                }
                            }
                        }

                        responseStatus = SIPResponseStatusCodesEnum.Ok;
                    }
                    else {
                        // Remove all header cannot be present with other headers and must have an Expiry equal to 0.
                        responseStatus = SIPResponseStatusCodesEnum.BadRequest;
                    }

                    #endregion
                }
                else {
                    int requestedExpiry = (contactHeaderExpiresValue != -1) ? contactHeaderExpiresValue : expiresHeaderValue;
                    requestedExpiry = (requestedExpiry == -1) ? maxAllowedExpiry : requestedExpiry;   // This will happen if the Expires header and the Expiry on the Contact are both missing.
                    bindingExpiry = (requestedExpiry > maxAllowedExpiry) ? maxAllowedExpiry : requestedExpiry;
                    bindingExpiry = (bindingExpiry < MINIMUM_EXPIRY_SECONDS) ? MINIMUM_EXPIRY_SECONDS : bindingExpiry;

                    bindingURI.Parameters.Remove(m_sipExpiresParameterKey);
                    //string bindingString = bindingURI.ToString();
                    //m_bindingsPersistor.Get(b => b.SIPAccountId == sipAccount.Id && b.ContactURI == bindingString);
                    SIPRegistrarBinding binding = GetBindingForContactURI(bindings, bindingURI.ToString());

                    if (binding != null) {
                        if (requestedExpiry <= 0) {
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingExpired, "Binding expired by client for " + sipAccountAOR + " from " + remoteSIPEndPoint.ToString() + ".", sipAccount.SIPUsername));
                            bindings.Remove(binding);
                            m_bindingsPersistor.Delete(binding);
                            bindingExpiry = 0;

                            // Remove the NAT keep-alive job if present.
                            if(m_natKeepAliveJobs.ContainsKey(binding.Id)) {
                                m_natKeepAliveJobs[binding.Id].CancelJob();
                            }
                        }
                        else {
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Registrar, "Binding update request for " + sipAccountAOR + " from " + remoteSIPEndPoint.ToString() + ", expiry requested " + requestedExpiry + "s granted " + bindingExpiry + "s.", sipAccount.Owner));
                            binding.RefreshBinding(bindingExpiry, remoteSIPEndPoint, proxySIPEndPoint, registrarSIPEndPoint);

                            DateTime startUpdateTime = DateTime.Now;
                            m_bindingsPersistor.Update(binding);
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegistrarTiming, "Binding database update time for " + sipAccountAOR + " took " + DateTime.Now.Subtract(startUpdateTime).TotalMilliseconds.ToString("0") + "ms.", null));

                            // Add a NAT keep-alive job if required.
                            if (sipAccount.SendNATKeepAlives && proxySIPEndPoint != null) {
                                if (m_natKeepAliveJobs.ContainsKey(binding.Id)) {
                                    m_natKeepAliveJobs[binding.Id].Update(proxySIPEndPoint, remoteSIPEndPoint, DateTime.Now.AddSeconds(bindingExpiry));
                                }
                                else {
                                    m_natKeepAliveJobs.Add(binding.Id, new NATKeepAliveJob(binding.Id, proxySIPEndPoint, remoteSIPEndPoint, DateTime.Now.AddSeconds(bindingExpiry), sipAccount.Owner));
                                }
                            }
                        }
                    }
                    else {
                        if (requestedExpiry > 0) {
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingInProgress, "New binding request for " + sipAccountAOR + " from " + remoteSIPEndPoint.ToString() + ", expiry requested " + requestedExpiry + "s granted " + bindingExpiry + "s.", sipAccount.Owner));

                            if (bindings.Count >= m_maxBindingsPerAccount) {
                                // Need to remove the oldest binding to stay within limit.
                                //SIPRegistrarBinding oldestBinding = m_bindingsPersistor.Get(b => b.SIPAccountId == sipAccount.Id, null, 0, Int32.MaxValue).OrderBy(x => x.LastUpdateUTC).Last();
                                SIPRegistrarBinding oldestBinding = bindings.OrderBy(x => x.LastUpdateUTC).Last();
                                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingInProgress, "Binding limit exceeded for " + sipAccountAOR + " from " + remoteSIPEndPoint.ToString() + " removing oldest binding to stay within limit of " + m_maxBindingsPerAccount + ".", sipAccount.Owner));
                                m_bindingsPersistor.Delete(oldestBinding);

                                if (m_natKeepAliveJobs.ContainsKey(binding.Id)) {
                                    m_natKeepAliveJobs[binding.Id].CancelJob();
                                }
                            }

                            SIPRegistrarBinding newBinding = new SIPRegistrarBinding(sipAccount, bindingURI, callId, cseq, userAgent, remoteSIPEndPoint, proxySIPEndPoint, registrarSIPEndPoint, bindingExpiry);
                            DateTime startAddTime = DateTime.Now;
                            bindings.Add(newBinding);
                            m_bindingsPersistor.Add(newBinding);
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.RegistrarTiming, "Binding database add time for " + sipAccountAOR + " took " + DateTime.Now.Subtract(startAddTime).TotalMilliseconds.ToString("0") + "ms.", null));

                            // Add a NAT keep-alive job if required.
                            try {
                                if (sipAccount.SendNATKeepAlives && proxySIPEndPoint != null) {
                                    m_natKeepAliveJobs.Add(newBinding.Id, new NATKeepAliveJob(newBinding.Id, proxySIPEndPoint, remoteSIPEndPoint, DateTime.Now.AddSeconds(bindingExpiry), sipAccount.Owner));
                                }
                            }
                            catch (Exception addNATExcp) {
                                logger.Error("Exception UpdateBinding Add NAT Job. " + addNATExcp.Message);
                            }

                            FireSIPMonitorLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate, sipAccount.SIPUsername, remoteSIPEndPoint, null));
                        }
                        else {
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingFailed, "New binding received for " + sipAccountAOR + " with expired contact," + bindingURI.ToString() + " no update.", sipAccount.Owner));
                            bindingExpiry = 0;
                        }
                    }

                    responseStatus = SIPResponseStatusCodesEnum.Ok;
                }

                return bindings;
            }
            catch (Exception excp) {
                logger.Error("Exception UpdateBinding. " + excp.Message);
                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, "Registrar error updating binding: " + excp.Message + " Binding not updated.", sipAccount.SIPUsername));
                responseStatus = SIPResponseStatusCodesEnum.InternalServerError;
                return null;
            }
        }

        public List<SIPRegistrarBinding> GetBindings(Guid sipAccountId) {
            return m_bindingsPersistor.Get(b => b.SIPAccountId == sipAccountId.ToString(), null, 0, Int32.MaxValue);
        }

        private SIPRegistrarBinding GetBindingForContactURI(List<SIPRegistrarBinding> bindings, string bindingURI){
            if (bindings == null || bindings.Count == 0) {
                return null;
            }
            else {
                foreach (SIPRegistrarBinding binding in bindings) {
                    if (binding.ContactURI == bindingURI) {
                        logger.Debug(binding.ContactURI + " matched " + bindingURI + ".");
                        return binding;
                    }
                }
                logger.Debug("No existing binding matched for " + bindingURI + ".");
                return null;
            }
        }

        private void SendNATKeepAlives() {
            try {
                Thread.CurrentThread.Name = SEND_KEEPALIVES_THREAD_NAME;

                while (!m_stop) {
                    try {
                        List<NATKeepAliveJob> m_jobsList = m_natKeepAliveJobs.Values.ToList();
                        List<string> jobsToRemove = new List<string>();

                        DateTime natKeepAliveStart = DateTime.Now;

                        // Send NAT keep-alives.
                        for (int index = 0; index < m_jobsList.Count; index++) {
                            NATKeepAliveJob job = m_jobsList[index];
                            if (job.EndTime < DateTime.Now || job.Cancel) {
                                if (!jobsToRemove.Contains(job.BindingId.ToString())) {
                                    jobsToRemove.Add(job.BindingId.ToString());
                                }
                            }
                            else if (job.NextSendTime == null || job.NextSendTime < DateTime.Now) {
                                SendNATKeepAlive_External(new NATKeepAliveMessage(job.ProxyEndPoint, job.RemoteEndPoint.SocketEndPoint));
                                job.NextSendTime = DateTime.Now.AddSeconds(NATKEEPALIVE_DEFAULTSEND_INTERVAL);
                                logger.Debug("Requesting NAT keep-alive from proxy socket " + job.ProxyEndPoint.ToString() + " to " + job.RemoteEndPoint + ", owner=" + job.Owner + ".");
                                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.NATKeepAlive, SIPMonitorEventTypesEnum.NATKeepAlive, "Requesting NAT keep-alive from proxy socket " + job.ProxyEndPoint.ToString() + " to " + job.RemoteEndPoint + ".", job.Owner));
                                if (m_natKeepAliveJobs.ContainsKey(job.BindingId)) {
                                    m_natKeepAliveJobs[job.BindingId] = job;
                                }
                            }
                        }

                        // Remove any flagged jobs.
                        foreach (string removeJob in jobsToRemove) {
                            m_natKeepAliveJobs.Remove(removeJob);
                        }

                        //FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Timing, "NATKeepAlive run took " + DateTime.Now.Subtract(natKeepAliveStart).TotalMilliseconds.ToString("0") + "ms.", null));
                    }
                    catch (Exception sendExcp) {
                        logger.Error("Exception SendNATKeepAlives Send. " + sendExcp.Message);
                    }

                    Thread.Sleep(SEND_NATKEEPALIVES_INTERVAL);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SendNATKeepAlives. " + excp.Message);
            }
        }

        private void FireSIPMonitorLogEvent(SIPMonitorEvent monitorEvent) {
            if (SIPMonitorEventLog_External != null) {
                SIPMonitorEventLog_External(monitorEvent);
            }
        }
    }
}
