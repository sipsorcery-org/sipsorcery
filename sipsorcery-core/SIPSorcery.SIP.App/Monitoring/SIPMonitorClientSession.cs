using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.SIP.App
{
    public class SIPMonitorClientSession
    {
        private static readonly string m_topLevelAdminId = "*";
        private static readonly string m_filterWildcard = SIPMonitorFilter.WILDCARD;

        private static ILog logger = AppState.logger;

        public string Address { get; private set; }
        public string SessionID { get; private set; }
        public string CustomerUsername { get; private set; }
        public string AdminId { get; private set; }
        public SIPMonitorClientTypesEnum SessionType;
        public SIPMonitorFilter Filter { get; private set; }
        public Queue<SIPMonitorEvent> Events = new Queue<SIPMonitorEvent>();
        public DateTime LastGetNotificationsRequest = DateTime.Now;
        public bool FilterDescriptionNotificationSent { get; set; }
        public DateTime SessionStartTime;
        public DateTime? SessionEndTime;
        public string UDPSocket;

        public SIPMonitorClientSession(string customerUsername, string adminId, string address, string sessionID, DateTime? sessionEndTime, string udpSocket)
        {
            CustomerUsername = customerUsername;
            AdminId = adminId;
            Address = address;
            SessionID = sessionID;
            SessionStartTime = DateTime.Now;
            SessionEndTime = sessionEndTime;
            UDPSocket = udpSocket;
        }

        public string SetFilter(string subject, string filter)
        {
            if (subject.IsNullOrBlank())
            {
                throw new ArgumentException("SetFilter must have a valid non-empty subject", "subject");
            }

            SessionType = SIPMonitorClientTypes.GetSIPMonitorClientType(subject);
            Filter = new SIPMonitorFilter(filter);
            Filter.BaseType = SessionType.ToString().ToLower();

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
                else
                {
                    // If the administrator has not requested a filter on a specific user set the wildcard.
                    if (Filter.Username == null)
                    {
                        Filter.Username = m_filterWildcard;
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
