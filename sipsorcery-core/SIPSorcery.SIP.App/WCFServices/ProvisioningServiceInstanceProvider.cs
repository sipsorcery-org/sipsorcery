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
using SIPSorcery.CRM;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App {

    public class InstanceProviderExtensionElement : BehaviorExtensionElement {

        private const string DISABLED_PROVIDER_SERVERS_PATTERN = "DisabledProviderServersPattern";

        private static ILog logger = AppState.GetLogger("provisioningsvc");

        private static readonly string m_storageTypeKey = Persistence.PERSISTENCE_STORAGETYPE_KEY;
        private static readonly string m_connStrKey = Persistence.PERSISTENCE_STORAGECONNSTR_KEY;
        private static readonly string m_newCustomersAllowedLimitKey = "NewCustomersAllowedLimit";

        private static StorageTypes m_serverStorageType;
        private static string m_serverStorageConnStr;
        private static string m_disabledProviderServerPattern;
        private static int m_newCustomersAllowedLimit;

        protected override object CreateBehavior() {

            try {
                m_serverStorageType = (ConfigurationManager.AppSettings[m_storageTypeKey] != null) ? StorageTypesConverter.GetStorageType(ConfigurationManager.AppSettings[m_storageTypeKey]) : StorageTypes.Unknown;
                m_serverStorageConnStr = ConfigurationManager.AppSettings[m_connStrKey];
                Int32.TryParse(ConfigurationManager.AppSettings[m_newCustomersAllowedLimitKey], out m_newCustomersAllowedLimit);

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
                SIPAssetPersistor<SIPProvider> sipProviderPersistor = SIPAssetPersistorFactory.CreateSIPProviderPersistor(m_serverStorageType, m_serverStorageConnStr);
                SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor = SIPAssetPersistorFactory.CreateSIPProviderBindingPersistor(m_serverStorageType, m_serverStorageConnStr);
                SIPProviderBindingSynchroniser sipProviderBindingSynchroniser = new SIPProviderBindingSynchroniser(sipProviderBindingsPersistor);

                sipProviderPersistor.Added += sipProviderBindingSynchroniser.SIPProviderAdded;
                sipProviderPersistor.Updated += sipProviderBindingSynchroniser.SIPProviderUpdated;
                sipProviderPersistor.Deleted += sipProviderBindingSynchroniser.SIPProviderDeleted;

                return new ProvisioningServiceInstanceProvider(
                    SIPAssetPersistorFactory.CreateSIPAccountPersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateDialPlanPersistor(m_serverStorageType, m_serverStorageConnStr),
                    sipProviderPersistor,
                    sipProviderBindingsPersistor,
                    SIPAssetPersistorFactory.CreateSIPRegistrarBindingPersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateSIPDialoguePersistor(m_serverStorageType, m_serverStorageConnStr),
                    SIPAssetPersistorFactory.CreateSIPCDRPersistor(m_serverStorageType, m_serverStorageConnStr),
                    new CustomerSessionManager(m_serverStorageType, m_serverStorageConnStr),
                    new SIPDomainManager(m_serverStorageType, m_serverStorageConnStr),
                    (e) => { logger.Debug(e.Message); },
                    m_newCustomersAllowedLimit);
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

        private SIPAssetPersistor<SIPAccount> m_sipAccountPersistor;
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

        public ProvisioningServiceInstanceProvider() {
        }

        public ProvisioningServiceInstanceProvider(
            SIPAssetPersistor<SIPAccount> sipAccountPersistor,
            SIPAssetPersistor<SIPDialPlan> sipDialPlanPersistor,
            SIPAssetPersistor<SIPProvider> sipProviderPersistor,
            SIPAssetPersistor<SIPProviderBinding> sipProviderBindingsPersistor,
            SIPAssetPersistor<SIPRegistrarBinding> sipRegistrarBindingsPersistor,
            SIPAssetPersistor<SIPDialogueAsset> sipDialoguePersistor,
            SIPAssetPersistor<SIPCDRAsset> sipCDRPersistor,
            CustomerSessionManager crmSessionManager,
            SIPDomainManager sipDomainManager,
            SIPMonitorLogDelegate log,
            int newCustomersAllowedLimit) {

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
                m_newCustomersAllowedLimit);
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
