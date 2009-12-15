using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPRegistrar {

    public class SIPRegistrarProgram {

        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;
        private static readonly string m_sipAccountsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPACCOUNTS_FILENAME;
        private static readonly string m_sipRegistrarBindingsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_REGISTRAR_BINDINGS_FILENAME;

        private static ILog logger = AppState.logger;
        private static ManualResetEvent m_registrarUp = new ManualResetEvent(false);

        private static StorageTypes m_sipRegistrarStorageType;
        private static string m_sipRegistrarStorageConnStr;

        static void Main(string[] args) {
            try {
                m_sipRegistrarStorageType = (ConfigurationManager.AppSettings[m_storageTypeKey] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[m_storageTypeKey]) : StorageTypes.Unknown;
                m_sipRegistrarStorageConnStr = ConfigurationManager.AppSettings[m_connStrKey];

                if (m_sipRegistrarStorageType == StorageTypes.Unknown || m_sipRegistrarStorageConnStr.IsNullOrBlank()) {
                    throw new ApplicationException("The SIP Registrar cannot start with no persistence settings.");
                }

                if (m_sipRegistrarStorageType == StorageTypes.XML && !Directory.Exists(m_sipRegistrarStorageConnStr)) {
                    throw new ApplicationException("Directory " + m_sipRegistrarStorageConnStr + " does not exist for XML persistor.");
                }

                SIPAssetPersistor<SIPAccount> sipAccountsPersistor = SIPAssetPersistorFactory<SIPAccount>.CreateSIPAssetPersistor(m_sipRegistrarStorageType, m_sipRegistrarStorageConnStr, m_sipAccountsXMLFilename);
                SIPDomainManager sipDomainManager = new SIPDomainManager(m_sipRegistrarStorageType, m_sipRegistrarStorageConnStr);
                SIPAssetPersistor<SIPRegistrarBinding> sipRegistrarBindingPersistor = SIPAssetPersistorFactory<SIPRegistrarBinding>.CreateSIPAssetPersistor(m_sipRegistrarStorageType, m_sipRegistrarStorageConnStr, m_sipRegistrarBindingsXMLFilename);

                SIPRegistrarDaemon daemon = new SIPRegistrarDaemon(sipDomainManager.GetDomain, sipAccountsPersistor.Get, sipRegistrarBindingPersistor, SIPRequestAuthenticator.AuthenticateSIPRequest);

                if (args != null && args.Length == 1 && args[0].StartsWith("-c")) {
                    Console.WriteLine("SIP Registrar starting");

                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();

                    m_registrarUp.WaitOne();
                }
                else {
                    System.ServiceProcess.ServiceBase[] ServicesToRun;
                    ServicesToRun = new System.ServiceProcess.ServiceBase[] { new Service(daemon) };
                    System.ServiceProcess.ServiceBase.Run(ServicesToRun);
                }
            }
            catch (Exception excp) {
                Console.WriteLine("Exception Main. " + excp.Message);
            }
        }
    }
}
