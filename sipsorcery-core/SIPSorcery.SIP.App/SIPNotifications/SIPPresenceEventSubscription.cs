using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.Persistence;

namespace SIPSorcery.SIP.App
{
    public class SIPPresenceEventSubscription : SIPEventSubscription
    {
        private const int MAX_SIPACCOUNTS_TO_RETRIEVE = 25;

        private static string m_wildcardUser = SIPMonitorFilter.WILDCARD;
        private static string m_contentType = SIPMIMETypes.PRESENCE_NOTIFY_CONTENT_TYPE;

        private SIPEventPresence Presence;

        private SIPAssetGetListDelegate<SIPAccount> GetSIPAccounts_External;
        private SIPAssetCountDelegate<SIPRegistrarBinding> GetSIPRegistrarBindingsCount_External;

        public override SIPEventPackage SubscriptionEventPackage
        {
            get { return SIPEventPackage.Presence; }
        }

        public override string MonitorFilter
        {
            get { return "presence " + ResourceURI.ToString(); }
        }

        public override string NotifyContentType
        {
            get { return m_contentType; }
        }

        public SIPPresenceEventSubscription(
            SIPMonitorLogDelegate log,
            string sessionID,
            SIPURI resourceURI,
            SIPURI canonincalResourceURI,
            string filter,
            SIPDialogue subscriptionDialogue,
            int expiry,
            SIPAssetGetListDelegate<SIPAccount> getSIPAccounts,
            SIPAssetCountDelegate<SIPRegistrarBinding> getBindingsCount
            )
            : base(log, sessionID, resourceURI, canonincalResourceURI, filter, subscriptionDialogue, expiry)
        {
            GetSIPAccounts_External = getSIPAccounts;
            GetSIPRegistrarBindingsCount_External = getBindingsCount;
            Presence = new SIPEventPresence(resourceURI);
        }

        public override void GetFullState()
        {
            try
            {
                List<SIPAccount> sipAccounts = null;

                if (ResourceURI.User == m_wildcardUser)
                {
                    sipAccounts = GetSIPAccounts_External(s => s.Owner == SubscriptionDialogue.Owner, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                }
                else
                {
                    sipAccounts = GetSIPAccounts_External(s => s.SIPUsername == CanonicalResourceURI.User && s.SIPDomain == CanonicalResourceURI.Host, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                }

                foreach (SIPAccount sipAccount in sipAccounts)
                {
                    SIPURI aor = SIPURI.ParseSIPURIRelaxed(sipAccount.SIPUsername + "@" + sipAccount.SIPDomain);

                    int bindingsCount = GetSIPRegistrarBindingsCount_External(b => b.SIPAccountId == sipAccount.Id);
                    if (bindingsCount > 0)
                    {
                        string safeSIPAccountID = sipAccount.Id.ToString();
                        Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.open, aor, Decimal.Zero));
                        //logger.Debug(" full presence " + aor.ToString() + " open.");
                    }
                    else
                    {
                        string safeSIPAccountID = sipAccount.Id.ToString();
                        Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.closed, null, Decimal.Zero));
                        //logger.Debug(" full presence " + aor.ToString() + " closed.");
                    }
                }

                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.NotifySent, "Full state notification for presence and " + ResourceURI.ToString() + ".", SubscriptionDialogue.Owner));
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPPresenceEventSubscription GetFullState. " + excp.Message);
            }
        }

        public override string GetNotifyBody()
        {
            return Presence.ToXMLText();
        }

        public override void AddMonitorEvent(SIPMonitorMachineEvent machineEvent)
        {
            try
            {
                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Monitor, "Monitor event " + machineEvent.MachineEventType + " presence " + machineEvent.ResourceURI.ToString() + " for subscription to " + ResourceURI.ToString() + ".", SubscriptionDialogue.Owner));

                string safeSIPAccountID = machineEvent.ResourceID;
                SIPURI sipAccountURI = machineEvent.ResourceURI;

                if (machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate)
                {
                    // A binding has been updated so there is at least one device online for the SIP account.
                    Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.open, sipAccountURI, Decimal.Zero));
                    //logger.Debug(" single presence open.");
                }
                else
                {
                    // A binding has been removed but there could still be others.
                    Guid sipAccountID = new Guid(machineEvent.ResourceID);
                    int bindingsCount = GetSIPRegistrarBindingsCount_External(b => b.SIPAccountId == sipAccountID);
                    if (bindingsCount > 0)
                    {
                        Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.open, sipAccountURI, Decimal.Zero));
                    }
                    else
                    {
                        Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.closed, sipAccountURI, Decimal.Zero));
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPresenceEventSubscription AddMonitorEvent. " + excp.Message);
                throw;
            }
        }

        public override void NotificationSent()
        {
            Presence.Tuples.Clear();
        }
    }
}
