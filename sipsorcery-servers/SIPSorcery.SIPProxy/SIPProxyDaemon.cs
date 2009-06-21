// ============================================================================
// FileName: SIPProxyDaemon.cs
//
// Description:
// A daemon to configure and start a SIP Stateless Proxy Server Agent.
//
// Author(s):
// Aaron Clauson
//
// History:
// 22 Mar 2009	Aaron Clauson	Created.
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
using SIPSorcery.Net;
using SIPSorcery.Servers;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPProxy
{
    public class SIPProxyDaemon
    {
        private ILog logger = AppState.logger;

        private XmlNode m_sipProxySocketsNode = SIPProxyState.SIPProxySocketsNode;
        private int m_monitorPort = SIPProxyState.MonitorLoopbackPort;
        private string m_proxyRuntimeScriptPath = SIPProxyState.ProxyScriptPath;
        private IPEndPoint m_natKeepAliveSocket = SIPProxyState.NATKeepAliveSocket;

        private SIPTransport m_sipTransport;
        private StatelessProxyCore m_statelessProxyCore;
        private SIPMonitorEventWriter m_monitorEventWriter;
        private NATKeepAliveRelay m_natKeepAliveRelay;
        private STUNServer m_stunServer;

        public SIPProxyDaemon()
        {}

        public void Start()
        {
            try
            {
                logger.Debug("SIP Proxy daemon starting...");

                // Pre-flight checks.
                if (!File.Exists(m_proxyRuntimeScriptPath))
                {
                    throw new ApplicationException("The proxy cannot start without a runtime script. Path " + m_proxyRuntimeScriptPath + " could not be loaded.");
                }
                else if (m_sipProxySocketsNode == null || m_sipProxySocketsNode.ChildNodes.Count == 0)
                {
                    throw new ApplicationException("The proxy cannot start without at least one socket specified to listen on, please check config file.");
                }

                // Send events from this process to the monitoring socket.
                if (m_monitorPort != 0)
                {
                    // Events will be sent by the monitor channel to the loopback interface and this port.
                    m_monitorEventWriter = new SIPMonitorEventWriter(m_monitorPort);
                    logger.Debug(" SIP Proxy monitor sender initialised for 127.0.0.1:" + m_monitorPort + ".");
                }

                // Configure the SIP transport layer.
                m_sipTransport = new SIPTransport(SIPDNSManager.Resolve, null, false, false);
                List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_sipProxySocketsNode);
                m_sipTransport.AddSIPChannel(sipChannels);

                // Create the SIP stateless proxy core.
                m_statelessProxyCore = new StatelessProxyCore(FireSIPMonitorEvent, m_sipTransport, m_proxyRuntimeScriptPath);

                // Logging.
                m_sipTransport.SIPRequestInTraceEvent += LogSIPRequestIn;
                m_sipTransport.SIPRequestOutTraceEvent += LogSIPRequestOut;
                m_sipTransport.SIPResponseInTraceEvent += LogSIPResponseIn;
                m_sipTransport.SIPResponseOutTraceEvent += LogSIPResponseOut;

                if (m_natKeepAliveSocket != null) {
                    m_natKeepAliveRelay = new NATKeepAliveRelay(m_sipTransport, m_natKeepAliveSocket, FireSIPMonitorEvent);
                }

                logger.Debug("SIP Proxy daemon successfully started.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPProxyDaemon Start. " + excp.Message);
            }
        }

        private void StartSTUNServer(IPEndPoint primaryEndPoint, IPEndPoint secondaryEndPoint, SIPTransport sipTransport)
        {
            STUNListener secondarySTUNListener = new STUNListener(secondaryEndPoint);   // This end point is only for secondary STUN messages.
            m_stunServer = new STUNServer(primaryEndPoint, STUNSend, secondaryEndPoint, secondarySTUNListener.Send);
            sipTransport.STUNRequestReceived += m_stunServer.STUNPrimaryReceived;
            sipTransport.STUNRequestReceived += LogPrimarySTUNRequestReceived;
            secondarySTUNListener.MessageReceived += m_stunServer.STUNSecondaryReceived;
            secondarySTUNListener.MessageReceived += LogSecondarySTUNRequestReceived;

            logger.Debug("STUN server successfully initialised.");
        }

        private void StartNATKeepAliveRelay(SIPTransport sipTransport, IPEndPoint natKeepAliveSocket, SIPMonitorLogDelegate logDelegate)
        {
            if (natKeepAliveSocket != null)
            {
                m_natKeepAliveRelay = new NATKeepAliveRelay(sipTransport, natKeepAliveSocket, logDelegate);
                logger.Debug("NAT keep-alive relay created on " + natKeepAliveSocket + ".");
            }
            else
            {
                logger.Warn("The NATKeepAliveRelay cannot be started with an empty socket.");
            }
        }

        /// <summary>
        /// Allows the STUN server to utilise the default UDP SIP socket.
        /// </summary>
        /// <param name="dstEndPoint"></param>
        /// <param name="buffer"></param>
        private void STUNSend(IPEndPoint dstEndPoint, byte[] buffer)
        {
            //m_sipProxyTransport.SendRaw(m_sipProxyTransport.GetDefaultSIPEndPoint(SIPProtocolsEnum.udp), new SIPEndPoint(dstEndPoint), buffer);
        }

        public void Stop()
        {
            try
            {
                logger.Debug("SIP Proxy daemon stopping...");

                if (m_natKeepAliveRelay != null)
                {
                    logger.Debug("Stopping NAT Keep-Alive Relay.");
                    m_natKeepAliveRelay.Shutdown();
                }

                if (m_stunServer != null)
                {
                    logger.Debug("Stopping STUN server.");
                    m_stunServer.Stop();
                }

                logger.Debug("Shutting down SIP Transport.");
                m_sipTransport.Shutdown();

                logger.Debug("SIP Proxy daemon stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPProxyDaemon Stop. " + excp.Message);
            }
        }

        #region Logging.

        private void FireSIPMonitorEvent(SIPMonitorEvent sipMonitorEvent)
        {
            try
            {
                if (sipMonitorEvent != null)
                {
                    if (m_monitorEventWriter != null && sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction)
                    {
                        m_monitorEventWriter.Send(sipMonitorEvent);
                    }

                    if (sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.FullSIPTrace &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.Timing &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.UnrecognisedMessage &&
                        sipMonitorEvent.EventType != SIPMonitorEventTypesEnum.NATKeepAliveRelay &&
                        sipMonitorEvent.GetType() != typeof(SIPMonitorMachineEvent))
                    {
                        logger.Debug("pr: " + sipMonitorEvent.Message);
                    }
               }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FireSIPMonitorEvent. " + excp.Message);
            }
        }

        private void LogSIPRequestIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPRequest sipRequest)
        {
            string message = "Proxy Request Received: " + localSIPEndPoint.ToString() + "<-" + endPoint.ToString() + "\r\n" + sipRequest.ToString();
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.FullSIPTrace, message, sipRequest.Header.From.FromURI.User, localSIPEndPoint, endPoint));
        }

        private void LogSIPRequestOut(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPRequest sipRequest)
        {
            string message = "Proxy Request Sent: " + localSIPEndPoint.ToString() + "->" + endPoint.ToString() + "\r\n" + sipRequest.ToString();
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.FullSIPTrace, message, sipRequest.Header.From.FromURI.User, localSIPEndPoint, endPoint));
        }

        private void LogSIPResponseIn(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPResponse sipResponse)
        {
            string message = "Proxy Response Received: " + localSIPEndPoint.ToString() + "<-" + endPoint.ToString() + "\r\n" + sipResponse.ToString();
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.FullSIPTrace, message, sipResponse.Header.From.FromURI.User, localSIPEndPoint, endPoint));
        }

        private void LogSIPResponseOut(SIPEndPoint localSIPEndPoint, SIPEndPoint endPoint, SIPResponse sipResponse)
        {
            string message = "Proxy Response Sent: " + localSIPEndPoint.ToString() + "->" + endPoint.ToString() + "\r\n" + sipResponse.ToString();
            FireSIPMonitorEvent(new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.FullSIPTrace, message, sipResponse.Header.From.FromURI.User, localSIPEndPoint, endPoint));
        }

        private void LogPrimarySTUNRequestReceived(IPEndPoint localSIPEndPoint, IPEndPoint remoteEndPoint, byte[] buffer, int bufferLength)
        {
            SIPMonitorEvent stunEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.STUNPrimary, "Primary STUN request received from " + remoteEndPoint + ".", null);
            FireSIPMonitorEvent(stunEvent);
        }

        private void LogSecondarySTUNRequestReceived(IPEndPoint localSIPEndPoint, IPEndPoint remoteEndPoint, byte[] buffer, int bufferLength)
        {
            SIPMonitorEvent stunEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.StatelessProxy, SIPMonitorEventTypesEnum.STUNSecondary, "Secondary STUN request recevied from " + remoteEndPoint + ".", null);
            FireSIPMonitorEvent(stunEvent);
        }

        #endregion
    }
}
