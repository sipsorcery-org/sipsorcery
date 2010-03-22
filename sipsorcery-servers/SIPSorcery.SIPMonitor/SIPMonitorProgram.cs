using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.Servers;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPMonitor
{
    class SIPMonitorProgram
    {
        private static ILog logger = AppState.logger;
        private static ManualResetEvent m_monitorUp = new ManualResetEvent(false);

        private static readonly string m_monitorServerID = SIPMonitorState.SIPMonitorServerID;

        static void Main(string[] args)
        {
            try
            {
                logger.Debug("SIP Monitor starting...");

                SIPMonitorClientManager sipMonitorPublisher = new SIPMonitorClientManager(m_monitorServerID);

                SIPMonitorDaemon daemon = new SIPMonitorDaemon(sipMonitorPublisher);

                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();

                    m_monitorUp.WaitOne();
                }
                else
                {
                    System.ServiceProcess.ServiceBase[] ServicesToRun;
                    ServicesToRun = new System.ServiceProcess.ServiceBase[] { new Service(daemon) };
                    System.ServiceProcess.ServiceBase.Run(ServicesToRun);
                }
            }
            catch (Exception excp)
            {
                Console.WriteLine("Exception Main. " + excp.Message);
            }
        }
    }
}
