using System.ServiceModel;
using System.ServiceModel.Channels;

namespace SIPSorcery.Silverlight.Messaging
{
    public class BasicHttpCustomHeaderBinding : BasicHttpBinding
    {
        private CustomHeaderBindingElement channelBindingElement;

        public BasicHttpCustomHeaderBinding(IClientCustomHeader customHeader) 
        {
            channelBindingElement = new CustomHeaderBindingElement();
            channelBindingElement.CustomHeader = customHeader;
        }

        public override BindingElementCollection CreateBindingElements()
        {
            BindingElementCollection bindingElements = base.CreateBindingElements();
            bindingElements.Insert(bindingElements.Count - 1, channelBindingElement);

            return bindingElements;
        }
    }
}
