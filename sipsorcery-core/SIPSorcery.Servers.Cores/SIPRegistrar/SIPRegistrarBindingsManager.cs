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

        private const string EXPIRE_BINDINGS_THREAD_NAME = "sipregistrar-expirebindings";
        private const int CHECK_REGEXPIRY_DURATION = 1000;            // Period at which to check for expired bindings.
        public const int NATKEEPALIVE_DEFAULTSEND_INTERVAL = 15;
        private const int MAX_USERAGENT_LENGTH = 128;
        public const int MINIMUM_EXPIRY_SECONDS = 60;
        private const int DEFAULT_BINDINGS_PER_USER = 1;              // The default maixmim number of bindings that will be allowed for each unique SIP account.
        private const int REMOVE_EXPIRED_BINDINGS_INTERVAL = 3000;    // The interval in seconds at which to check for and remove expired bindings.
        private const int BINDING_EXPIRY_GRACE_PERIOD = 10;

        private string m_sipRegisterRemoveAll = SIPConstants.SIP_REGISTER_REMOVEALL;
        private string m_sipExpiresParameterKey = SIPContactHeader.EXPIRES_PARAMETER_KEY;

        private static ILog logger = AppState.GetLogger("sipregistrar");

        private SIPMonitorLogDelegate SIPMonitorEventLog_External;
        private SendNATKeepAliveDelegate SendNATKeepAlive_External;

        private SIPAssetPersistor<SIPRegistrarBinding> m_bindingsPersistor;
        private SIPUserAgentConfigurationManager m_userAgentConfigs;
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
            ThreadPool.QueueUserWorkItem(ExpireBindings);
        }

        public void Stop() {
            m_stop = true;
        }

        private void ExpireBindings(object state) {
            try {
                Thread.CurrentThread.Name = EXPIRE_BINDINGS_THREAD_NAME;

                while (!m_stop) {
                    try {
                        DateTime expiryTime = DateTime.Now.AddSeconds(BINDING_EXPIRY_GRACE_PERIOD);
                        m_bindingsPersistor.Delete(b => b.ExpiryTime < expiryTime);
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
        public int UpdateBinding(
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
            
            int bindingExpiry = 0;
            int maxAllowedExpiry = m_userAgentConfigs.GetMaxAllowedExpiry(userAgent);
            responseMessage = null;
            string sipAccountAOR = sipAccount.SIPUsername + "@" + sipAccount.SIPDomain;

            try {
                userAgent = (userAgent != null && userAgent.Length > MAX_USERAGENT_LENGTH) ? userAgent.Substring(0, MAX_USERAGENT_LENGTH) : userAgent;

                if (bindingURI.Host == m_sipRegisterRemoveAll) {

                    #region Process remove all bindings.

                    if (expiresHeaderValue == 0) {
                        // Removing all bindings for user.
                        FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingRemoval, "Remove all bindings requested for " + sipAccountAOR + ".", sipAccount.SIPUsername));

                        List<SIPRegistrarBinding> bindings = m_bindingsPersistor.Get(b => b.SIPAccountId == sipAccount.Id, null, 0, Int32.MaxValue);
                        // Mark all the current bindings as expired.
                        for (int index = 0; index < bindings.Count; index++) {
                            bindings[index].RemovalReason = SIPBindingRemovalReason.ClientExpiredAll;
                            bindings[index].Expiry = 0;
                            m_bindingsPersistor.Update(bindings[index]);
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
                    string bindingString = bindingURI.ToString();
                    SIPRegistrarBinding binding = m_bindingsPersistor.Get(b => b.SIPAccountId == sipAccount.Id && b.ContactURI == bindingString);

                    if (binding != null) {
                        if (requestedExpiry <= 0) {
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingExpired, "Binding expired by client for " + sipAccountAOR + " from " + remoteSIPEndPoint.ToString() + ".", sipAccount.SIPUsername));
                            m_bindingsPersistor.Delete(binding);
                            bindingExpiry = 0;
                        }
                        else {
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Registrar, "Binding update request for " + sipAccountAOR + " from " + remoteSIPEndPoint.ToString() + ", expiry requested " + requestedExpiry + "s granted " + bindingExpiry + "s.", sipAccount.SIPUsername));
                            binding.RefreshBinding(bindingExpiry, remoteSIPEndPoint, proxySIPEndPoint, registrarSIPEndPoint);
                            m_bindingsPersistor.Update(binding);
                        }
                    }
                    else {
                        if (requestedExpiry > 0) {
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingInProgress, "New binding request for " + sipAccountAOR + " from " + remoteSIPEndPoint.ToString() + ", expiry requested " + requestedExpiry + "s granted " + bindingExpiry + "s.", sipAccount.SIPUsername));

                            if (m_bindingsPersistor.Count(b => b.SIPAccountId == sipAccount.Id) >= m_maxBindingsPerAccount) {
                                // Need to remove the oldest binding to stay within limit.
                                SIPRegistrarBinding oldestBinding = m_bindingsPersistor.Get(b => b.SIPAccountId == sipAccount.Id, null, 0, Int32.MaxValue).OrderBy(x => x.LastUpdate).Last();
                                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingInProgress, "Binding limit exceeded for " + sipAccountAOR + " from " + remoteSIPEndPoint.ToString() + " removing oldest binding to stay within limit of " + m_maxBindingsPerAccount + ".", sipAccount.SIPUsername));
                                m_bindingsPersistor.Delete(oldestBinding);
                            }

                            SIPRegistrarBinding newBinding = new SIPRegistrarBinding(sipAccount, bindingURI, callId, cseq, userAgent, remoteSIPEndPoint, proxySIPEndPoint, registrarSIPEndPoint, bindingExpiry);
                            m_bindingsPersistor.Add(newBinding);

                            FireSIPMonitorLogEvent(new SIPMonitorMachineEvent(SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate, sipAccount.SIPUsername, remoteSIPEndPoint, null));
                        }
                        else {
                            FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.BindingFailed, "New binding received for " + sipAccountAOR + " with expired contact," + bindingURI.ToString() + " no update.", sipAccount.SIPUsername));
                            bindingExpiry = 0;
                        }
                    }

                    responseStatus = SIPResponseStatusCodesEnum.Ok;
                }

                return bindingExpiry;
            }
            catch (Exception excp) {
                logger.Error("Exception UpdateBinding. " + excp.Message);
                FireSIPMonitorLogEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Registrar, SIPMonitorEventTypesEnum.Error, "Registrar error updating binding: " + excp.Message + " Binding not updated.", sipAccount.SIPUsername));
                responseStatus = SIPResponseStatusCodesEnum.InternalServerError;
                return 0;
            }
        }

        /// <summary>
        /// Gets a SIP contact header for this address-of-record based on the bindings list.
        /// </summary>
        /// <returns></returns>
        public List<SIPContactHeader> GetContactHeader(Guid sipAccountId) {

            string sipAccountIdString = sipAccountId.ToString();
            List<SIPRegistrarBinding> bindings = m_bindingsPersistor.Get(b => b.SIPAccountId == sipAccountIdString, "expiry", 0, Int32.MaxValue);

            if (bindings != null && bindings.Count > 0) {
                List<SIPContactHeader> contactHeaderList = new List<SIPContactHeader>();

                foreach (SIPRegistrarBinding binding in bindings) {
                    SIPContactHeader bindingContact = new SIPContactHeader(null, binding.MangledContactSIPURI);
                    bindingContact.Expires = Convert.ToInt32(binding.ExpiryTime.Subtract(DateTime.Now).TotalSeconds % Int32.MaxValue);
                    contactHeaderList.Add(bindingContact);
                }

                return contactHeaderList;
            }
            else {
                return null;
            }
        }

        private void SendNATKeepAlives() {
            /*try {
                lock (regRecord.Bindings) {
                    if (regRecord.Bindings.Count > 0) {
                        foreach (SIPRegistrarBinding binding in regRecord.GetBindings()) {
                            if (regRecord.NATSendKeepAlives && SendNATKeepAlive_External != null && binding.MangledContactURI != null) {
                                // If a user has been specified as requiring NAT keep-alives to be sent then they are identified here and a message is sent to
                                // the SIP proxy with the contact socket the keep-alive should be sent to.
                                if (binding.LastNATKeepAliveSendTime == null || DateTime.Now.Subtract(binding.LastNATKeepAliveSendTime.Value).TotalSeconds > NATKeepAliveSendInterval) {
                                    IPEndPoint sendFromEndPoint = (binding.ProxyEndPoint == null) ? binding.RegistrarEndPoint : binding.ProxyEndPoint;
                                    SendNATKeepAlive_External(new NATKeepAliveMessage(new SIPEndPoint(SIPProtocolsEnum.udp, IPSocket.ParseSocketString(binding.MangledContactURI.Host)), sendFromEndPoint));
                                    binding.LastNATKeepAliveSendTime = DateTime.Now;
                                    FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.NATKeepAlive, SIPMonitorEventTypesEnum.NATKeepAlive, "Requesting NAT keep-alive from proxy socket " + sendFromEndPoint + " to " + binding.ContactURI.Protocol + ":" + binding.MangledContactURI.ToString() + " for " + regRecord.AddressOfRecord.ToString() + ".", regRecord.AuthUser));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SendNATKeepAlives. " + excp.Message);
            }*/
        }

        private void FireSIPMonitorLogEvent(SIPMonitorEvent monitorEvent) {
            if (SIPMonitorEventLog_External != null) {
                SIPMonitorEventLog_External(monitorEvent);
            }
        }
    }
}
