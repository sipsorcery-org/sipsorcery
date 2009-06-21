using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.SIPRegistrar {

    public class SIPRegistrarProgram {

        private const string XML_REGISTRAR_BINDINGS_FILENAME = "sipregistrarbindings.xml";
        public const string XML_DOMAINS_FILENAME = "sipdomains.xml";
        public const string XML_SIPACCOUNTS_FILENAME = "sipaccounts.xml";

        private static readonly string m_storageTypeKey = Persistence.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = Persistence.PERSISTENCE_STORAGECONNSTR_KEY;

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

                SIPAssetPersistor<SIPAccount> sipAccountsPersistor = null;
                SIPDomainManager sipDomainManager = null;
                SIPAssetPersistor<SIPRegistrarBinding> sipRegistrarBindingPersistor = null;

                if (m_sipRegistrarStorageType == StorageTypes.XML) {

                    if (!Directory.Exists(m_sipRegistrarStorageConnStr)) {
                        throw new ApplicationException("Directory " + m_sipRegistrarStorageConnStr + " does not exist for XML persistor.");
                    }

                    sipAccountsPersistor = SIPAssetPersistorFactory.CreateSIPAccountPersistor(StorageTypes.XML, m_sipRegistrarStorageConnStr + XML_SIPACCOUNTS_FILENAME);
                    sipDomainManager = new SIPDomainManager(StorageTypes.XML, m_sipRegistrarStorageConnStr + XML_DOMAINS_FILENAME);
                    sipRegistrarBindingPersistor = SIPAssetPersistorFactory.CreateSIPRegistrarBindingPersistor(StorageTypes.XML, m_sipRegistrarStorageConnStr + XML_REGISTRAR_BINDINGS_FILENAME);
                }
                else if (m_sipRegistrarStorageType == StorageTypes.DBLinqMySQL || m_sipRegistrarStorageType == StorageTypes.DBLinqPostgresql) {
                    sipAccountsPersistor = SIPAssetPersistorFactory.CreateSIPAccountPersistor(m_sipRegistrarStorageType, m_sipRegistrarStorageConnStr);
                    sipDomainManager = new SIPDomainManager(m_sipRegistrarStorageType, m_sipRegistrarStorageConnStr);
                    sipRegistrarBindingPersistor = SIPAssetPersistorFactory.CreateSIPRegistrarBindingPersistor(m_sipRegistrarStorageType, m_sipRegistrarStorageConnStr);
                }
                else {
                    throw new NotImplementedException(m_sipRegistrarStorageType + " is not implemented for the SIP Registrar persistor.");
                }

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
