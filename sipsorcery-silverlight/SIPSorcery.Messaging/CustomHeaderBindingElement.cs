using System;
using System.Collections.Generic;
using System.ServiceModel.Channels;

namespace SIPSorcery.Silverlight.Messaging
{
    public class CustomHeaderBindingElement : BindingElement
    {
        public IClientCustomHeader CustomHeader { get; set; }

        public CustomHeaderBindingElement()
        {
        }

        public override IChannelFactory<TChannel> BuildChannelFactory<TChannel>(BindingContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            if (!this.CanBuildChannelFactory<TChannel>(context))
            {
                throw new InvalidOperationException("Unsupported channel type");
            }

            CustomHeaderChannelFactory factory = new CustomHeaderChannelFactory(
                context.BuildInnerChannelFactory<IRequestChannel>(), CustomHeader);

            return (IChannelFactory<TChannel>)factory;

        }

        #region Do-nothing methods just so that we don't upset the channel stack

        public override BindingElement Clone()
        {
            return new CustomHeaderBindingElement() { CustomHeader = this.CustomHeader };
        }

        public override T GetProperty<T>(BindingContext context)
        {
            return context.GetInnerProperty<T>();
        }

        public override bool CanBuildChannelFactory<TChannel>(BindingContext context)
        {
            return context.CanBuildInnerChannelFactory<TChannel>();
        }

        #endregion
    }
}
