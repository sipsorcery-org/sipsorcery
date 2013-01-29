using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace SIPSorcery.Web.Services 
{
    [ServiceContract(Namespace = "http://www.sipsorcery.com/callmanager")]
    public interface ICallManagerServices {

        [OperationContract]
        [WebGet(UriTemplate = "isalive")]
        bool IsAlive();

        [OperationContract]
        [WebGet(UriTemplate = "webcallback?user={username}&number={number}")]
        string WebCallback(string username, string number);

        [OperationContract]
        [WebGet(UriTemplate = "blindtransfer?user={username}&destination={destination}&callid={replacesCallID}")]
        string BlindTransfer(string username, string destination, string replacesCallID);

        [OperationContract]
        [WebGet(UriTemplate = "dualtransfer?user={username}&callid1={callID1}&callid2={callID2}")]
        string DualTransfer(string username, string callID1, string callID2);

        [OperationContract]
        [WebGet(UriTemplate = "callback?username={username}&dialstring1={dialstring1}&dialstring2={dialstring2}")]
        string Callback(string username, string dialString1, string dialString2);
    }
}
