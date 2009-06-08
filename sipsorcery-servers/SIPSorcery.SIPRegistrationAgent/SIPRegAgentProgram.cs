using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.SIPRegistrationAgent {

    class SIPRegAgentProgram {

        private const string SIPREGAGENT_STORAGETYPE_KEY = "SIPRegAgentStorageType";
        private const string SIPREGAGENT_STORAGECONNSTR_KEY = "SIPRegAgentConnStr";
        public const string XML_SIPPROVIDERS_FILENAME = "sipproviders.xml";
        public const string XML_PROVIDER_BINDINGS_FILENAME = "sipproviderbindings.xml";

        private static ManualResetEvent m_regAgentUp = new ManualResetEvent(false);

        private static StorageTypes m_sipRegAgentStorageType;
        private static string m_sipRegAgentStorageConnStr;

        static void Main(string[] args) {
            try {
                m_sipRegAgentStorageType = (ConfigurationManager.AppSettings[SIPREGAGENT_STORAGETYPE_KEY] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[SIPREGAGENT_STORAGETYPE_KEY]) : StorageTypes.Unknown;
                m_sipRegAgentStorageConnStr = ConfigurationManager.AppSettings[SIPREGAGENT_STORAGECONNSTR_KEY];

                if (m_sipRegAgentStorageType == StorageTypes.Unknown || m_sipRegAgentStorageConnStr.IsNullOrBlank()) {
                    throw new ApplicationException("The SIP Registration Agent cannot start with no persistence settings.");
                }

                SIPAssetPersistor<SIPProvider> sipProvidersPersistor = SIPAssetPersistorFactory.CreateSIPProviderPersistor(StorageTypes.XML, m_sipRegAgentStorageConnStr + XML_SIPPROVIDERS_FILENAME );
                SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor = SIPAssetPersistorFactory.CreateSIPProviderBindingPersistor(StorageTypes.XML, m_sipRegAgentStorageConnStr + XML_PROVIDER_BINDINGS_FILENAME);
                SynchroniseBindings(sipProvidersPersistor, sipProviderBindingsPersistor);

                SIPRegAgentDaemon daemon = new SIPRegAgentDaemon(sipProvidersPersistor.Get, sipProvidersPersistor.Update, sipProviderBindingsPersistor);

                if (args != null && args.Length == 1 && args[0].StartsWith("-c")) {
                    Console.WriteLine("SIP Registration Agent starting");

                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();

                    m_regAgentUp.WaitOne();
                }
                else {
                    System.ServiceProcess.ServiceBase[] ServicesToRun;
                    ServicesToRun = new System.ServiceProcess.ServiceBase[] { new Service(daemon) };
                    System.ServiceProcess.ServiceBase.Run(ServicesToRun);
                }
            }
            catch (Exception excp) {
                Console.WriteLine("Exception Main. " + excp.Message);
                Console.WriteLine("press any key to exit...");
                Console.ReadLine();
            }
        }

        /// <summary>
        /// Synchronises the SIP Provider entries with the SIP Provider Binding entries. If a SIP Provider is set to register and does
        /// not have abinding one needs to be added and vice versa.
        /// </summary>
        private static void SynchroniseBindings(SIPAssetPersistor<SIPProvider> sipProvidersPersistor, SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor) {

            Console.WriteLine("Synchronising SIP Provider bindings.");
            
            List<SIPProvider> sipProviders = sipProvidersPersistor.Get(null, null, 0, Int32.MaxValue);
            foreach (SIPProvider sipProvider in sipProviders) {
                if (sipProvider.RegisterEnabled && sipProvider.RegisterAdminEnabled) {
                    SIPProviderBinding binding = sipProviderBindingsPersistor.Get(b => b.ProviderId == sipProvider.Id);
                    if (binding == null) {
                        // Add a missing binding.
                        SIPProviderBinding missingBinding = new SIPProviderBinding(sipProvider);
                        sipProviderBindingsPersistor.Add(missingBinding);
                    }
                }
                else {
                    SIPProviderBinding binding = sipProviderBindingsPersistor.Get(b => b.ProviderId == sipProvider.Id);
                    if (binding != null) {
                        // Remove binding for a SIP Provider that does not have registrations enabled.
                        sipProviderBindingsPersistor.Delete(binding);
                    }
                }
            }
        }
    }
}
