using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPEventSubscription
    {
        protected static ILog logger = AppState.logger;

        protected SIPMonitorLogDelegate MonitorLogEvent_External;

        public string SessionID { get; set; }
        public SIPURI ResourceURI { get; private set; }
        public SIPURI CanonicalResourceURI { get; private set; }
        public string Filter;
        public SIPDialogue SubscriptionDialogue { get; private set; }
        public DateTime LastSubscribe = DateTime.Now;
        public int Expiry;

        public virtual SIPEventPackage SubscriptionEventPackage
        {
            get { throw new NotImplementedException(); }
        }

        public virtual string NotifyContentType
        {
            get { throw new NotImplementedException(); }
        }

        public virtual string MonitorFilter
        {
            get { throw new NotImplementedException(); }
        }

        public SIPEventSubscription(
            SIPMonitorLogDelegate log,
            string sessionID,
            SIPURI resourceURI,
            SIPURI canonicalResourceURI,
            string filter,
            SIPDialogue subscriptionDialogue,
            int expiry)
        {
            MonitorLogEvent_External = log;
            SessionID = sessionID;
            ResourceURI = resourceURI;
            CanonicalResourceURI = canonicalResourceURI;
            Filter = filter;
            SubscriptionDialogue = subscriptionDialogue;
            Expiry = expiry;
        }

        public virtual void GetFullState()
        {
            throw new NotImplementedException();
        }

        public virtual bool AddMonitorEvent(SIPMonitorMachineEvent machineEvent)
        {
            throw new NotImplementedException();
        }

        public virtual void NotificationSent()
        {
            throw new NotImplementedException();
        }

        public virtual string GetNotifyBody()
        {
            throw new NotImplementedException();
        }
    }
}
