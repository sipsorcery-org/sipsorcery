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

namespace SIPSorcery.Servers {

    public class CallManagerServiceInstanceProvider : IInstanceProvider, IServiceBehavior {

        private SIPCallManager m_sipCallManager;

        public CallManagerServiceInstanceProvider(SIPCallManager sipCallManager) {
            m_sipCallManager = sipCallManager;
        }

        public object GetInstance(InstanceContext instanceContext) {
            return GetInstance(instanceContext, null);
        }

        public object GetInstance(InstanceContext instanceContext, Message message) {
            return new CallManagerServices(m_sipCallManager);
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
