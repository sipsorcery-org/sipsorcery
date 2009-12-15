using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace SIPSorcery.SIPProxy
{
    class SIPProxyProgram
    {
        private static ManualResetEvent m_proxyUp = new ManualResetEvent(false);

        static void Main(string[] args)
        {
            try
            {
                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    Console.WriteLine("SIP Proxy starting");

                    SIPProxyDaemon daemon = new SIPProxyDaemon();

                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();

                    m_proxyUp.WaitOne();
                }
                else
                {
                    System.ServiceProcess.ServiceBase[] ServicesToRun;
                    ServicesToRun = new System.ServiceProcess.ServiceBase[] { new Service() };
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
