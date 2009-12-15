using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;

namespace SIPSorcery.WatchTower
{
    public class WatchTowerProgram
    {
        private static ManualResetEvent m_watchTowerUp = new ManualResetEvent(false);

        static void Main(string[] args)
        {
            try
            {
                WatchTowerDaemon daemon = new WatchTowerDaemon();

                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    Console.WriteLine("WatchTower starting");

                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();

                    m_watchTowerUp.WaitOne();
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
