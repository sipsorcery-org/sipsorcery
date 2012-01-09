using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;

namespace SIPSorcery.Web.Services {

    public partial class CallManagerProxy : ClientBase<ICallManagerServices>, ICallManagerServices
    {
        public CallManagerProxy()
            : base()
        { }

        public CallManagerProxy(Binding binding, EndpointAddress address)
            : base(binding, address)
        { }

        public bool IsAlive()
        {
            return base.Channel.IsAlive();
        }

        public string WebCallback(string username, string number)
        {
            return base.Channel.WebCallback(username, number);
        }

        public string BlindTransfer(string username, string destination, string replaceCallID)
        {
            return base.Channel.BlindTransfer(username, destination, replaceCallID);
        }

        public string DualTransfer(string username, string callID1, string callID2)
        {
            return base.Channel.DualTransfer(username, callID1, callID2);
        }

        public string Callback(string username, string dialString1, string dialString2)
        {
            return base.Channel.Callback(username, dialString1, dialString2);
        }
    }
}
