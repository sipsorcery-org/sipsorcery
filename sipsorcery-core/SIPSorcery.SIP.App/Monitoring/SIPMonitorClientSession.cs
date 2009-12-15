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
        private static readonly string m_topLevelAdminId = Customer.TOPLEVEL_ADMIN_ID;
        private static readonly string m_filterWildcard = SIPMonitorFilter.WILDCARD;

        private static ILog logger = AppState.logger;

        public Guid MonitorSessionID { get; private set; }
        public string CustomerUsername { get; private set; }
        public string AdminId { get; private set; }
        public string Address { get; private set; }
        public string MachineEventsSessionId { get; private set; }
        public string ControlEventsSessionId { get; private set; }
        public bool SubscribedForMachineEvents { get; set; }
        public SIPMonitorFilter ControlEventsFilter { get; private set; }
        public Queue<SIPMonitorEvent> MachineEvents = new Queue<SIPMonitorEvent>();
        public Queue<SIPMonitorEvent> ControlEvents = new Queue<SIPMonitorEvent>();
        public DateTime LastGetNotificationsRequest = DateTime.Now;
        public bool FilterDescriptionNotificationSent { get; set; }

        public SIPMonitorClientSession(string customerUsername, string adminId, string address)
        {
            MonitorSessionID = Guid.NewGuid();
            CustomerUsername = customerUsername;
            AdminId = adminId;
            Address = address;
        }

        public string SetFilter(string subject, string filter)
        {
            if (subject.IsNullOrBlank())
            {
                throw new ArgumentException("SetFilter must have a valid non-empty subject", "subject");
            }

            SIPMonitorClientTypesEnum clientType = SIPMonitorClientTypes.GetSIPMonitorClientType(subject);

            if (clientType == SIPMonitorClientTypesEnum.Machine)
            {
                if (filter.IsNullOrBlank())
                {
                    MachineEventsSessionId = null;
                    SubscribedForMachineEvents = false;
                    MachineEvents.Clear();
                    return null;
                }
                else
                {
                    if (MachineEventsSessionId == null)
                    {
                        MachineEventsSessionId = Guid.NewGuid().ToString();
                        SubscribedForMachineEvents = true;
                        MachineEvents.Clear();
                    }

                    return MachineEventsSessionId;
                }
            }
            else if (clientType == SIPMonitorClientTypesEnum.ControlClient)
            {
                if (filter.IsNullOrBlank())
                {
                    ControlEventsSessionId = null;
                    ControlEventsFilter = null;
                    ControlEvents.Clear();
                    return null;
                }
                else
                {
                    if (ControlEventsSessionId == null)
                    {
                        ControlEvents.Clear();  // The control filter may have changed so remove any pending events.
                        ControlEventsSessionId = Guid.NewGuid().ToString();
                    }

                    ControlEventsFilter = new SIPMonitorFilter(filter);

                    // If the filter request is for a full SIP trace the user field must not be used since it's
                    // tricky to decide which user a SIP message belongs to prior to authentication. If a full SIP
                    // trace is requested instead of a user filter a regex will be set that matches the username in
                    // the From or To header. If a full SIP trace is not specified then the user filer will be set.
                    if (AdminId != m_topLevelAdminId && ControlEventsFilter != null)
                    {
                        // If this user is not the top level admin there are tight restrictions on what filter can be set.
                        if (ControlEventsFilter.EventFilterDescr == "full")
                        {
                            ControlEventsFilter.RegexFilter = ":" + CustomerUsername + "@";
                            ControlEventsFilter.Username = m_filterWildcard;
                        }
                        else
                        {
                            ControlEventsFilter.Username = CustomerUsername;
                        }
                    }

                    return ControlEventsSessionId;
                }
            }

            throw new ApplicationException("SetFilter did not understand the subject type of " + subject + ".");
        }

        public void Close(string sessionID)
        {
            if (MachineEventsSessionId == sessionID)
            {
                logger.Debug("Closing machine events session for " + CustomerUsername + ".");
                MachineEventsSessionId = null;
                SubscribedForMachineEvents = false;
                MachineEvents.Clear();
            }
            else if (ControlEventsSessionId == sessionID)
            {
                logger.Debug("Closing control events session for " + CustomerUsername + ".");
                FilterDescriptionNotificationSent = false;
                ControlEventsSessionId = null;
                ControlEventsFilter = null;
                ControlEvents.Clear();
            }
        }
    }
}
