using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App {

    public class SIPDispatcherJob {

        private const string THREAD_NAME_PREFIX = "sipdispatcherjob-";

        private static int m_jobNumber;
        protected static ILog logger = AppState.logger;

        protected XmlNode m_configNode;
        protected bool m_stop;
        protected SIPTransport m_sipTransport;

        public SIPDispatcherJob(SIPTransport sipTransport, XmlNode configNode) {
            m_jobNumber++;
            m_sipTransport = sipTransport;
            m_configNode = configNode;
        }

        public virtual void Start() {
            try {
                string jobKey = m_configNode.Attributes.GetNamedItem("key").Value;
                if (!jobKey.IsNullOrBlank()) {
                    Thread.CurrentThread.Name = THREAD_NAME_PREFIX + jobKey;
                }
                else {
                    Thread.CurrentThread.Name = THREAD_NAME_PREFIX + m_jobNumber;
                }
            }
            catch (Exception excp) {
                logger.Error("Exception SIPDispatcherJob Start. " + excp.Message);
            }
        }
        
        public virtual SIPEndPoint GetSIPEndPoint() {
            return null;
        }

        public virtual bool IsSIPEndPointMonitored(SIPEndPoint sipEndPoint) {
            return false;
        }

        public void Stop() {
            m_stop = true;
        }
    }
}
