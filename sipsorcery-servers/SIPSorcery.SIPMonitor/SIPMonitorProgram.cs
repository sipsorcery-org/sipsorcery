using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.Sys;

namespace SIPSorcery.SIPMonitor
{
    class SIPMonitorProgram
    {
        private static readonly string m_storageTypeKey = Persistence.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = Persistence.PERSISTENCE_STORAGECONNSTR_KEY;

        private static ManualResetEvent m_monitorUp = new ManualResetEvent(false);

        private static StorageTypes m_storageType;
        private static string m_connStr;

        static void Main(string[] args)
        {
            try
            {
                m_storageType = (ConfigurationManager.AppSettings[m_storageTypeKey] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[m_storageTypeKey]) : StorageTypes.Unknown;
                m_connStr = ConfigurationManager.AppSettings[m_connStrKey];

                CustomerSessionManager customerSessionManager = new CustomerSessionManager(m_storageType, m_connStr);
                SIPMonitorDaemon daemon = new SIPMonitorDaemon(customerSessionManager);

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
