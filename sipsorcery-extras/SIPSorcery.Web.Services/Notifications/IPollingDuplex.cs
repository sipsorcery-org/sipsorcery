using System;
using System.Collections.Generic;
using System.Text;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace SIPSorcery.Web.Services
{
    [ServiceContract]
    public interface IPollingDuplex
    {
        [OperationContract(
            AsyncPattern = true,
            Action="http://docs.oasis-open.org/ws-rx/wsmc/200702/MakeConnection", 
            ReplyAction="*")]
        IAsyncResult BeginMakeConnect(MakeConnection poll, AsyncCallback callback, object state);
        
        Message EndMakeConnect(IAsyncResult result);
    }
}
