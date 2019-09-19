using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SIPSorcery.Persistence;
using SIPSorcery.CRM;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIPNotifier
{
    public class SIPNotifierProgram
    {
        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;
        private static readonly string m_customersXMLFilename = SIPSorcery.CRM.CustomerSessionManager.CUSTOMERS_XML_FILENAME;
        private static readonly string m_sipAccountsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPACCOUNTS_FILENAME;
        private static readonly string m_registrarBindingsXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_REGISTRAR_BINDINGS_FILENAME;
        private static readonly string m_sipDialoguesXMLFilename = SIPSorcery.SIP.App.AssemblyState.XML_SIPDIALOGUES_FILENAME;

        private static ILog logger = AppState.logger;
        private static ManualResetEvent m_notifierUp = new ManualResetEvent(false);

        private static StorageTypes m_sipNotifierStorageType;
        private static string m_sipNotifierStorageConnStr;

        static void Main(string[] args)
        {
            try
            {
                logger.Debug("SIP Notifier starting");

                m_sipNotifierStorageType = (AppState.GetConfigSetting(m_storageTypeKey) != null) ? StorageTypesConverter.GetStorageType(AppState.GetConfigSetting(m_storageTypeKey)) : StorageTypes.Unknown; ;
                m_sipNotifierStorageConnStr = AppState.GetConfigSetting(m_connStrKey);

                if (m_sipNotifierStorageType == StorageTypes.Unknown || m_sipNotifierStorageConnStr.IsNullOrBlank())
                {
                    throw new ApplicationException("The SIP Notifier cannot start with no persistence settings.");
                }

                if (m_sipNotifierStorageType == StorageTypes.XML && !Directory.Exists(m_sipNotifierStorageConnStr))
                {
                    throw new ApplicationException("Directory " + m_sipNotifierStorageConnStr + " does not exist for XML persistor.");
                }

                SIPAssetPersistor<Customer> customerPersistor = SIPAssetPersistorFactory<Customer>.CreateSIPAssetPersistor(m_sipNotifierStorageType, m_sipNotifierStorageConnStr, m_customersXMLFilename);
                SIPAssetPersistor<SIPAccountAsset> sipAccountsPersistor = SIPAssetPersistorFactory<SIPAccountAsset>.CreateSIPAssetPersistor(m_sipNotifierStorageType, m_sipNotifierStorageConnStr, m_sipAccountsXMLFilename);
                SIPAssetPersistor<SIPRegistrarBinding> sipRegistrarBindingsPersistor = SIPAssetPersistorFactory<SIPRegistrarBinding>.CreateSIPAssetPersistor(m_sipNotifierStorageType, m_sipNotifierStorageConnStr, m_registrarBindingsXMLFilename);
                SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor = SIPAssetPersistorFactory<SIPDialogueAsset>.CreateSIPAssetPersistor(m_sipNotifierStorageType, m_sipNotifierStorageConnStr, m_sipDialoguesXMLFilename);
                SIPDomainManager sipDomainManager = new SIPDomainManager(m_sipNotifierStorageType, m_sipNotifierStorageConnStr);

                SIPNotifierDaemon daemon = new SIPNotifierDaemon(
                    customerPersistor.Get, 
                    sipDialoguePersistor.Get, 
                    sipDialoguePersistor.Get, 
                    sipDomainManager.GetDomain, 
                    sipAccountsPersistor, 
                    sipRegistrarBindingsPersistor.Get,
                    sipAccountsPersistor.Get,
                    sipRegistrarBindingsPersistor.Count,
                    SIPRequestAuthenticator.AuthenticateSIPRequest, 
                    null);

                if (args != null && args.Length == 1 && args[0].StartsWith("-c"))
                {
                    Thread daemonThread = new Thread(daemon.Start);
                    daemonThread.Start();
                    m_notifierUp.WaitOne();
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
