using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Web;
using SIPSorcery.Sys;
using SIPSorcery.SIP.App;

namespace SIPSorcery.Web.Services
{
    public partial class CallDispatcherProxy : ClientBase<ICallDispatcherService>, ICallDispatcherService
    {
        public CallDispatcherProxy()
            : base()
        { }

        public CallDispatcherProxy(string clientEndPointName)
            : base(clientEndPointName)
        { }

        public bool IsAlive()
        {
            return base.Channel.IsAlive();
        }

        public void SetNextCallDest(string username, string appServerEndPoint)
        {
            base.Channel.SetNextCallDest(username, appServerEndPoint);
        }
    }
}
