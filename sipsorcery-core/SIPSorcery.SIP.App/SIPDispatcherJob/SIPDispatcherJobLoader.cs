using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPDispatcherJobLoader
    {
        private static ILog logger = AppState.logger;

        private XmlNode m_configNode;

        public SIPDispatcherJobLoader(XmlNode configNode)
        {
            m_configNode = configNode;
        }

        public Dictionary<string, SIPDispatcherJob> Load(SIPTransport sipTransport)
        {
            try
            {
                Dictionary<string, SIPDispatcherJob> dispatcherJobs = new Dictionary<string, SIPDispatcherJob>();

                if (sipTransport == null)
                {
                    SIPChannel dispatcherChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Loopback, 0));
                    sipTransport = new SIPTransport(SIPDNSManager.ResolveSIPService, new SIPTransactionEngine(), dispatcherChannel, true);
                }

                foreach (XmlNode dispatcherNode in m_configNode.ChildNodes)
                {
                    string jobType = dispatcherNode.Attributes.GetNamedItem("class").Value;
                    string jobKey = dispatcherNode.Attributes.GetNamedItem("key").Value;

                    if (!jobKey.IsNullOrBlank() && !jobType.IsNullOrBlank())
                    {
                        SIPDispatcherJob job = SIPDispatcherJobFactory.CreateJob(jobType, dispatcherNode, sipTransport);
                        if (job != null && !dispatcherJobs.ContainsKey(jobKey))
                        {
                            ThreadPool.QueueUserWorkItem(delegate { job.Start(); });
                            dispatcherJobs.Add(jobKey, job);
                        }
                    }
                    else
                    {
                        logger.Warn("The job key or class were empty for a SIPDispatcherJob node.\n" + dispatcherNode.OuterXml);
                    }
                }

                return dispatcherJobs;
            }
            catch (Exception dispatcherExcp)
            {
                logger.Error("Exception StatelessProxyCore Starting Dispatcher. " + dispatcherExcp.Message);
                return null;
            }
        }
    }
}
