using System;
using System.Collections.Generic;

namespace SIPSorcery.SIP.App
{
    public delegate int SIPRegistraBdingsCountDelegate(Guid sipAccountID);

    public delegate object SIPAssetGetPropertyByIdDelegate<T>(Guid id, string propertyName);

    public class SIPPresenceEventSubscription : SIPEventSubscription
    {
        private const int MAX_SIPACCOUNTS_TO_RETRIEVE = 25;
        public const string SWITCHBOARD_FILTER = "switchboard";    // If a client specifies this value as a filter it's only interested in SIP accounts that are switchboard enabled.

        private static string m_wildcardUser = SIPMonitorFilter.WILDCARD;
        private static string m_contentType = SIPMIMETypes.PRESENCE_NOTIFY_CONTENT_TYPE;

        private SIPEventPresence Presence;

        private SIPRegistraBdingsCountDelegate GetSIPRegistrarBindingsCount_External;
        private GetSIPAccountListDelegate GetSIPAccounts_External;
        private SIPAssetGetPropertyByIdDelegate<SIPAccount> GetSipAccountProperty_External;

        private bool m_switchboardSIPAccountsOnly;      // If true means this subscription should only generate notifications for SIP accounts that are switchboard enabled.

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
            //SIPAssetPersistor<SIPAccount> sipAccountPersistor,
            GetSIPAccountListDelegate getSipAccountsExternal,
            SIPAssetGetPropertyByIdDelegate<SIPAccount> getSipAccountPropertyExternal,
            SIPRegistraBdingsCountDelegate getBindingsCount,
            bool switchboardSIPAccountsOnly
            )
            : base(log, sessionID, resourceURI, canonincalResourceURI, filter, subscriptionDialogue, expiry)
        {
            //m_sipAccountPersistor = sipAccountPersistor;
            GetSIPAccounts_External = getSipAccountsExternal;
            GetSipAccountProperty_External = getSipAccountPropertyExternal;
            GetSIPRegistrarBindingsCount_External = getBindingsCount;
            Presence = new SIPEventPresence(resourceURI);
            m_switchboardSIPAccountsOnly = switchboardSIPAccountsOnly;
        }

        public override void GetFullState()
        {
            try
            {
                List<SIPAccount> sipAccounts = null;

                if (ResourceURI.User == m_wildcardUser)
                {
                    if (m_switchboardSIPAccountsOnly)
                    {
                        //sipAccounts = m_sipAccountPersistor.Get(s => s.Owner == SubscriptionDialogue.Owner && s.IsSwitchboardEnabled, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                        sipAccounts = GetSIPAccounts_External(s => s.Owner == SubscriptionDialogue.Owner && s.IsSwitchboardEnabled, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                    }
                    else
                    {
                        //sipAccounts = m_sipAccountPersistor.Get(s => s.Owner == SubscriptionDialogue.Owner, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                        sipAccounts = GetSIPAccounts_External(s => s.Owner == SubscriptionDialogue.Owner, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                    }
                }
                else
                {
                    if (m_switchboardSIPAccountsOnly)
                    {
                        //sipAccounts = m_sipAccountPersistor.Get(s => s.SIPUsername == CanonicalResourceURI.User && s.SIPDomain == CanonicalResourceURI.Host && s.IsSwitchboardEnabled, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                        sipAccounts = GetSIPAccounts_External(s => s.SIPUsername == CanonicalResourceURI.User && s.SIPDomain == CanonicalResourceURI.Host && s.IsSwitchboardEnabled, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                    }
                    else
                    {
                        //sipAccounts = m_sipAccountPersistor.Get(s => s.SIPUsername == CanonicalResourceURI.User && s.SIPDomain == CanonicalResourceURI.Host, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                        sipAccounts = GetSIPAccounts_External(s => s.SIPUsername == CanonicalResourceURI.User && s.SIPDomain == CanonicalResourceURI.Host, "SIPUsername", 0, MAX_SIPACCOUNTS_TO_RETRIEVE);
                    }
                }

                foreach (SIPAccount sipAccount in sipAccounts)
                {
                    SIPURI aor = SIPURI.ParseSIPURIRelaxed(sipAccount.SIPUsername + "@" + sipAccount.SIPDomain);

                    int bindingsCount = GetSIPRegistrarBindingsCount_External(sipAccount.Id);
                    if (bindingsCount > 0)
                    {
                        string safeSIPAccountID = sipAccount.Id.ToString();
                        Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.open, aor, Decimal.Zero, sipAccount.AvatarURL));
                        //logger.Debug(" full presence " + aor.ToString() + " open.");
                    }
                    else
                    {
                        string safeSIPAccountID = sipAccount.Id.ToString();
                        Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.closed, null, Decimal.Zero, sipAccount.AvatarURL));
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

        /// <summary>
        /// Checks and where required adds a presence related monitor event to the list of pending notifications.
        /// </summary>
        /// <param name="machineEvent">The monitor event that has been received.</param>
        /// <returns>True if a notification needs to be sent as a result of this monitor event, false otherwise.</returns>
        public override bool AddMonitorEvent(SIPMonitorMachineEvent machineEvent)
        {
            try
            {
                MonitorLogEvent_External(new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Notifier, SIPMonitorEventTypesEnum.Monitor, "Monitor event " + machineEvent.MachineEventType + " presence " + machineEvent.ResourceURI.ToString() + " for subscription to " + ResourceURI.ToString() + ".", SubscriptionDialogue.Owner));

                string safeSIPAccountID = machineEvent.ResourceID;
                SIPURI sipAccountURI = machineEvent.ResourceURI;
                bool sendNotificationForEvent = true;
                string avatarURL = null;

                if (m_switchboardSIPAccountsOnly)
                {
                    // Need to check whether the SIP account is switchboard enabled before forwarding the notification.
                    Guid sipAccountID = new Guid(machineEvent.ResourceID);
                    //sendNotificationForEvent = Convert.ToBoolean(m_sipAccountPersistor.GetProperty(sipAccountID, "IsSwitchboardEnabled"));
                    sendNotificationForEvent = Convert.ToBoolean(GetSipAccountProperty_External(sipAccountID, "IsSwitchboardEnabled"));
                    
                    if (sendNotificationForEvent)
                    {
                        //avatarURL = m_sipAccountPersistor.GetProperty(sipAccountID, "AvatarURL") as string;
                        avatarURL = GetSipAccountProperty_External(sipAccountID, "AvatarURL") as string;
                    }
                }

                if (sendNotificationForEvent)
                {
                    if (machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate)
                    {
                        // A binding has been updated so there is at least one device online for the SIP account.
                        Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.open, sipAccountURI, Decimal.Zero, avatarURL));
                        //logger.Debug(" single presence open.");
                    }
                    else
                    {
                        // A binding has been removed but there could still be others.
                        Guid sipAccountID = new Guid(machineEvent.ResourceID);
                        int bindingsCount = GetSIPRegistrarBindingsCount_External(sipAccountID);
                        if (bindingsCount > 0)
                        {
                            Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.open, sipAccountURI, Decimal.Zero, avatarURL));
                        }
                        else
                        {
                            Presence.Tuples.Add(new SIPEventPresenceTuple(safeSIPAccountID, SIPEventPresenceStateEnum.closed, sipAccountURI, Decimal.Zero, avatarURL));
                        }
                    }

                    return true;
                }
                else
                {
                    return false;
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
