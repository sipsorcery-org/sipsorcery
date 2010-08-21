using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{

    public class AppServerDispatcher : SIPDispatcherJob
    {

        public class AppServerEntry
        {

            public int Priority;
            public SIPURI AppServerURI;
            public bool HasFailed;

            public AppServerEntry(int priority, SIPURI appServerURI)
            {
                Priority = priority;
                AppServerURI = appServerURI;
                HasFailed = false;
            }
        }

        //private SIPEndPoint m_outboundProxy;
        private TimeSpan m_interval = new TimeSpan(0, 0, 60);   // Default interval is 1 minute.
        private List<AppServerEntry> m_appServerEntries = new List<AppServerEntry>();
        private AppServerEntry m_activeAppServerEntry;

        private Stopwatch m_startCheckTime = new Stopwatch();

        public AppServerDispatcher(SIPTransport sipTransport, XmlNode configNode)
            : base(sipTransport, configNode)
        {
            try
            {

                XmlNodeList appServerNodes = configNode.SelectNodes("appserver");
                foreach (XmlNode appServerNode in appServerNodes)
                {
                    int priority = Convert.ToInt32(appServerNode.Attributes.GetNamedItem("priority").Value);
                    SIPURI serverURI = SIPURI.ParseSIPURIRelaxed(appServerNode.InnerText);
                    AppServerEntry appServerEntry = new AppServerEntry(priority, serverURI);
                    m_appServerEntries.Add(appServerEntry);
                }

                //if (configNode.SelectSingleNode("outboundproxy") != null && !configNode.SelectSingleNode("outboundproxy").InnerText.IsNullOrBlank()) {
                //    m_outboundProxy = SIPEndPoint.ParseSIPEndPoint(configNode.SelectSingleNode("outboundproxy").InnerText);
                //}

                if (configNode.SelectSingleNode("interval") != null && !configNode.SelectSingleNode("interval").InnerText.IsNullOrBlank())
                {
                    if (!TimeSpan.TryParse(configNode.SelectSingleNode("interval").InnerText, out m_interval))
                    {
                        logger.Warn("AppServerDispatcher interval could not be parsed from " + configNode.SelectSingleNode("interval").InnerText + ".");
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AppServerDispatcher. " + excp.Message);
                throw;
            }
        }

        public override void Start()
        {
            try
            {
                base.Start();

                m_activeAppServerEntry = GetActiveAppServer();

                if (m_activeAppServerEntry != null)
                {
                    while (!m_stop)
                    {
                        Thread.Sleep(Convert.ToInt32(m_interval.TotalMilliseconds % Int32.MaxValue));

                        logger.Debug("AppServerDispatcher executing.");

                        m_startCheckTime.Reset();
                        m_startCheckTime.Start();

                        SIPClientUserAgent uac = new SIPClientUserAgent(m_sipTransport, null, null, null, LogMonitorEvent);
                        SIPCallDescriptor callDescriptor = new SIPCallDescriptor(null, null, m_activeAppServerEntry.AppServerURI.ToString(), null, null, null, null, null, SIPCallDirection.Out, null, null, null);
                        callDescriptor.MangleResponseSDP = false;
                        uac.CallFailed += CallFailed;
                        uac.CallAnswered += CallAnswered;
                        uac.Call(callDescriptor);
                        uac.ServerTransaction.CDR = null;
                    }
                }
                else
                {
                    logger.Warn("No active app server could be set in AppServerDispatcher.Start, job stopping.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception AppServerDispatcher StartJob. " + excp.Message);
            }
        }

        private AppServerEntry GetActiveAppServer()
        {
            try
            {
                AppServerEntry activeAppServer = null;
                foreach (AppServerEntry appServerEntry in m_appServerEntries)
                {
                    if (!appServerEntry.HasFailed)
                    {
                        if (activeAppServer == null)
                        {
                            activeAppServer = appServerEntry;
                        }
                        else if (activeAppServer.Priority < appServerEntry.Priority)
                        {
                            activeAppServer = appServerEntry;
                        }
                    }
                }

                if (activeAppServer == null && m_appServerEntries.Count > 0)
                {
                    logger.Debug("No active app server was set, resetting failed app servers.");
                    for (int index = 0; index < m_appServerEntries.Count; index++)
                    {
                        AppServerEntry failedAppServer = m_appServerEntries[index];
                        failedAppServer.HasFailed = false;
                        m_appServerEntries[index] = failedAppServer;
                    }

                    return GetActiveAppServer();
                }

                return activeAppServer;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SetActiveAppServer. " + excp.Message);
                return null;
            }
        }

        private void CallAnswered(ISIPClientUserAgent uac, SIPResponse sipResponse)
        {
            logger.Debug("AppServerDispatcher CallAnswered with " + (int)sipResponse.Status + " " + sipResponse.ReasonPhrase + ".");
            SIPHeader okHeader = uac.ServerTransaction.TransactionFinalResponse.Header;
            int executionCount = 0;
            foreach (string unknownHeader in okHeader.UnknownHeaders)
            {
                if (unknownHeader.StartsWith("DialPlanEngine-ExecutionCount"))
                {
                    executionCount = Convert.ToInt32(Regex.Match(unknownHeader, @"\S: (?<count>\d+)").Result("${count}"));
                    break;
                }
            }
            m_startCheckTime.Stop();
            logger.Debug("AppServerDispatcher execution count for " + m_activeAppServerEntry.AppServerURI.ToString() + " is " + executionCount + ".");
            logger.Debug("AppServerDispatcher response took " + m_startCheckTime.ElapsedMilliseconds + "ms.");
        }

        private void CallFailed(ISIPClientUserAgent uac, string errorMessage)
        {
            logger.Debug("AppServerDispatcher CallFailed with " + errorMessage + ".");
            m_activeAppServerEntry.HasFailed = true;
            m_activeAppServerEntry = GetActiveAppServer();
        }

        public override SIPEndPoint GetSIPEndPoint()
        {
            return new SIPEndPoint(m_activeAppServerEntry.AppServerURI);
        }

        public override bool IsSIPEndPointMonitored(SIPEndPoint sipEndPoint)
        {
            lock (m_activeAppServerEntry)
            {
                foreach (AppServerEntry appServerEntry in m_appServerEntries)
                {
                    if (new SIPEndPoint(appServerEntry.AppServerURI).ToString() == sipEndPoint.ToString())
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void LogMonitorEvent(SIPMonitorEvent monitorEvent)
        {
            if (monitorEvent is SIPMonitorConsoleEvent)
            {
                SIPMonitorConsoleEvent consoleEvent = monitorEvent as SIPMonitorConsoleEvent;
                if (consoleEvent.EventType != SIPMonitorEventTypesEnum.FullSIPTrace && consoleEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction)
                {
                    logger.Debug(monitorEvent.Message);
                }
            }
        }
    }
}
