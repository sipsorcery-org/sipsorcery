using System.ServiceModel;
using System.ServiceModel.Channels;

namespace SIPSorcery.Silverlight.Messaging {

    public class PollingDuplexCustomHeaderBinding : PollingDuplexHttpBinding {

        private CustomHeaderBindingElement channelBindingElement;

        public PollingDuplexCustomHeaderBinding(IClientCustomHeader customHeader, PollingDuplexHttpSecurityMode securityMode)
            : base(securityMode) {
            channelBindingElement = new CustomHeaderBindingElement();
            channelBindingElement.CustomHeader = customHeader;
        }

        public override BindingElementCollection CreateBindingElements() {
            BindingElementCollection bindingElements = base.CreateBindingElements();
            bindingElements.Insert(bindingElements.Count - 1, channelBindingElement);

            return bindingElements;
        }
    }
}
