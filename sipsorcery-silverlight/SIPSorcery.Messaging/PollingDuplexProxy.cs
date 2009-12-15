using System;
using System.ComponentModel;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Threading;

namespace SIPSorcery.Silverlight.Messaging
{
    [ServiceContractAttribute(ConfigurationName = "Proxy.IPubSub", CallbackContract = typeof(IPubSubCallback))]
    public interface IPubSub
    {
        [OperationContractAttribute(AsyncPattern = true, Action = "http://tempuri.org/IPubSub/Subscribe", ReplyAction = "http://tempuri.org/IPubSub/SubscribeResponse")]
        IAsyncResult BeginSubscribe(string topic, System.AsyncCallback callback, object asyncState);

        int EndSubscribe(System.IAsyncResult result);

        [OperationContractAttribute(IsOneWay = true, AsyncPattern = true, Action = "http://tempuri.org/IPubSub/Publish")]
        IAsyncResult BeginPublish(string topic, string content, System.AsyncCallback callback, object asyncState);

        void EndPublish(System.IAsyncResult result);
    }

    public interface IPubSubCallback
    {
        [OperationContractAttribute(IsOneWay = true, Action = "http://microsoft.com/samples/pollingDuplex/notification")]
        void Notify(Message request);

        [OperationContractAttribute(IsOneWay = true, Action = "http://microsoft.com/samples/pollingDuplex/closesession")]
        void CloseSession(Message request);
    }

    public interface IPubSubChannel : IPubSub, IClientChannel
    { }

    public partial class SubscribeCompletedEventArgs : AsyncCompletedEventArgs
    {

        private object[] results;

        public SubscribeCompletedEventArgs(object[] results, System.Exception exception, bool cancelled, object userState) :
            base(exception, cancelled, userState)
        {
            this.results = results;
        }

        public int Result
        {
            get
            {
                base.RaiseExceptionIfNecessary();
                return ((int)(this.results[0]));
            }
        }
    }

    public partial class PubSubClient : DuplexClientBase<IPubSub>, IPubSub
    {
        private BeginOperationDelegate onBeginIsAliveDelegate;

        private EndOperationDelegate onEndIsAliveDelegate;

        private SendOrPostCallback onIsAliveCompletedDelegate;

        private BeginOperationDelegate onBeginSubscribeDelegate;

        private EndOperationDelegate onEndSubscribeDelegate;

        private System.Threading.SendOrPostCallback onSubscribeCompletedDelegate;

        private BeginOperationDelegate onBeginPublishDelegate;

        private EndOperationDelegate onEndPublishDelegate;

        private System.Threading.SendOrPostCallback onPublishCompletedDelegate;

        private bool useGeneratedCallback;

        private BeginOperationDelegate onBeginOpenDelegate;

        private EndOperationDelegate onEndOpenDelegate;

        private SendOrPostCallback onOpenCompletedDelegate;

        private BeginOperationDelegate onBeginCloseDelegate;

        private EndOperationDelegate onEndCloseDelegate;

        private SendOrPostCallback onCloseCompletedDelegate;

        public PubSubClient(InstanceContext callbackInstance, System.ServiceModel.Channels.Binding binding, System.ServiceModel.EndpointAddress remoteAddress) :
            base(callbackInstance, binding, remoteAddress)
        {
        }

        public PubSubClient(System.ServiceModel.Channels.Binding binding, System.ServiceModel.EndpointAddress remoteAddress) :
            this(new PubSubClientCallback(), binding, remoteAddress)
        {
        }

        private PubSubClient(PubSubClientCallback callbackImpl, System.ServiceModel.Channels.Binding binding, System.ServiceModel.EndpointAddress remoteAddress) :
            this(new System.ServiceModel.InstanceContext(callbackImpl), binding, remoteAddress)
        {
            useGeneratedCallback = true;
            callbackImpl.Initialize(this);
        }

        public CookieContainer CookieContainer
        {
            get
            {
                System.ServiceModel.Channels.IHttpCookieContainerManager httpCookieContainerManager = this.InnerChannel.GetProperty<System.ServiceModel.Channels.IHttpCookieContainerManager>();
                if ((httpCookieContainerManager != null))
                {
                    return httpCookieContainerManager.CookieContainer;
                }
                else
                {
                    return null;
                }
            }
            set
            {
                System.ServiceModel.Channels.IHttpCookieContainerManager httpCookieContainerManager = this.InnerChannel.GetProperty<System.ServiceModel.Channels.IHttpCookieContainerManager>();
                if ((httpCookieContainerManager != null))
                {
                    httpCookieContainerManager.CookieContainer = value;
                }
                else
                {
                    throw new System.InvalidOperationException("Unable to set the CookieContainer. Please make sure the binding contains an HttpC" +
                            "ookieContainerBindingElement.");
                }
            }
        }

        public event EventHandler<SubscribeCompletedEventArgs> SubscribeCompleted;

        public event EventHandler<AsyncCompletedEventArgs> PublishCompleted;

        public event EventHandler<NotifyReceivedEventArgs> NotifyReceived;

        public event EventHandler<NotifyReceivedEventArgs> CloseSessionReceived;

