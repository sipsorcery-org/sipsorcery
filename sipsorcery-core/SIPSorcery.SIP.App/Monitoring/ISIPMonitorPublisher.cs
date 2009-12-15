using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Linq;
using System.ServiceModel;
using System.Text;

namespace SIPSorcery.SIP.App {

    public delegate List<string> GetNotificationsDelegate(string address, out string sessionID, out string sessionError);

    [ServiceContract(CallbackContract = typeof(ISIPMonitorNotificationReady), Namespace = "http://www.sipsorcery.com/notifications", ConfigurationName = "SIPSorcery.SIP.App.ISIPMonitorPublisher")]
    public interface ISIPMonitorPublisher 
    {
        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/Subscribe", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/SubscribeResponse")]
        string Subscribe(string customerUsername, string adminId, string address, string subject, string filter);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/GetNotifications", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/GetNotificationsResponse")]
        List<string> GetNotifications(string address, out string sessionID, out string sessionError);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/RegisterListener", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/RegisterListenerResponse")]
        void RegisterListener(string address);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/IsNotificationReady", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/IsNotificationReadyResponse")]
        bool IsNotificationReady(string address);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/CloseSession", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/CloseSessionResponse")]
        void CloseSession(string address, string sessionID);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/CloseConnection", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/CloseConnectionResponse")]
        void CloseConnection(string address);

        void RegisterListener(string address, Action<string> notificationsReady);

        void MonitorEventReceived(SIPMonitorEvent monitorEvent);
    }

    public interface ISIPMonitorNotificationReady
    {
        [OperationContract(IsOneWay = true, Action = "http://www.sipsorcery.com/notifications/ISIPMonitorNotificationReady/NotificationReady")]
        void NotificationReady(string address);
    }
}
