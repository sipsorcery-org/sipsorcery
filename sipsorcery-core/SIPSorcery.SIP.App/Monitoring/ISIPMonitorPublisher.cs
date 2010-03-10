using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Linq;
using System.ServiceModel;
using System.Text;

namespace SIPSorcery.SIP.App {

    public delegate List<string> GetNotificationsDelegate(string address, out string sessionID, out string sessionError);

    //[ServiceContract(CallbackContract = typeof(ISIPMonitorNotificationReady), Namespace = "http://www.sipsorcery.com/notifications", ConfigurationName = "SIPSorcery.SIP.App.ISIPMonitorPublisher")]
    [ServiceContract(Namespace = "http://www.sipsorcery.com/notifications", ConfigurationName = "SIPSorcery.SIP.App.ISIPMonitorPublisher")]
    public interface ISIPMonitorPublisher 
    {
        event Action<string> NotificationReady;

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/IsAlive", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/IsAliveResponse")]
        bool IsAlive();

        /// <summary>
        /// This method subscribes a client to the monitor event publisher so that server monitor events can be provided to the client.
        /// </summary>
        /// <param name="customerUsername">The username of the customer subscribing for monitor events.</param>
        /// <param name="adminId">The admin ID of the customer subscribing for monitor events.</param>
        /// <param name="address">The address identifier of the connection that events are being subscribed for over. A single 
        /// address can have multiple sessions and it maps 1-to-1 with the physical connection.</param>
        /// <param name="subject">The type of filter being set. Can be one of ControlClient or Machine.</param>
        /// <param name="filter">The user provided filter for the monitor events. The filter tells the monitor event publisher what events
        /// this session is interested in.</param>
        /// <returns>The session ID for the subscription request. Each matching monitor event will have a session ID set.</returns>
        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/Subscribe", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/SubscribeResponse")]
        string Subscribe(string customerUsername, string adminId, string address, string subject, string filter, int expiry, out string subscribeError);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/GetNotifications", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/GetNotificationsResponse")]
        List<string> GetNotifications(string address, out string sessionID, out string sessionError);

        //[OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/RegisterListener", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/RegisterListenerResponse")]
        //void RegisterListener(string address);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/IsNotificationReady", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/IsNotificationReadyResponse")]
        bool IsNotificationReady(string address);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/CloseSession", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/CloseSessionResponse")]
        void CloseSession(string address, string sessionID);

        [OperationContract(Action = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/CloseConnection", ReplyAction = "http://www.sipsorcery.com/notifications/ISIPMonitorPublisher/CloseConnectionResponse")]
        void CloseConnection(string address);

        //void RegisterListener(string address, Action<string> notificationsReady);

        void MonitorEventReceived(SIPMonitorEvent monitorEvent);
    }

    public interface ISIPMonitorNotificationReady
    {
        [OperationContract(IsOneWay = true, Action = "http://www.sipsorcery.com/notifications/ISIPMonitorNotificationReady/NotificationReady")]
        void NotificationReady(string address);
    }
}
