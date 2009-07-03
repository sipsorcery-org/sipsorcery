using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Servers {
    
    public class AppServerDispatcher : SIPDispatcherJob {

        private string m_serverURI;
        private SIPEndPoint m_outboundProxy;
        private DateTime m_startCheckTime;

        public AppServerDispatcher(SIPTransport sipTransport, XmlNode configNode) : base(sipTransport, configNode) {
            m_serverURI = configNode.SelectSingleNode("server").InnerText;
            if(configNode.SelectSingleNode("outboundproxy") != null && !configNode.SelectSingleNode("outboundproxy").InnerText.IsNullOrBlank()) {
                m_outboundProxy = SIPEndPoint.ParseSIPEndPoint(configNode.SelectSingleNode("outboundproxy").InnerText);
            }
        }

        public override void Start() {
            try {
                base.Start();

                while (!m_stop) {
                    Thread.Sleep(10000);

                    logger.Debug("AppServerDispatcher executing.");

                    m_startCheckTime = DateTime.Now;

                    SIPClientUserAgent uac = new SIPClientUserAgent(m_sipTransport, m_outboundProxy, null, null, LogMonitorEvent);
                    SIPCallDescriptor callDescriptor = new SIPCallDescriptor(null, null, m_serverURI, null, null, null, null, null, SIPCallDirection.Out, null, null, false);
                    uac.CallFailed += CallFailed;
                    uac.CallAnswered += CallAnswered;
                    uac.Call(callDescriptor);
                    uac.ServerTransaction.CDR = null;

                    Thread.Sleep(50000);
                }
            }
            catch (Exception excp) {
                logger.Error("Exception AppServerDispatcher StartJob. " + excp.Message);
            }
        }

        private void CallAnswered(SIPClientUserAgent uac, SIPResponse sipResponse) {
            logger.Debug("AppServerDispatcher CallAnswered with " + (int)sipResponse.Status + " " + sipResponse.ReasonPhrase + ".");
            SIPHeader okHeader = uac.ServerTransaction.TransactionFinalResponse.Header;
            int executionCount = 0;
            foreach (string unknownHeader in okHeader.UnknownHeaders) {
                if (unknownHeader.StartsWith("DialPlanEngine-ExecutionCount")) {
                    executionCount = Convert.ToInt32(Regex.Match(unknownHeader, @"\S: (?<count>\d+)").Result("${count}"));
                    break;
                }
            }
            logger.Debug("AppServerDispatcher execution count for " + m_serverURI.ToString() + " is " + executionCount + ".");
            logger.Debug("AppServerDispatcher response took " + DateTime.Now.Subtract(m_startCheckTime).TotalMilliseconds.ToString("0.##") + "ms.");
        }

        private void CallFailed(SIPClientUserAgent uac, string errorMessage) {
            logger.Debug("AppServerDispatcher CallFailed with " + errorMessage + ".");
        }

        public override SIPEndPoint GetSIPEndPoint() {
            return null;
        }

        private void LogMonitorEvent(SIPMonitorEvent monitorEvent) {
            if (monitorEvent.EventType != SIPMonitorEventTypesEnum.FullSIPTrace && monitorEvent.EventType != SIPMonitorEventTypesEnum.SIPTransaction) {
                logger.Debug(monitorEvent.Message);
            }
        }
    }
}
