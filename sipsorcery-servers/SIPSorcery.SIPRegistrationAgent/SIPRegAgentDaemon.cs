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
        private SIPAssetPersistor<SIPProvider> m_providerPersistor;

        public SIPRegAgentDaemon(
            SIPAssetPersistor<SIPProvider> providerPersistor,
            SIPAssetPersistor<SIPProviderBinding> bindingPersistor) {

            m_providerPersistor = providerPersistor;
            m_bindingPersistor = bindingPersistor;

            // The Registration Agent wants to know about any changes to SIP Provider entries in order to update any SIP 
            // Provider bindings it is maintaining or needs to add or remove.
            SIPProviderBindingSynchroniser sipProviderBindingSynchroniser = new SIPProviderBindingSynchroniser(m_bindingPersistor);
            m_providerPersistor.Added += sipProviderBindingSynchroniser.SIPProviderAdded;
            m_providerPersistor.Updated += sipProviderBindingSynchroniser.SIPProviderUpdated;
            m_providerPersistor.Deleted += sipProviderBindingSynchroniser.SIPProviderDeleted;
        }

        public void Start()
        {
            try
            {
                logger.Debug("SIP Registration Agent daemon starting...");

                if (m_sipRegAgentSocketsNode == null) {
                    throw new ApplicationException("The SIP Registration Agent could not be started, no SIP transport sockets node could be found.");
                }

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
                m_sipTransport = new SIPTransport(SIPDNSManager.Resolve, new SIPTransactionEngine(), false);
                List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_sipRegAgentSocketsNode);
                m_sipTransport.AddSIPChannel(sipChannels);

                m_sipRegAgentCore = new SIPRegistrationAgentCore(
                    FireSIPMonitorEvent,
                    m_sipTransport,
                    m_outboundProxy,
                    m_providerPersistor.Get,
                    m_providerPersistor.Update,
                    m_bindingPersistor);
                m_sipRegAgentCore.Start();

                logger.Debug("SIP Registration Agent successfully started.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPRegAgentDaemon Start. " + excp.Message);
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
