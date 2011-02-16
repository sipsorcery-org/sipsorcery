using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPRegistrationAgent
{
    public class SIPRegAgentProgram
    {
        private const int MIN_THREADPOOL_WORKERS = 50;  // The reg agent uses the threadpool to perform database operations and it needs a larger than
                                                        // normal number of idle threads on standby.

        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;
        private static readonly string m_sipProvidersXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPPROVIDERS_FILENAME;
        private static readonly string m_sipProviderBindingsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_PROVIDER_BINDINGS_FILENAME;

        private static ILog logger = AppState.logger;

        private static ManualResetEvent m_regAgentUp = new ManualResetEvent(false);

        private static StorageTypes m_sipRegAgentStorageType;
        private static string m_sipRegAgentStorageConnStr;

        static void Main(string[] args)
        {
            try
            {
                logger.Debug("SIP Registration Agent starting");

                m_sipRegAgentStorageType = (AppState.GetConfigSetting(m_storageTypeKey) != null) ? StorageTypesConverter.GetStorageType(AppState.GetConfigSetting(m_storageTypeKey)) : StorageTypes.Unknown;
                m_sipRegAgentStorageConnStr = AppState.GetConfigSetting(m_connStrKey);

                if (m_sipRegAgentStorageType == StorageTypes.Unknown || m_sipRegAgentStorageConnStr.IsNullOrBlank())
                {
                    throw new ApplicationException("The SIP Registration Agent cannot start with no persistence settings.");
                }

                if (m_sipRegAgentStorageType == StorageTypes.XML && !Directory.Exists(m_sipRegAgentStorageConnStr))
                {
                    throw new ApplicationException("Directory " + m_sipRegAgentStorageConnStr + " does not exist for XML persistor.");
                }

                int minWorker, minIOC;
                ThreadPool.GetMinThreads(out minWorker, out minIOC);
                ThreadPool.SetMinThreads(MIN_THREADPOOL_WORKERS, minIOC);
                logger.Debug("ThreadPool minimum idle thread adusted from " + minWorker + " to " + MIN_THREADPOOL_WORKERS + ".");

                SIPAssetPersistor<SIPProvider> sipProvidersPersistor = SIPAssetPersistorFactory<SIPProvider>.CreateSIPAssetPersistor(m_sipRegAgentStorageType, m_sipRegAgentStorageConnStr, m_sipProvidersXMLFilename);
                SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor = SIPAssetPersistorFactory<SIPProviderBinding>.CreateSIPAssetPersistor(m_sipRegAgentStorageType, m_sipRegAgentStorageConnStr, m_sipProviderBindingsXMLFilename);

                SIPRegAgentDaemon daemon = new SIPRegAgentDaemon(sipProvidersPersistor, sipProviderBindingsPersistor);
                SIPDNSManager.SIPMonitorLogEvent = daemon.FireSIPMonitorEvent;

                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();
                    m_regAgentUp.WaitOne();
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
                logger.Error("Exception Main. " + excp.Message);
            }
        }
    }
}
