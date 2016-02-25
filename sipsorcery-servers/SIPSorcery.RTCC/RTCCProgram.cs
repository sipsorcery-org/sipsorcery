using System;
using System.Threading;
using SIPSorcery.Persistence;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.RTCC
{
    class RTCCProgram
    {
        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;

        private static ILog logger = AppState.logger;
        private static ManualResetEvent m_rtccUp = new ManualResetEvent(false);

        private static StorageTypes m_rtccStorageType;
        private static string m_rtccStorageConnStr;

        static void Main(string[] args)
        {
            try
            {
                logger.Debug("RTCC Server starting");

                m_rtccStorageType = (AppState.GetConfigSetting(m_storageTypeKey) != null) ? StorageTypesConverter.GetStorageType(AppState.GetConfigSetting(m_storageTypeKey)) : StorageTypes.Unknown; ;
                m_rtccStorageConnStr = AppState.GetConfigSetting(m_connStrKey);

                if (m_rtccStorageType == StorageTypes.Unknown || m_rtccStorageConnStr.IsNullOrBlank())
                {
                    throw new ApplicationException("The RTCC Server cannot start with no persistence settings.");
                }

                var sipSorceryPersistor = new SIPSorceryPersistor(m_rtccStorageType, m_rtccStorageConnStr);
                RTCCDaemon daemon = new RTCCDaemon(sipSorceryPersistor);

                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();
                    m_rtccUp.WaitOne();
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
