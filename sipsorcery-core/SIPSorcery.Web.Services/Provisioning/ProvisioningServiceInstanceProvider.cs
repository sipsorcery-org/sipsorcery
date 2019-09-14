using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Configuration;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text;
using System.Xml;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services {

    public class InstanceProviderExtensionElement : BehaviorExtensionElement {

        private const string DISABLED_PROVIDER_SERVERS_PATTERN = "DisabledProviderServersPattern";
        private const string NEW_CUSTOMERS_ALLOWED_LIMIT_KEY = "NewCustomersAllowedLimit";
        private const string INVITE_CODE_REQUIRED_KEY = "InviteCodeRequired";

        private static ILog logger = AppState.GetLogger("provisioningsvc");

        private static readonly string m_storageTypeKey = SIPSorceryConfiguration.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = SIPSorceryConfiguration.PERSISTENCE_STORAGECONNSTR_KEY;

        private static readonly string m_providersStorageFileName = AssemblyState.XML_SIPPROVIDERS_FILENAME;
        private static readonly string m_providerBindingsStorageFileName = AssemblyState.XML_SIPPROVIDERS_FILENAME;
        private static readonly string m_sipAccountsStorageFileName = AssemblyState.XML_SIPACCOUNTS_FILENAME;
        private static readonly string m_dialplansStorageFileName = AssemblyState.XML_DIALPLANS_FILENAME;
        private static readonly string m_registrarBindingsStorageFileName = AssemblyState.XML_REGISTRAR_BINDINGS_FILENAME;
        private static readonly string m_dialoguesStorageFileName = AssemblyState.XML_SIPDIALOGUES_FILENAME;
        private static readonly string m_cdrsStorageFileName = AssemblyState.XML_SIPCDRS_FILENAME;

        private static StorageTypes m_serverStorageType;
        private static string m_serverStorageConnStr;
        private static string m_disabledProviderServerPattern;
        private static int m_newCustomersAllowedLimit;
        private static bool m_inviteCodeRequired;

        protected override object CreateBehavior() {

            try {
                m_serverStorageType = (ConfigurationManager.AppSettings[m_storageTypeKey] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[m_storageTypeKey]) : StorageTypes.Unknown;
                m_serverStorageConnStr = ConfigurationManager.AppSettings[m_connStrKey];
                Int32.TryParse(ConfigurationManager.AppSettings[NEW_CUSTOMERS_ALLOWED_LIMIT_KEY], out m_newCustomersAllowedLimit);
                Boolean.TryParse(ConfigurationManager.AppSettings[INVITE_CODE_REQUIRED_KEY], out m_inviteCodeRequired);

                if (m_serverStorageType == StorageTypes.Unknown || m_serverStorageConnStr.IsNullOrBlank()) {
                    throw new ApplicationException("The Provisioning Web Service cannot start with no persistence settings specified.");
                }

                // Prevent users from creaing loopback or other crazy providers.
                m_disabledProviderServerPattern = ConfigurationManager.AppSettings[DISABLED_PROVIDER_SERVERS_PATTERN];
                if (!m_disabledProviderServerPattern.IsNullOrBlank()) {
                    SIPProvider.DisallowedServerPatterns = m_disabledProviderServerPattern;
                }

                // The Registration Agent wants to know about any changes to SIP Provider entries in order to update any SIP 
                // Provider bindings it is maintaining or needs to add or remove.
                SIPAssetPersistor<SIPProvider> sipProviderPersistor = SIPAssetPersistorFactory<SIPProvider>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_providersStorageFileName);
                SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor = SIPAssetPersistorFactory<SIPProviderBinding>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_providerBindingsStorageFileName);
                SIPProviderBindingSynchroniser sipProviderBindingSynchroniser = new SIPProviderBindingSynchroniser(sipProviderBindingsPersistor);

                sipProviderPersistor.Added += sipProviderBindingSynchroniser.SIPProviderAdded;
                sipProviderPersistor.Updated += sipProviderBindingSynchroniser.SIPProviderUpdated;
                sipProviderPersistor.Deleted += sipProviderBindingSynchroniser.SIPProviderDeleted;

                return new ProvisioningServiceInstanceProvider(
                    SIPAssetPersistorFactory<SIPAccountAsset>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_sipAccountsStorageFileName),
                    SIPAssetPersistorFactory<SIPDialPlan>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_dialplansStorageFileName),
                    sipProviderPersistor,
                    sipProviderBindingsPersistor,
                    SIPAssetPersistorFactory<SIPRegistrarBinding>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_registrarBindingsStorageFileName),
                    SIPAssetPersistorFactory<SIPDialogueAsset>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_dialoguesStorageFileName),
                    SIPAssetPersistorFactory<SIPCDRAsset>.CreateSIPAssetPersistor(m_serverStorageType, m_serverStorageConnStr, m_cdrsStorageFileName),
                    new CustomerSessionManager(m_serverStorageType, m_serverStorageConnStr),
                    new SIPDomainManager(m_serverStorageType, m_serverStorageConnStr),
                    (e) => { logger.Debug(e.Message); },
                    m_newCustomersAllowedLimit,
                    m_inviteCodeRequired);
            }
            catch (Exception excp) {
                logger.Error("Exception InstanceProviderExtensionElement CreateBehavior. " + excp.Message);
                throw;
            }
        }

        public override Type BehaviorType {
            get { return typeof(ProvisioningServiceInstanceProvider); }
        }
    }

    public class ProvisioningServiceInstanceProvider : IInstanceProvider, IServiceBehavior {

        private SIPAssetPersistor<SIPAccountAsset> m_sipAccountPersistor;
        private SIPAssetPersistor<SIPDialPlan> m_sipDialPlanPersistor;
        private SIPAssetPersistor<SIPProvider> m_sipProviderPersistor;
        private SIPAssetPersistor<SIPProviderBinding> m_sipProviderBindingsPersistor;
        private SIPAssetPersistor<SIPRegistrarBinding> m_sipRegistrarBindingsPersistor;
        private SIPAssetPersistor<SIPDialogueAsset> m_sipDialoguePersistor;
        private SIPAssetPersistor<SIPCDRAsset> m_sipCDRPersistor;
        private SIPAssetPersistor<Customer> m_crmCustomerPersistor;
        private CustomerSessionManager m_crmSessionManager;
        private SIPDomainManager m_sipDomainManager;
        private SIPMonitorLogDelegate m_logDelegate;
        private int m_newCustomersAllowedLimit;
        private bool m_inviteCodeRequired;

        public ProvisioningServiceInstanceProvider() {
        }

        public ProvisioningServiceInstanceProvider(
            SIPAssetPersistor<SIPAccountAsset> sipAccountPersistor,
            SIPAssetPersistor<SIPDialPlan> sipDialPlanPersistor,
            SIPAssetPersistor<SIPProvider> sipProviderPersistor,
            SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor,
            SIPAssetPersistor<SIPRegistrarBinding> sipRegistrarBindingsPersistor,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPAssetPersistor<SIPCDRAsset> sipCDRPersistor,
            CustomerSessionManager crmSessionManager,
            SIPDomainManager sipDomainManager,
            SIPMonitorLogDelegate log,
            int newCustomersAllowedLimit,
            bool inviteCodeRequired)
        {
            m_sipAccountPersistor = sipAccountPersistor;
            m_sipDialPlanPersistor = sipDialPlanPersistor;
            m_sipProviderPersistor = sipProviderPersistor;
            m_sipProviderBindingsPersistor = sipProviderBindingsPersistor;
            m_sipRegistrarBindingsPersistor = sipRegistrarBindingsPersistor;
            m_sipDialoguePersistor = sipDialoguePersistor;
            m_sipCDRPersistor = sipCDRPersistor;
            m_crmCustomerPersistor = crmSessionManager.CustomerPersistor;
            m_crmSessionManager = crmSessionManager;
            m_sipDomainManager = sipDomainManager;
            m_logDelegate = log;
            m_newCustomersAllowedLimit = newCustomersAllowedLimit;
            m_inviteCodeRequired = inviteCodeRequired;
        }

        public object GetInstance(InstanceContext instanceContext) {
            return GetInstance(instanceContext, null);
        }

        public object GetInstance(InstanceContext instanceContext, Message message) {
            return new SIPProvisioningWebService(
                m_sipAccountPersistor,
                m_sipDialPlanPersistor,
                m_sipProviderPersistor,
                m_sipProviderBindingsPersistor,
                m_sipRegistrarBindingsPersistor,
                m_sipDialoguePersistor,
                m_sipCDRPersistor,
                m_crmSessionManager,
                m_sipDomainManager,
                m_logDelegate,
                m_newCustomersAllowedLimit,
                m_inviteCodeRequired);
        }

        public void ReleaseInstance(InstanceContext instanceContext, object instance) { }

        public void ApplyDispatchBehavior(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
            foreach (ChannelDispatcherBase channelDispatcherBase in serviceHostBase.ChannelDispatchers) {
                ChannelDispatcher channelDispatcher = channelDispatcherBase as ChannelDispatcher;
                if (channelDispatcher != null) {
                    foreach (EndpointDispatcher endpoint in channelDispatcher.Endpoints) {
                        endpoint.DispatchRuntime.InstanceProvider = this;
                    }
                }
            }
        }

        public void AddBindingParameters(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase, System.Collections.ObjectModel.Collection<ServiceEndpoint> endpoints, BindingParameterCollection bindingParameters) {
        }

        public void Validate(ServiceDescription serviceDescription, ServiceHostBase serviceHostBase) {
        }
    }
}
