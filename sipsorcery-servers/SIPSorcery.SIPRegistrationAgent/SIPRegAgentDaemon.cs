// ============================================================================
// FileName: SIPRegAgentDaemon.cs
//
// Description:
// A daemon to configure and start a SIP Registration Agent.
//
// Author(s):
// Aaron Clauson
//
// History:
// 29 Mar 2009	Aaron Clauson	Created.
//
// License: 
// This software is licensed under the BSD License http://www.opensource.org/licenses/bsd-license.php
//
// Copyright (c) 2009 Aaron Clauson (aaronc@blueface.ie), Blue Face Ltd, Dublin, Ireland (www.blueface.ie)
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Servers;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPRegistrationAgent
{
    public class SIPRegAgentDaemon
    {
        private ILog logger = AppState.logger;

        private XmlNode m_sipRegAgentSocketsNode = SIPRegAgentState.SIPRegAgentSocketsNode;
        private int m_monitorLoopbackPort = SIPRegAgentState.MonitorLoopbackPort;
        private SIPEndPoint m_outboundProxy = SIPRegAgentState.OutboundProxy;
 
        private SIPTransport m_sipTransport;
        private SIPMonitorEventWriter m_monitorEventWriter;
        private SIPRegistrationAgentCore m_sipRegAgentCore;
        private SIPAssetPersistor<SIPProviderBinding> m_bindingPersistor;

        private SIPAssetGetByIdDelegate<SIPProvider> GetSIPProviderById_External;
        private SIPAssetUpdateDelegate<SIPProvider> UpdateSIPProvider_External;

        public SIPRegAgentDaemon(
            SIPAssetGetByIdDelegate<SIPProvider> getSIPProviderById,
            SIPAssetUpdateDelegate<SIPProvider> updateSIPProvider,
            SIPAssetPersistor<SIPProviderBinding> bindingPersistor) {

            GetSIPProviderById_External = getSIPProviderById;
            UpdateSIPProvider_External = updateSIPProvider;
            m_bindingPersistor = bindingPersistor;
        }

        public void Start()
        {
            try
            {
                logger.Debug("SIP Registration Agent daemon starting...");

                // Pre-flight checks.
                if (m_sipRegAgentSocketsNode == null || m_sipRegAgentSocketsNode.ChildNodes.Count == 0)
                {
                    throw new ApplicationException("The SIP Registration Agent cannot start without at least one socket specified to listen on, please check config file.");
                }

                // Send events from this process to the monitoring socket.
                if (m_monitorLoopbackPort != 0)
                {
                    // Events will be sent by the monitor channel to the loopback interface and this port.
                    m_monitorEventWriter = new SIPMonitorEventWriter(m_monitorLoopbackPort);
                    logger.Debug("Monitor channel initialised for 127.0.0.1:" + m_monitorLoopbackPort + ".");
                }

                // Configure the SIP transport layer.
                m_sipTransport = new SIPTransport(SIPDNSManager.Resolve, new SIPTransactionEngine(), false, false);
                List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_sipRegAgentSocketsNode);
                m_sipTransport.AddSIPChannel(sipChannels);

                m_sipRegAgentCore = new SIPRegistrationAgentCore(
                    FireSIPMonitorEvent,
                    m_sipTransport,
                    m_outboundProxy,
                    GetSIPProviderById_External,
                    UpdateSIPProvider_External,
                    m_bindingPersistor);
                m_sipRegAgentCore.Start();

                logger.Debug("SIP Registration Agent successfully started.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegAgentDaemon Start. " + excp.Message);
            }
        }

        public void SIPProviderAdded(SIPProvider sipProvider) {
            try {
                logger.Debug("SIPRegAgentDaemon SIPProviderAdded for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

                if (sipProvider.RegisterEnabled) {
                    SIPProviderBinding binding = new SIPProviderBinding(sipProvider);
                    m_bindingPersistor.Add(binding);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPRegAgentDaemon SIPProviderAdded. " + excp.Message);
            }
        }

        public void SIPProviderUpdated(SIPProvider sipProvider) {
            try {
                logger.Debug("SIPRegAgentDaemon SIPProviderUpdated for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

                SIPProviderBinding existingBinding = m_bindingPersistor.Get(b => b.ProviderId == sipProvider.Id);

                if (sipProvider.RegisterEnabled) {
                    if (existingBinding == null) {
                        SIPProviderBinding newBinding = new SIPProviderBinding(sipProvider);
                        m_bindingPersistor.Add(newBinding);
                    }
                    else {
                        existingBinding.SetProviderFields(sipProvider);
                        existingBinding.NextRegistrationTime = DateTime.Now;
                        m_bindingPersistor.Update(existingBinding);
                    }
                }
                else {
                    if (existingBinding != null) {
                        if (existingBinding.IsRegistered) {
                            // Let the registration agent know the existing binding should be expired.
                            existingBinding.BindingExpiry = 0;
                            existingBinding.NextRegistrationTime = DateTime.Now;
                            m_bindingPersistor.Update(existingBinding);
                        }
                        else {
                            m_bindingPersistor.Delete(existingBinding);
                        }
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPRegAgentDaemon SIPProviderUpdated. " + excp.Message);
            }
        }

        public void SIPProviderDeleted(SIPProvider sipProvider) {
            try {
                logger.Debug("SIPRegAgentDaemon SIPProviderDeleted for " + sipProvider.Owner + " and " + sipProvider.ProviderName + ".");

                SIPProviderBinding existingBinding = m_bindingPersistor.Get(b => b.ProviderId == sipProvider.Id);
                if (existingBinding != null) {
                    if (existingBinding.IsRegistered) {
                        // Let the registration agent know the existing binding should be expired.
                        existingBinding.BindingExpiry = 0;
                        existingBinding.NextRegistrationTime = DateTime.Now;
                        m_bindingPersistor.Update(existingBinding);
                    }
                    else {
                        m_bindingPersistor.Delete(existingBinding);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPRegAgentDaemon SIPProviderDeleted. " + excp.Message);
            }
        }

        public void Stop()
        {
            try
            {
                logger.Debug("SIP Registration Agent daemon stopping...");

                m_sipRegAgentCore.Stop();

                logger.Debug("Shutting down SIP Transport.");
                m_sipTransport.Shutdown();

                logger.Debug("SIP Registration Agent daemon stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegAgentDaemon Stop. " + excp.Message);
            }
        }

        private void FireSIPMonitorEvent(SIPMonitorEvent sipMonitorEvent) {
            try {
                if (sipMonitorEvent != null) {
                    if (sipMonitorEvent.GetType() != typeof(SIPMonitorMachineEvent))
                    {
                        logger.Debug("ra: " + sipMonitorEvent.Message);
                    }
                    
                    if (m_monitorEventWriter != null) {
                        m_monitorEventWriter.Send(sipMonitorEvent);
                    }
                }
            }
            catch (Exception excp) {
                logger.Error("Exception FireSIPMonitorEvent. " + excp.Message);
            }
        }  
    }
}
