using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.Servers;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;

namespace SIPSorcery.SSHServer
{
    class SSHServerProgram
    {
        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;

        private static ManualResetEvent m_serverUp = new ManualResetEvent(false);

        private static StorageTypes m_storageType;
        private static string m_connStr;

        static void Main(string[] args)
        {
            try
            {
                m_storageType = (AppState.GetConfigSetting(m_storageTypeKey) != null) ? StorageTypesConverter.GetStorageType(AppState.GetConfigSetting(m_storageTypeKey)) : StorageTypes.Unknown;
                m_connStr = AppState.GetConfigSetting(m_connStrKey);

                CustomerSessionManager customerSessionManager = new CustomerSessionManager(m_storageType, m_connStr);
                SSHServerDaemon daemon = new SSHServerDaemon(customerSessionManager);

                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    Console.WriteLine("SSH Server starting");

                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();

                    m_serverUp.WaitOne();
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
