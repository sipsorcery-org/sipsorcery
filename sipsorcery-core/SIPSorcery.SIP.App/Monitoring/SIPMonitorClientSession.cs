using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.CRM;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPMonitorClientSession
    {
        private int DEFAULT_EXPIRY = 300;             // The number of seconds of no notification requests that a session will be removed.

        private static readonly string m_topLevelAdminId = Customer.TOPLEVEL_ADMIN_ID;
        private static readonly string m_filterWildcard = SIPMonitorFilter.WILDCARD;

        private static ILog logger = AppState.logger;

        public string SessionID { get; private set; }
        public string CustomerUsername { get; private set; }
        public string AdminId { get; private set; }
        public string Address { get; private set; }
        public SIPMonitorClientTypesEnum SessionType;
        public SIPMonitorFilter Filter { get; private set; }
        public Queue<SIPMonitorEvent> Events = new Queue<SIPMonitorEvent>();
        public DateTime LastGetNotificationsRequest = DateTime.Now;
        public bool FilterDescriptionNotificationSent { get; set; }
        public int Expiry;

        public SIPMonitorClientSession(string customerUsername, string adminId, string address, int expiry)
        {
            SessionID = Guid.NewGuid().ToString();
            CustomerUsername = customerUsername;
            AdminId = adminId;
            Address = address;
            Expiry = expiry;

            if (Expiry <= 0)
            {
                Expiry = DEFAULT_EXPIRY;
            }
        }

        public string SetFilter(string subject, string filter)
        {
            if (subject.IsNullOrBlank())
            {
                throw new ArgumentException("SetFilter must have a valid non-empty subject", "subject");
            }

            SessionType = SIPMonitorClientTypes.GetSIPMonitorClientType(subject);
            Filter = new SIPMonitorFilter(filter);

            if (Filter != null)
            {
                // If the filter request is for a full SIP trace the user field must not be used since it's
                // tricky to decide which user a SIP message belongs to prior to authentication. If a full SIP
                // trace is requested instead of a user filter a regex will be set that matches the username in
                // the From or To header. If a full SIP trace is not specified then the user filer will be set.
                if (AdminId != m_topLevelAdminId)
                {
                    // If this user is not the top level admin there are tight restrictions on what filter can be set.
                    if (Filter.EventFilterDescr == "full")
                    {
                        Filter.RegexFilter = ":" + CustomerUsername + "@";
                        Filter.Username = m_filterWildcard;
                    }
                    else
                    {
                        Filter.Username = CustomerUsername;
                    }
                }

                return SessionID;
            }
            else
            {
                throw new ApplicationException("SetFilter did not understand the subject type of " + subject + ".");
            }
        }

        public void Close()
        {
            logger.Debug("Closing session " + SessionID + " for " + CustomerUsername + ".");
            Events.Clear();
        }
    }
}
