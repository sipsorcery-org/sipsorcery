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

namespace SIPSorcery.SIPMonitor
{
    class SIPMonitorProgram
    {
        private static ManualResetEvent m_monitorUp = new ManualResetEvent(false);

        static void Main(string[] args)
        {
            try
            {
                SIPMonitorClientManager sipMonitorPublisher = new SIPMonitorClientManager();

                SIPMonitorDaemon daemon = new SIPMonitorDaemon(sipMonitorPublisher);

                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    Console.WriteLine("SIP Monitor starting");

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
