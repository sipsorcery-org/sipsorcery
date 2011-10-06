// ============================================================================
// FileName: WatchTowerDaemon.cs
//
// Description:
// A daemon to configure and start the Call Dispatcher Server Agent.
//
// Author(s):
// Aaron Clauson
//
// History:
// 20 Nov 2009	Aaron Clauson	Created.
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using SIPSorcery.Servers;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.WatchTower
{
    public class WatchTowerDaemon
    {
        private ILog logger = WatchTowerState.logger;

        private static int m_monitorEventLoopbackPort = WatchTowerState.MonitorLoopbackPort;
        private static string m_proxyAppServerEndPointsPath = WatchTowerState.AppServerEndPointsPath;
        private XmlNode m_sipAppServerWorkersNode = WatchTowerState.SIPAppServerWorkersNode;
        private XmlNode m_transportNode = WatchTowerState.SIPSocketsNode;

        private SIPMonitorEventWriter m_monitorEventWriter;
        private SIPTransport m_sipTransport;
        private SIPAppServerManager m_appServerManager;

        public WatchTowerDaemon()
        { }

        public void Start()
        {
            try
            {
                logger.Debug("WatchTowerDaemon starting...");

                // Send events from this process to the monitoring socket.
                if (m_monitorEventLoopbackPort != 0)
                {
                    m_monitorEventWriter = new SIPMonitorEventWriter(m_monitorEventLoopbackPort);
                }

                List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_transportNode);
                if (sipChannels == null || sipChannels.Count == 0)
                {
                    throw new ApplicationException("No SIP channel was created for the WatchTower daemon.");
                }

                m_sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine());
                m_sipTransport.AddSIPChannel(sipChannels);

                if (m_sipAppServerWorkersNode != null)
                {
                    m_appServerManager = new SIPAppServerManager(
                        FireSIPMonitorEvent,
                        m_sipTransport,
                        m_sipAppServerWorkersNode,
                        m_proxyAppServerEndPointsPath);
                }

                //m_sipTransport.SIPTransportRequestReceived += GotRequest;
                //m_sipTransport.SIPTransportResponseReceived += GotResponse;
            }
            catch (Exception excp)
            {
                logger.Error("Exception WatchTowerDaemon Start. " + excp.Message);
                throw;
            }
        }

        public void Stop()
        {
            try
            {
                m_appServerManager.Stop();
            }
            catch (Exception excp)
            {
                logger.Error("Exception WatchTowerDaemon Stop. " + excp.Message);
            }
        }

        private void FireSIPMonitorEvent(SIPMonitorEvent sipMonitorEvent)
        {
            try
            {
                if (sipMonitorEvent != null && m_monitorEventWriter != null)
                {
                    if (!(sipMonitorEvent is SIPMonitorConsoleEvent &&
                        ((SIPMonitorConsoleEvent)sipMonitorEvent).EventType == SIPMonitorEventTypesEnum.SIPTransaction))
                    {
                        m_monitorEventWriter.Send(sipMonitorEvent);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPMonitorEvent. " + excp.Message);
            }
        }
    }
}