        public event EventHandler<AsyncCompletedEventArgs> OpenCompleted;

        public event EventHandler<AsyncCompletedEventArgs> CloseCompleted;

        public IAsyncResult BeginSubscribe(string topic, AsyncCallback callback, object asyncState)
        {
            return base.Channel.BeginSubscribe(topic, callback, asyncState);
        }

        public int EndSubscribe(IAsyncResult result)
        {
            return base.Channel.EndSubscribe(result);
        }

        private IAsyncResult OnBeginSubscribe(object[] inValues, AsyncCallback callback, object asyncState)
        {
            string topic = ((string)(inValues[0]));
            return ((IPubSub)(this)).BeginSubscribe(topic, callback, asyncState);
        }

        private object[] OnEndSubscribe(IAsyncResult result)
        {
            int retVal = ((IPubSub)(this)).EndSubscribe(result);
            return new object[] {
                    retVal};
        }

        private void OnSubscribeCompleted(object state)
        {
            if ((this.SubscribeCompleted != null))
            {
                InvokeAsyncCompletedEventArgs e = ((InvokeAsyncCompletedEventArgs)(state));
                this.SubscribeCompleted(this, new SubscribeCompletedEventArgs(e.Results, e.Error, e.Cancelled, e.UserState));
            }
        }

        public void SubscribeAsync(string topic)
        {
            this.SubscribeAsync(topic, null);
        }

        public void SubscribeAsync(string topic, object userState)
        {
            if ((this.onBeginSubscribeDelegate == null))
            {
                this.onBeginSubscribeDelegate = new BeginOperationDelegate(this.OnBeginSubscribe);
            }
            if ((this.onEndSubscribeDelegate == null))
            {
                this.onEndSubscribeDelegate = new EndOperationDelegate(this.OnEndSubscribe);
            }
            if ((this.onSubscribeCompletedDelegate == null))
            {
                this.onSubscribeCompletedDelegate = new System.Threading.SendOrPostCallback(this.OnSubscribeCompleted);
            }
            base.InvokeAsync(this.onBeginSubscribeDelegate, new object[] {
                        topic}, this.onEndSubscribeDelegate, this.onSubscribeCompletedDelegate, userState);
        }

        public IAsyncResult BeginPublish(string topic, string content, AsyncCallback callback, object asyncState)
        {
            return base.Channel.BeginPublish(topic, content, callback, asyncState);
        }

        public void EndPublish(IAsyncResult result)
        {
            base.Channel.EndPublish(result);
        }

        private IAsyncResult OnBeginPublish(object[] inValues, AsyncCallback callback, object asyncState)
        {
            string topic = ((string)(inValues[0]));
            string content = ((string)(inValues[1]));
            return ((IPubSub)(this)).BeginPublish(topic, content, callback, asyncState);
        }

        private object[] OnEndPublish(System.IAsyncResult result)
        {
            ((IPubSub)(this)).EndPublish(result);
            return null;
        }

        private void OnPublishCompleted(object state)
        {
            if ((this.PublishCompleted != null))
            {
                InvokeAsyncCompletedEventArgs e = ((InvokeAsyncCompletedEventArgs)(state));
                this.PublishCompleted(this, new System.ComponentModel.AsyncCompletedEventArgs(e.Error, e.Cancelled, e.UserState));
            }
        }

        public void PublishAsync(string topic, string content)
        {
            this.PublishAsync(topic, content, null);
        }

        public void PublishAsync(string topic, string content, object userState)
        {
            if ((this.onBeginPublishDelegate == null))
            {
                this.onBeginPublishDelegate = new BeginOperationDelegate(this.OnBeginPublish);
            }
            if ((this.onEndPublishDelegate == null))
            {
                this.onEndPublishDelegate = new EndOperationDelegate(this.OnEndPublish);
            }
            if ((this.onPublishCompletedDelegate == null))
            {
                this.onPublishCompletedDelegate = new System.Threading.SendOrPostCallback(this.OnPublishCompleted);
            }
            base.InvokeAsync(this.onBeginPublishDelegate, new object[] {
                        topic,
                        content}, this.onEndPublishDelegate, this.onPublishCompletedDelegate, userState);
        }

        private void OnNotifyReceived(object state)
        {
            if ((this.NotifyReceived != null))
            {
                object[] results = ((object[])(state));
                this.NotifyReceived(this, new NotifyReceivedEventArgs(results, null, false, null));
            }
        }

        private void OnCloseSessionReceived(object state)
        {
            if ((this.CloseSessionReceived != null))
            {
                object[] results = ((object[])(state));
                this.CloseSessionReceived(this, new NotifyReceivedEventArgs(results, null, false, null));
            }
        }

        private void VerifyCallbackEvents()
        {
            if (((this.useGeneratedCallback != true)
                        && (this.NotifyReceived != null)))
            {
                throw new System.InvalidOperationException("Callback events cannot be used when the callback InstanceContext is specified. Pl" +
                        "ease choose between specifying the callback InstanceContext or subscribing to th" +
                        "e callback events.");
            }
        }

