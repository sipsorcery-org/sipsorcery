using System;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace SIPSorcery.Silverlight.Messaging
{
    public class CustomHeaderChannelFactory : ChannelFactoryBase<IRequestChannel>
    {
        private IChannelFactory<IRequestChannel> innerChannelFactory;
        private IClientCustomHeader m_customHeader;

        public CustomHeaderChannelFactory(IChannelFactory<IRequestChannel> innerChannelFactory, IClientCustomHeader customHeader)
        {
            this.m_customHeader = customHeader;
            this.innerChannelFactory = innerChannelFactory;
        }

        protected override IRequestChannel OnCreateChannel(EndpointAddress to, Uri via)
        {
            IRequestChannel innerchannel = innerChannelFactory.CreateChannel(to, via);
            CustomHeaderChannel clientChannel = new CustomHeaderChannel(this, innerchannel, m_customHeader);

            return (IRequestChannel)clientChannel;
        }

        #region Do-nothing methods just so that we don't upset the channel stack

        protected override IAsyncResult OnBeginOpen(TimeSpan timeout, AsyncCallback callback, object state)
        {
            return innerChannelFactory.BeginOpen(timeout, callback, state);
        }
        protected override void OnEndOpen(IAsyncResult result)
        {
            innerChannelFactory.EndOpen(result);
        }
        protected override IAsyncResult OnBeginClose(TimeSpan timeout, AsyncCallback callback, object state)
        {
            return innerChannelFactory.BeginClose(timeout, callback, state);
        }
        protected override void OnEndClose(IAsyncResult result)
        {
            innerChannelFactory.EndClose(result);
        }
        protected override void OnOpen(TimeSpan timeout)
        {
            innerChannelFactory.Open(timeout);
        }
        protected override void OnAbort()
        {
            innerChannelFactory.Abort();
        }
        protected override void OnClose(TimeSpan timeout)
        {
            innerChannelFactory.Close(timeout);
        }

        public override T GetProperty<T>()
        {
            return innerChannelFactory.GetProperty<T>();
        }

        #endregion
    }
}
