using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using SIPSorcery.SIP;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App {

    public class SIPDispatcherJobFactory {

        private static ILog logger = AppState.logger;

        public static SIPDispatcherJob CreateJob(string jobClass, XmlNode configNode, SIPTransport sipTransport) {

            SIPDispatcherJob job = null;

            if (jobClass != null) {
                try {
                    Type jobType = Type.GetType(jobClass);
                    job = (SIPDispatcherJob)Activator.CreateInstance(jobType, new object[] { sipTransport, configNode });
                    logger.Debug("A SIPDispatcherJob of type, " + jobClass + ", was created.");
                }
                catch (ArgumentNullException nullExcp) {
                    logger.Error("ArgumentNullException attempting to create a SIPDispatcherJob instance of type, " + jobClass + ". " + nullExcp.Message);
                    throw new ApplicationException("Unable to create SIPDispatcherJob of type " + jobClass + ". Check that the assembly is available and the class exists.");
                }
                catch (Exception excp) {
                    logger.Error("Exception attempting to create a SIPDispatcherJobinstance of type, " + jobClass + ". " + excp.Message);
                    throw excp;
                }
            }
            else {
                throw new ApplicationException("No class element existed to create a SIPDispatcherJob from.");
            }

            return job;
        }
    }	
}
