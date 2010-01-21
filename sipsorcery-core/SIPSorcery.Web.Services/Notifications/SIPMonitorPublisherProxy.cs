using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Web;
using SIPSorcery.Sys;
using SIPSorcery.SIP.App;
using log4net;

namespace SIPSorcery.Web.Services
{
    public interface ISIPMonitorPublisherChannel : ISIPMonitorPublisher, IDuplexChannel
    { }

    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, UseSynchronizationContext = false)]
    public partial class SIPMonitorPublisherProxy : DuplexClientBase<ISIPMonitorPublisher>, ISIPMonitorPublisher
    {
        private static ILog logger = AppState.logger;

        public SIPMonitorPublisherProxy(InstanceContext callbackInstance)
            : base(callbackInstance)
        { }

        public SIPMonitorPublisherProxy(InstanceContext callbackInstance, string endPointName)
            : base(callbackInstance, endPointName)
        { }

        public bool IsAlive()
        {
            return base.Channel.IsAlive();
        }

        public string Subscribe(string customerUsername, string adminId, string address, string subject, string topic, out string subscribeError)
        {
            return base.Channel.Subscribe(customerUsername, adminId, address, subject, topic, out subscribeError);
        }

        public List<string> GetNotifications(string address, out string sessionID, out string sessionError)
        {
            return base.Channel.GetNotifications(address, out sessionID, out sessionError);
        }

        public bool IsNotificationReady(string address)
        {
            return base.Channel.IsNotificationReady(address);
        }

        public void CloseSession(string address, string sessionID)
        {
            base.Channel.CloseSession(address, sessionID);
        }

        public void CloseConnection(string address)
        {
            base.Channel.CloseConnection(address);
        }

        public void RegisterListener(string address)
        {
            base.Channel.RegisterListener(address);
        }

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