        private IAsyncResult OnBeginOpen(object[] inValues, System.AsyncCallback callback, object asyncState)
        {
            this.VerifyCallbackEvents();
            return ((System.ServiceModel.ICommunicationObject)(this)).BeginOpen(callback, asyncState);
        }

        private object[] OnEndOpen(System.IAsyncResult result)
        {
            ((System.ServiceModel.ICommunicationObject)(this)).EndOpen(result);
            return null;
        }

        private void OnOpenCompleted(object state)
        {
            if ((this.OpenCompleted != null))
            {
                InvokeAsyncCompletedEventArgs e = ((InvokeAsyncCompletedEventArgs)(state));
                this.OpenCompleted(this, new System.ComponentModel.AsyncCompletedEventArgs(e.Error, e.Cancelled, e.UserState));
            }
        }

        public void OpenAsync()
        {
            this.OpenAsync(null);
        }

        public void OpenAsync(object userState)
        {
            if ((this.onBeginOpenDelegate == null))
            {
                this.onBeginOpenDelegate = new BeginOperationDelegate(this.OnBeginOpen);
            }
            if ((this.onEndOpenDelegate == null))
            {
                this.onEndOpenDelegate = new EndOperationDelegate(this.OnEndOpen);
            }
            if ((this.onOpenCompletedDelegate == null))
            {
                this.onOpenCompletedDelegate = new System.Threading.SendOrPostCallback(this.OnOpenCompleted);
            }
            base.InvokeAsync(this.onBeginOpenDelegate, null, this.onEndOpenDelegate, this.onOpenCompletedDelegate, userState);
        }

        private IAsyncResult OnBeginClose(object[] inValues, System.AsyncCallback callback, object asyncState)
        {
            return ((ICommunicationObject)(this)).BeginClose(callback, asyncState);
        }

        private object[] OnEndClose(System.IAsyncResult result)
        {
            ((ICommunicationObject)(this)).EndClose(result);
            return null;
        }

        private void OnCloseCompleted(object state)
        {
            if ((this.CloseCompleted != null))
            {
                InvokeAsyncCompletedEventArgs e = ((InvokeAsyncCompletedEventArgs)(state));
                this.CloseCompleted(this, new AsyncCompletedEventArgs(e.Error, e.Cancelled, e.UserState));
            }
        }

        public void CloseAsync()
        {
            this.CloseAsync(null);
        }

        public void CloseAsync(object userState)
        {
            if ((this.onBeginCloseDelegate == null))
            {
                this.onBeginCloseDelegate = new BeginOperationDelegate(this.OnBeginClose);
            }
            if ((this.onEndCloseDelegate == null))
            {
                this.onEndCloseDelegate = new EndOperationDelegate(this.OnEndClose);
            }
            if ((this.onCloseCompletedDelegate == null))
            {
                this.onCloseCompletedDelegate = new System.Threading.SendOrPostCallback(this.OnCloseCompleted);
            }
            base.InvokeAsync(this.onBeginCloseDelegate, null, this.onEndCloseDelegate, this.onCloseCompletedDelegate, userState);
        }

        protected override IPubSub CreateChannel()
        {
            return new PubSubClientChannel(this);
        }

        private class PubSubClientCallback : object, IPubSubCallback
        {
            private PubSubClient proxy;

            public void Initialize(PubSubClient proxy)
            {
                this.proxy = proxy;
            }

            public void Notify(Message request)
            {
                this.proxy.OnNotifyReceived(new object[] { request});
            }

            public void CloseSession(Message request)
            {
                this.proxy.OnCloseSessionReceived(new object[] { request });
            }
        }

        private class PubSubClientChannel : ChannelBase<IPubSub>, IPubSub
        {
            public PubSubClientChannel(DuplexClientBase<IPubSub> client) :
                base(client)
            { }

            public IAsyncResult BeginSubscribe(string topic, AsyncCallback callback, object asyncState)
            {
                object[] _args = new object[1];
                _args[0] = topic;
                IAsyncResult _result = base.BeginInvoke("Subscribe", _args, callback, asyncState);
                return _result;
            }

            public int EndSubscribe(System.IAsyncResult result)
            {
                // The Subcribe method is OneWay on the server. The underlying PollDuplex channel
                // will not pass any Subscribe response through and it will always timeout.
                return 0;
            }

            public System.IAsyncResult BeginPublish(string topic, string content, System.AsyncCallback callback, object asyncState)
            {
                object[] _args = new object[2];
                _args[0] = topic;
                _args[1] = content;
                IAsyncResult _result = base.BeginInvoke("Publish", _args, callback, asyncState);
                return _result;
            }

            public void EndPublish(System.IAsyncResult result)
            {
                object[] _args = new object[0];
                base.EndInvoke("Publish", _args, result);
            }
        }
    }

    public class NotifyReceivedEventArgs : AsyncCompletedEventArgs
    {
        private object[] results;

        public NotifyReceivedEventArgs(object[] results, Exception exception, bool cancelled, object userState) :
            base(exception, cancelled, userState)
        {
            this.results = results;
        }

        public Message request
        {
            get
            {
                base.RaiseExceptionIfNecessary();
                return ((Message)(this.results[0]));
            }
        }
    }
}
