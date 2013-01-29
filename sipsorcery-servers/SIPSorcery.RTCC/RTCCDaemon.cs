using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using log4net;
using SIPSorcery.Servers;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.RTCC
{
    public class RTCCDaemon
    {
        private ILog logger = AppState.logger;

        private XmlNode m_rtccSIPSocketsNode = RTCCState.RTCCSIPSocketsNode;
        private int m_monitorLoopbackPort = RTCCState.MonitorLoopbackPort;
        private SIPEndPoint m_outboundProxy = RTCCState.OutboundProxy;

        private SIPTransport m_sipTransport;
        private SIPMonitorEventWriter m_monitorEventWriter;
        private SIPDialogueManager m_sipDialogueManager;
        private SIPSorceryPersistor m_sipSorceryPersistor;
        private RTCCCore m_rtccCore;

        public RTCCDaemon(SIPSorceryPersistor sipSorceryPersistor)
        {
            m_sipSorceryPersistor = sipSorceryPersistor;
        }

        public void Start()
        {
            try
            {
                logger.Debug("RTCC Daemon starting...");

                // Pre-flight checks.
                if (m_rtccSIPSocketsNode == null || m_rtccSIPSocketsNode.ChildNodes.Count == 0)
                {
                    throw new ApplicationException("The RTCC server cannot start without a SIP socket, please check config file.");
                }

                // Send events from this process to the monitoring socket.
                if (m_monitorLoopbackPort != 0)
                {
                    // Events will be sent by the monitor channel to the loopback interface and this port.
                    m_monitorEventWriter = new SIPMonitorEventWriter(m_monitorLoopbackPort);
                    logger.Debug("Monitor channel initialised for 127.0.0.1:" + m_monitorLoopbackPort + ".");
                }

                // Configure the SIP transport layer.
                m_sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine(), false);
                List<SIPChannel> sipChannels = SIPTransportConfig.ParseSIPChannelsNode(m_rtccSIPSocketsNode);
                m_sipTransport.AddSIPChannel(sipChannels);

                if (m_sipSorceryPersistor != null && m_sipSorceryPersistor.SIPCDRPersistor != null)
                {
                    SIPCDR.CDRCreated += m_sipSorceryPersistor.QueueCDR;
                    SIPCDR.CDRAnswered += m_sipSorceryPersistor.QueueCDR;
                    SIPCDR.CDRHungup += m_sipSorceryPersistor.QueueCDR;
                }

                m_sipDialogueManager = new SIPDialogueManager(
                     m_sipTransport,
                     m_outboundProxy,
                     FireSIPMonitorEvent,
                     m_sipSorceryPersistor.SIPDialoguePersistor,
                     m_sipSorceryPersistor.SIPCDRPersistor,
                     SIPRequestAuthenticator.AuthenticateSIPRequest,
                     m_sipSorceryPersistor.SIPAccountsPersistor.Get,
                     m_sipSorceryPersistor.SIPDomainManager.GetDomain);

                m_rtccCore = new RTCCCore(
                    FireSIPMonitorEvent,
                    m_sipDialogueManager,
                    m_sipSorceryPersistor.SIPDialoguePersistor);
                m_rtccCore.Start();

                logger.Debug("RTCC Daemon successfully started.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTCCDaemon Start. " + excp.Message);
            }
        }

        public void Stop()
        {
            try
            {
                logger.Debug("RTCC daemon stopping...");

                logger.Debug("Shutting down SIP Transport.");
                m_sipTransport.Shutdown();

                if (m_rtccCore != null)
                {
                    m_rtccCore.Stop();
                }

                logger.Debug("RTCC daemon stopped.");
            }
            catch (Exception excp)
            {
                logger.Error("Exception RTCCDaemon Stop. " + excp.Message);
            }
        }

        private void FireSIPMonitorEvent(SIPMonitorEvent sipMonitorEvent)
        {
            try
            {
                if (sipMonitorEvent != null)
                {
                    if (sipMonitorEvent is SIPMonitorConsoleEvent)
                    {
                        SIPMonitorConsoleEvent consoleEvent = sipMonitorEvent as SIPMonitorConsoleEvent;
                        logger.Debug("rtcc: " + sipMonitorEvent.Message);
                    }

                    if (m_monitorEventWriter != null)
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
