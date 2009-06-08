using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.Net;
using SIPSorcery.Sys;

namespace SIPSorcery.SIPAppServer
{
    class MainConsole
    {
        private static readonly string m_storageTypeKey = Persistence.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = Persistence.PERSISTENCE_STORAGECONNSTR_KEY;

        private static ManualResetEvent m_proxyUp = new ManualResetEvent(false);

        private static StorageTypes m_sipAppServerStorageType;
        private static string m_sipAppServerStorageConnStr;

        [STAThread]
        static void Main(string[] args)
        {
            bool isConsole = false;

            try
            {
                m_sipAppServerStorageType = (ConfigurationManager.AppSettings[m_storageTypeKey] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[m_storageTypeKey]) : StorageTypes.Unknown;
                m_sipAppServerStorageConnStr = ConfigurationManager.AppSettings[m_connStrKey];

                if (m_sipAppServerStorageType == StorageTypes.Unknown || m_sipAppServerStorageConnStr.IsNullOrBlank())
                {
                    throw new ApplicationException("The SIP Application Service cannot start with no persistence settings specified.");
                }

                SIPAppServerDaemon daemon = new SIPAppServerDaemon(m_sipAppServerStorageType, m_sipAppServerStorageConnStr);

                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    isConsole = true;
                    Console.WriteLine("SIP App Server starting");

                    Thread daemonThread = new Thread(new ThreadStart(daemon.Start));
                    daemonThread.Start();

                    m_proxyUp.WaitOne();
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
                Console.WriteLine("Exception SIP App Server Main. " + excp.Message);

                if (isConsole) {
                    Console.WriteLine("press any key to exit...");
                    Console.ReadLine();
                }
            }
        }
    }
}
