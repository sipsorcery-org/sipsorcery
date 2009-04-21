using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace SIPSorcery.SIPAppServer
{
    class MainConsole
    {
        private static ManualResetEvent m_proxyUp = new ManualResetEvent(false);

        [STAThread]
        static void Main(string[] args)
        {
            bool isConsole = false;

            try
            {
                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    isConsole = true;
                    Console.WriteLine("SIP App Server starting");

                    SIPAppServerDaemon daemon = new SIPAppServerDaemon();

                    Thread daemonThread = new Thread(new ThreadStart(daemon.Start));
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
                Console.WriteLine("Exception SIP App Server Main. " + excp.Message);

                if (isConsole) {
                    Console.WriteLine("press any key to exit...");
                    Console.ReadLine();
                }
            }
        }
    }
}
