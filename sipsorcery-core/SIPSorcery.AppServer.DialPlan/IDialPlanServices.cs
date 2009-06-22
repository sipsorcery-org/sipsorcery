using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace SIPSorcery.AppServer.DialPlan {

    [ServiceContract(Namespace = "http://www.sipsorcery.com/dialplan")]
    public interface IDialPlanServices {

        [OperationContract]
        [WebGet(UriTemplate = "isalive")]
        bool IsAlive();

        [OperationContract]
        [WebGet(UriTemplate = "webcallback?user={username}&number={number}")]
        string WebCallback(string username, string number);
    }
}
