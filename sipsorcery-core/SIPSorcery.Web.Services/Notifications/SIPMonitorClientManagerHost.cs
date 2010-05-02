using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Web;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery.Web.Services
{
    //[CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, UseSynchronizationContext = false)]
    [ServiceBehavior(InstanceContextMode=InstanceContextMode.Single, ConcurrencyMode=ConcurrencyMode.Multiple)]
    public class SIPMonitorClientManagerHost : ISIPMonitorPublisher
    {
        private ISIPMonitorPublisher m_publisher;

        public event Action<string> NotificationReady;              // Not used across WCF channels, for in memory only.
        public event Func<SIPMonitorEvent, bool> MonitorEventReady; // Not used.

        public SIPMonitorClientManagerHost(ISIPMonitorPublisher publisher)
        {
            m_publisher = publisher;
        }

        public bool IsAlive()
        {
            return m_publisher.IsAlive();
        }

        public string Subscribe(string customerUsername, string adminId, string address, string sessionID, string subject, string filter, int expiry, string udpSocket, out string subscribeError)
        {
            return m_publisher.Subscribe(customerUsername, adminId, address, sessionID, subject, filter, expiry, udpSocket, out subscribeError);
        }

        public List<string> GetNotifications(string address, out string sessionID, out string sessionError)
        {
            return m_publisher.GetNotifications(address, out sessionID, out sessionError);
        }

        public bool IsNotificationReady(string address)
        {
            return m_publisher.IsNotificationReady(address);
        }

        public string ExtendSession(string address, string sessionID, int expiry)
        {
            return m_publisher.ExtendSession(address, sessionID, expiry);
        }

        public void CloseSession(string address, string sessionID)
        {
            m_publisher.CloseSession(address, sessionID);
        }

        public void CloseConnection(string address)
        {
            m_publisher.CloseConnection(address);
        }

        //public void RegisterListener(string address)
        //{
        //    m_publisher.RegisterListener(address);
        //}

        public void MonitorEventReceived(SIPMonitorEvent monitorEvent)
        {
            throw new NotImplementedException();
        }

        public void RegisterListener(string address, Action<string> notificationsReady)
        {
            throw new NotImplementedException();
        }
    }
}
