using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace SIPSorcery.Web.Services {

    [ServiceContract(Namespace = "http://www.sipsorcery.com/calldispatcher")]
    public interface ICallDispatcherService {

        [OperationContract]
        bool IsAlive();

        [OperationContract(IsOneWay=true)]
        void SetNextCallDest(string username, string appServerEndPoint);
    }
}
