using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;

namespace SIPSorcery.Web.Services
{
    [ServiceContract(Namespace = "http://www.sipsorcery.com/notifications/pull")]
    public interface INotifications
    {
        [OperationContract]
        [WebGet(UriTemplate = "isalive", ResponseFormat = WebMessageFormat.Json)]
        bool IsAlive();

        [OperationContract]
        [WebGet(UriTemplate = "login?username={username}&password={password}", ResponseFormat = WebMessageFormat.Json)]
        string Login(string username, string password);

        [OperationContract]
        [WebGet(UriTemplate = "logout")]
        void Logout();

        [OperationContract]
        [WebGet(UriTemplate = "getpollperiod", ResponseFormat = WebMessageFormat.Json)]
        int GetPollPeriod();

        [OperationContract]
        [WebGet(UriTemplate = "subscribeaddress?subject={subject}&filter={filter}", ResponseFormat = WebMessageFormat.Json)]
        string Subscribe(string subject, string filter);

        [OperationContract]
        [WebGet(UriTemplate = "subscribeforaddress?subject={subject}&filter={filter}&addressid={addressid}", ResponseFormat = WebMessageFormat.Json)]
        string SubscribeForAddress(string subject, string filter, string addressID);

        [OperationContract]
        [WebGet(UriTemplate = "getnotifications", ResponseFormat = WebMessageFormat.Json)]
        Dictionary<string, List<string>> GetNotifications();
        
        [OperationContract]
        [WebGet(UriTemplate = "getnotificationsforaddress?addressid={addressid}", ResponseFormat = WebMessageFormat.Json)]
        Dictionary<string, List<string>> GetNotificationsForAddress(string addressID);

        [OperationContract]
        [WebGet(UriTemplate = "closesession?sessionid={sessionid}")]
        void CloseSession(string sessionID);

        [OperationContract]
        [WebGet(UriTemplate = "closeconnection")]
        void CloseConnection();

        [OperationContract]
        [WebGet(UriTemplate = "closeconnectionforaddress?addressid={addressid}")]
        void CloseConnectionForAddress(string addressID);
    }
}
