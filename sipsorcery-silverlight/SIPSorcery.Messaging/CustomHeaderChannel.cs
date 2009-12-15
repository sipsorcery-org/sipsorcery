using System;
using System.Collections.Generic;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace SIPSorcery.Silverlight.Messaging
{
    public class CustomHeaderChannel : ChannelBase, IRequestChannel
    {
        private IRequestChannel m_innerChannel;
        private IClientCustomHeader m_customHeader;

        #region Do-nothing properties just so that we don't upset the channel stack
        public Uri Via
        {
            get { return m_innerChannel.Via; }
        }

        public EndpointAddress RemoteAddress
        {
            get { return m_innerChannel.RemoteAddress; }
        }

        #endregion

        public CustomHeaderChannel(ChannelManagerBase channelManager, IRequestChannel innerChannel, IClientCustomHeader  customHeader)
            : base(channelManager)
        {
            this.m_innerChannel = innerChannel;
            this.m_customHeader = customHeader;
        }

        // Plug in IClientMessageInspector in the following three methods
        public IAsyncResult BeginRequest(Message message, TimeSpan timeout, AsyncCallback callback, object state)
        {
            m_customHeader.BeforeSendRequest(ref message, null);
            return m_innerChannel.BeginRequest(message, timeout, callback, state);
        }

        public IAsyncResult BeginRequest(Message message, AsyncCallback callback, object state)
        {
            return BeginRequest(message, DefaultSendTimeout, callback, state);
        }

        public Message EndRequest(IAsyncResult result)
        {
            Message message = m_innerChannel.EndRequest(result);
            m_customHeader.AfterReceiveReply(ref message, null);
            return message;
        }

        #region Do-nothing methods just so that we don't upset the channel stack


        // No sync methods in Silverlight
        Message IRequestChannel.Request(Message message, TimeSpan timeout)
        {
            throw new NotImplementedException();
        }

        // No sync methods in Silverlight
        Message IRequestChannel.Request(Message message)
        {
            throw new NotImplementedException();
        }

        protected override void OnAbort()
        {
            m_innerChannel.Abort();
        }

        protected override IAsyncResult OnBeginClose(TimeSpan timeout, AsyncCallback callback, object state)
        {
            return m_innerChannel.BeginClose(timeout, callback, state);
        }

        protected override IAsyncResult OnBeginOpen(TimeSpan timeout, AsyncCallback callback, object state)
        {
            return m_innerChannel.BeginOpen(timeout, callback, state);
        }

        // No sync methods in Silverlight
        protected override void OnClose(TimeSpan timeout)
        {
            throw new NotImplementedException();
        }

        protected override void OnEndClose(IAsyncResult result)
        {
            m_innerChannel.EndClose(result);
        }

        protected override void OnEndOpen(IAsyncResult result)
        {
            m_innerChannel.EndOpen(result);
        }

        // No sync methods in Silverlight
        protected override void OnOpen(TimeSpan timeout)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
