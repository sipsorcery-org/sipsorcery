using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.SIPRegistrar {

    class SIPRegistrarProgram {

        private const string SIPREGISTRAR_STORAGETYPE_KEY = "SIPRegistrarStorageType";
        private const string SIPREGISTRAR_STORAGECONNSTR_KEY = "SIPRegistrarConnStr";
        private const string XML_REGISTRAR_BINDINGS_FILENAME = "sipregistrarbindings.xml";
        public const string XML_DOMAINS_FILENAME = "sipdomains.xml";
        public const string XML_SIPACCOUNTS_FILENAME = "sipaccounts.xml";

        private static ManualResetEvent m_registrarUp = new ManualResetEvent(false);

        private static StorageTypes m_sipRegistrarStorageType;
        private static string m_sipRegistrarStorageConnStr;

        static void Main(string[] args) {
            try {
                m_sipRegistrarStorageType = (ConfigurationManager.AppSettings[SIPREGISTRAR_STORAGETYPE_KEY] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[SIPREGISTRAR_STORAGETYPE_KEY]) : StorageTypes.Unknown;
                m_sipRegistrarStorageConnStr = ConfigurationManager.AppSettings[SIPREGISTRAR_STORAGECONNSTR_KEY];

                if (m_sipRegistrarStorageType == StorageTypes.Unknown || m_sipRegistrarStorageConnStr.IsNullOrBlank()) {
                    throw new ApplicationException("The SIP Registrar cannot start with no persistence settings.");
                }

                SIPAssetPersistor<SIPAccount> sipAccountsPersistor = SIPAssetPersistorFactory.CreateSIPAccountPersistor(StorageTypes.XML, m_sipRegistrarStorageConnStr + XML_SIPACCOUNTS_FILENAME);
                SIPDomainManager sipDomainManager = new SIPDomainManager(StorageTypes.XML, m_sipRegistrarStorageConnStr + XML_DOMAINS_FILENAME);
                SIPAssetPersistor<SIPRegistrarBinding> sipRegistrarBindingPersistor = SIPAssetPersistorFactory.CreateSIPRegistrarBindingPersistor(StorageTypes.XML, m_sipRegistrarStorageConnStr + XML_REGISTRAR_BINDINGS_FILENAME);

                SIPRegistrarDaemon daemon = new SIPRegistrarDaemon(sipDomainManager.GetDomain, sipAccountsPersistor.Get, sipRegistrarBindingPersistor);

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
