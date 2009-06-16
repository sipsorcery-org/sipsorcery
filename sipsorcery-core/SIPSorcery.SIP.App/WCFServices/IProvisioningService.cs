using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;

namespace SIPSorcery.SIP.App {

    [ServiceContract(Namespace = "http://www.sipsorcery.com")]
    public interface IProvisioningService {

        [OperationContract]
        bool IsAlive();
    }
}
