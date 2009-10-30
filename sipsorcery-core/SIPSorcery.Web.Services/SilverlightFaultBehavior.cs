using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Configuration;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;

namespace SIPSorcery.Web.Services
{
    public class SilverlightFaultBehavior : BehaviorExtensionElement, IEndpointBehavior
    {
        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters)
        {  }

        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {  }

        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
        {
            SilverlightFaultMessageInspector inspector = new SilverlightFaultMessageInspector();
            endpointDispatcher.DispatchRuntime.MessageInspectors.Add(inspector);
        }

        public void Validate(ServiceEndpoint endpoint)
        {
        }

        public override System.Type BehaviorType
        {
            get { return typeof(SilverlightFaultBehavior); }
        }

        protected override object CreateBehavior()
        {
            return new SilverlightFaultBehavior();
        }
    }

    public class SilverlightFaultMessageInspector : IDispatchMessageInspector
    {
        object IDispatchMessageInspector.AfterReceiveRequest(ref System.ServiceModel.Channels.Message request, IClientChannel channel, InstanceContext instanceContext)
        {
            // Do nothing to the incoming message
            return null;
        }

        void IDispatchMessageInspector.BeforeSendReply(ref System.ServiceModel.Channels.Message reply, object correlationState)
        {
            if (reply.IsFault)
            {
                HttpResponseMessageProperty property = new HttpResponseMessageProperty();
                property.StatusCode = System.Net.HttpStatusCode.OK; // 200

                reply.Properties[HttpResponseMessageProperty.Name] = property;
            }
        }
    }
}