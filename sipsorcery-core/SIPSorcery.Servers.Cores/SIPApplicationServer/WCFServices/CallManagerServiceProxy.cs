using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading;

namespace SIPSorcery.Servers {

    public class CallManagerServiceClient : ClientBase<ICallManagerServiceProxy> {

        public EventHandler<CompletedEventArgs<bool>> IsAliveComplete;
        public EventHandler<CompletedEventArgs<string>> WebCallbackComplete;

        public CallManagerServiceClient(String endpointConfigurationName)
            : base(endpointConfigurationName) 
        { }

        public CallManagerServiceClient(System.ServiceModel.Channels.Binding binding, EndpointAddress address)
            : base(binding, address) 
        { }

        public void IsAliveAsync() {
            ClientBase<ICallManagerServiceProxy>.BeginOperationDelegate beginIsAlive = (i, c, s) => { return Channel.BeginIsAlive(c, s); };
            ClientBase<ICallManagerServiceProxy>.EndOperationDelegate endIsAlive = (r) => { bool retVal = Channel.EndIsAlive(r); return new object[] { retVal }; };
            SendOrPostCallback completeIsAlive = (s) => { InvokeAsyncCompletedEventArgs e = ((InvokeAsyncCompletedEventArgs)(s)); IsAliveComplete(this, new CompletedEventArgs<bool>(e.Results, e.Error, e.Cancelled, e.UserState)); };
            base.InvokeAsync(beginIsAlive, null, endIsAlive, completeIsAlive, null);
        }

        public void WebCallbackAsync(string username, string number) {
            ClientBase<ICallManagerServiceProxy>.BeginOperationDelegate beginWebCallback = (i, c, s) => { return Channel.BeginWebCallback(username, number, c, s); };
            ClientBase<ICallManagerServiceProxy>.EndOperationDelegate endWebCallback = (r) => { string retVal = Channel.EndWebCallback(r); return new object[] { retVal }; };
            SendOrPostCallback completeWebCallback = (s) => { InvokeAsyncCompletedEventArgs e = ((InvokeAsyncCompletedEventArgs)(s)); WebCallbackComplete(this, new CompletedEventArgs<string>(e.Results, e.Error, e.Cancelled, e.UserState)); };
            base.InvokeAsync(beginWebCallback, null, endWebCallback, completeWebCallback, null);
        }
    }

    [ServiceContractAttribute(Namespace = "http://www.sipsorcery.com/callmanager", ConfigurationName = "ICallManagerServiceProxy")]
    public interface ICallManagerServiceProxy {

        [OperationContractAttribute(AsyncPattern = true, Action = "http://www.sipsorcery.com/callmanager/ICallManagerServices/IsAlive", ReplyAction = "http://www.sipsorcery.com/callmanager/ICallManagerServices/IsAliveResponse")]
        IAsyncResult BeginIsAlive(AsyncCallback callback, object asyncState);

        bool EndIsAlive(IAsyncResult result);

        [OperationContractAttribute(AsyncPattern = true, Action = "http://www.sipsorcery.com/callmanager/ICallManagerServices/WebCallback", ReplyAction = "http://www.sipsorcery.com/callmanager/ICallManagerServices/WebCallback")]
        IAsyncResult BeginWebCallback(string username, string number, AsyncCallback callback, object asyncState);

        string EndWebCallback(IAsyncResult result);
    }

    public class CompletedEventArgs<TResult> : AsyncCompletedEventArgs {

        private object[] results;

        public CompletedEventArgs(object[] results, Exception exception, bool cancelled, object userState) :
            base(exception, cancelled, userState) {
            this.results = results;
        }

        public TResult Result {
            get {
                //base.RaiseExceptionIfNecessary();
                return ((TResult)(this.results[0]));
            }
        }
    }
}
