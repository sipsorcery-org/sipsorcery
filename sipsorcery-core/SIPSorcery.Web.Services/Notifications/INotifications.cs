using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;

namespace SIPSorcery.Web.Services
{
    [ServiceContract(Namespace = "http://www.sipsorcery.com/notifications/pull")]
    public interface INotifications
    {
        [OperationContract]
        bool IsAlive();

        [OperationContract]
        int GetPollPeriod();

        [OperationContract]
        Dictionary<string, List<string>> GetNotifications();

        [OperationContract]
        string Subscribe(string subject, string filter);

        [OperationContract]
        void CloseSession(string sessionID);

        [OperationContract]
        void CloseConnection();
    }
}
