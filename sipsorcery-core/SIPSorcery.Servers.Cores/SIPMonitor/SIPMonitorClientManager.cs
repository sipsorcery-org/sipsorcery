using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using log4net;

namespace SIPSorcery.Servers
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// Throwing exceptions within this classes methods will result in the WCF netTCP
    /// channel becoming faulted. That causes a problem because a callback channel (which
    /// allows this server class to call a method on the client) is also being used and
    /// if it is waiting will also end up in a faulted state.
    /// An elegant way needs to be found to handle a channel fault but for the time being
    /// it is deemed prudent not to throw them.
    /// </remarks>
    public class SIPMonitorClientManager : ISIPMonitorPublisher
    {
        private const string REMOVESESSIONS_THREAD_NAME = "sipmonitor-expiredsession";
        private const int REMOVE_EXPIRED_SESSIONS_PERIOD = 5000;
        private const int MAX_EVENT_QUEUE_SIZE = 50;
        private int SESSION_INACTIVITY_LIMIT = 180;             // The number of minutes of no notification requests that a session will be removed.

        private static ILog logger = AppState.logger;

        private Dictionary<string, SIPMonitorClientSession> m_clientSessions = new Dictionary<string, SIPMonitorClientSession>();   // <Address, sessions>.
        private Dictionary<string, Action<string>> m_clientsWaiting = new Dictionary<string, Action<string>>();                     // <Address> List of address endpoints that are waiting for a notification.
        private List<string> m_failedSubscriptions = new List<string>();
        private bool m_exit;

        public SIPMonitorClientManager()
        {
            ThreadPool.QueueUserWorkItem(delegate { RemoveExpiredSessions(); });
        }

        public string Subscribe(string customerUsername, string adminId, string address, string subject, string filter)
        {
            try
            {
                string sessionID = null;

                logger.Debug("SIPMonitorClientManager subscribe for customer=" + customerUsername + ", adminID=" + adminId + ", subject=" + subject + ", filter=" + filter + ".");

                if (customerUsername.IsNullOrBlank() || address.IsNullOrBlank() || subject.IsNullOrBlank())
                {
                    throw new ArgumentException("Subscribe was passed a required parameter that was empty.");
                }

                SIPMonitorClientSession session = GetSession(customerUsername, address) ?? new SIPMonitorClientSession(customerUsername, adminId, address);
                sessionID = session.SetFilter(subject, filter);

                if (session.ControlEventsFilter != null)
                {
                    logger.Debug("Filter set for customer=" + customerUsername + ", adminID=" + adminId + ", sessionID=" + sessionID + " as " + session.ControlEventsFilter.GetFilterDescription() + ".");
                }

                lock (m_clientSessions)
                {
                    if (!m_clientSessions.ContainsKey(session.Address))
                    {
                        m_clientSessions.Add(session.Address, session);
                    }
                }

                // A suscription can belong to a second session on the same address in which case there could already
                // be a pending notificatins request waiting.
                Action<string> notificationReady = null;

                lock (m_clientsWaiting)
                {
                    if (m_clientsWaiting.ContainsKey(address))
                    {
                        notificationReady = m_clientsWaiting[address];
                        m_clientsWaiting.Remove(address);
                    }
                }

                if (notificationReady != null)
                {
                    logger.Debug("Subscribe Firing notification available callback for address " + address + ".");
                    notificationReady(address);
                }

                //logger.Debug("SIPMonitorClientManager subscribed " + session.Address + " " + session.CustomerUsername + ".");

                return sessionID;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorClientManager Subscribe. " + excp.Message);
                //throw;
                return null;
            }
        }

        public void MonitorEventReceived(SIPMonitorEvent monitorEvent)
        {
            try
            {
                Action<string> notificationReady = null;
                string notificationAddress = null;

                lock (m_clientSessions)
                {
                    foreach (SIPMonitorClientSession session in m_clientSessions.Values)
                    {
                        bool eventAdded = false;
                        if (monitorEvent.GetType() == typeof(SIPMonitorMachineEvent))
                        {
                            if (session.SubscribedForMachineEvents && monitorEvent.Username == session.CustomerUsername)
                            {
                                //logger.Debug("SIPMonitorClientManager Enqueueing machine event for " + session.Address + ".");
                                lock (session.MachineEvents)
                                {
                                    if (session.MachineEvents.Count > MAX_EVENT_QUEUE_SIZE)
                                    {
                                        // Queue has exceeded max allowed size, pop off the oldest event.
                                        session.MachineEvents.Enqueue(monitorEvent);
                                    }
                                    session.MachineEvents.Enqueue(monitorEvent);
                                }
                                eventAdded = true;
                            }
                        }
                        else if (session.ControlEventsFilter != null && session.ControlEventsFilter.ShowSIPMonitorEvent(monitorEvent))
                        {
                            lock (session.ControlEvents)
                            {
                                if (session.ControlEvents.Count > MAX_EVENT_QUEUE_SIZE)
                                {
                                    // Queue has exceeded max allowed size, pop off the oldest event.
                                    session.ControlEvents.Dequeue();
                                }
                                session.ControlEvents.Enqueue(monitorEvent);
                            }
                            eventAdded = true;
                        }

                        if (eventAdded && m_clientsWaiting.ContainsKey(session.Address))
                        {
                            notificationAddress = session.Address;
                            notificationReady = m_clientsWaiting[session.Address];
                        }
                    }
                }

                if (notificationReady != null)
                {
                    //logger.Debug("SIPMonitorClientManager Firing notification available callback for address " + notificationAddress + ".");

                    try
                    {
                        notificationReady(notificationAddress);
                    }
                    catch (Exception callbackExcp)
                    {
                        logger.Error("Exception SIPMonitorClientManager MonitorEventReceived Callback. " + callbackExcp.Message);
                        lock (m_clientsWaiting)
                        {
                            m_clientsWaiting.Remove(notificationAddress);
                        }
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorClientManager MonitorEventReceived. " + excp.Message);
            }
        }

        public List<string> GetNotifications(string address, out string sessionId, out string sessionError)
        {
            sessionId = null;
            sessionError = null;

            try
            {
                //logger.Debug("SIPMonitorClientManager GetNotifications for address " + address + ".");

                lock (m_clientsWaiting)
                {
                    // If there is a callback set for this address the consumer has now received it and it can be removed.
                    if (m_clientsWaiting.ContainsKey(address))
                    {
                        m_clientsWaiting.Remove(address);
                    }
                }

                SIPMonitorClientSession session = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
                if (session != null)
                {
                    session.LastGetNotificationsRequest = DateTime.Now;

                    if (!session.FilterDescriptionNotificationSent && session.ControlEventsFilter != null)
                    {
                        //logger.Debug("First notifications request after new console client filter set.");
                        session.FilterDescriptionNotificationSent = true;
                        sessionId = session.ControlEventsSessionId;
                        SIPMonitorControlClientEvent filterDescriptionEvent = new SIPMonitorControlClientEvent(SIPMonitorServerTypesEnum.Monitor, SIPMonitorEventTypesEnum.Monitor, session.ControlEventsFilter.GetFilterDescription(), session.CustomerUsername);
                        return new List<string>() { filterDescriptionEvent.ToConsoleString(session.AdminId) };
                    }
                    else if (session.MachineEvents.Count > 0)
                    {
                        sessionId = session.MachineEventsSessionId;
                        List<string> eventList = new List<string>();
                        lock (session.MachineEvents)
                        {
                            while (session.MachineEvents.Count > 0)
                            {
                                eventList.Add(((SIPMonitorMachineEvent)session.MachineEvents.Dequeue()).ToCSV());
                            }
                        }
                        return eventList;
                    }
                    else if (session.ControlEvents.Count > 0)
                    {
                        sessionId = session.ControlEventsSessionId;
                        List<string> eventList = new List<string>();
                        lock (session.ControlEvents)
                        {
                            while (session.ControlEvents.Count > 0)
                            {
                                eventList.Add(((SIPMonitorControlClientEvent)session.ControlEvents.Dequeue()).ToConsoleString(session.AdminId));
                            }
                        }
                        return eventList;
                    }
                }

                return null;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorClientManager GetNotifications. " + excp.Message);
                sessionError = "Exception SIPMonitorClientManager GetNotifications. " + excp.Message;
                return null;
                //throw;
            }
        }

        public bool IsNotificationReady(string address)
        {
            try
            {
                SIPMonitorClientSession session = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
                if (session != null)
                {
                    if ((!session.FilterDescriptionNotificationSent && session.ControlEventsFilter != null) ||
                        session.MachineEvents.Count > 0 || session.ControlEvents.Count > 0)
                    {
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                return false;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorClientManager IsNotificationReady. " + excp.Message);
                return false;
            }
        }

        public void CloseSession(string address, string sessionID)
        {
            SIPMonitorClientSession connection = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
            if (connection != null)
            {
                logger.Debug("Closing session for customer=" + connection.CustomerUsername + ", sessionID=" + sessionID + ".");

                connection.Close(sessionID);
            }
        }

        public void CloseConnection(string address)
        {
            SIPMonitorClientSession connection = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
            if (connection != null)
            {
                logger.Debug("Closing connection for customer=" + connection.CustomerUsername + ", address=" + address + ".");

                lock (m_clientSessions)
                {
                    m_clientSessions.Remove(address);
                }
            }
        }

        public void RegisterListener(string address)
        {
            if (OperationContext.Current != null)
            {
                //logger.Debug("RegisterListener OperationContext is available.");
                ISIPMonitorNotificationReady notificationReadyClient = OperationContext.Current.GetCallbackChannel<ISIPMonitorNotificationReady>();
                RegisterListener(address, notificationReadyClient.NotificationReady);
            }
            else
            {
                throw new ApplicationException("SIPMonitorClientManager.RegisterListener was called without a callback function and with no available Operation Context.");
            }
        }

        public void RegisterListener(string address, Action<string> notificationsReady)
        {
            try
            {
                //logger.Debug("RegisterListener for address " + address + ".");

                if (notificationsReady == null)
                {
                    throw new ArgumentNullException("notificationsReady", "A callback function must be provided to register a listener.");
                }

                if (m_clientSessions.ContainsKey(address))
                {
                    lock (m_clientsWaiting)
                    {
                        if (!m_clientsWaiting.ContainsKey(address))
                        {
                            //logger.Debug("RegisterListener callback set.");
                            m_clientsWaiting.Add(address, notificationsReady);
                        }
                    }
                }
                else
                {
                    throw new ApplicationException("No current subscription for address " + address + " in RegisterListener.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception RegisterListener. " + excp.Message);
                //throw;
            }
        }

        public void Stop()
        {
            m_exit = true;
        }

        private SIPMonitorClientSession GetSession(string customerUsername, string address)
        {
            lock (m_clientSessions)
            {
                if (m_clientSessions.ContainsKey(address))
                {
                    if (m_clientSessions[address].CustomerUsername == customerUsername)
                    {
                        return m_clientSessions[address];
                    }
                    else
                    {
                        logger.Warn("Mismatch on address and customer username getting monitor client session, removing original session.");
                        m_clientSessions.Remove(address);
                    }
                }

                return null;
            }
        }

        private void RemoveExpiredSessions()
        {
            try
            {
                Thread.CurrentThread.Name = REMOVESESSIONS_THREAD_NAME;

                while (!m_exit)
                {
                    try
                    {
                        lock (m_clientSessions)
                        {
                            var expiredSessions = from session in m_clientSessions.Values
                                                  where session.LastGetNotificationsRequest < DateTime.Now.AddMinutes(-1 * SESSION_INACTIVITY_LIMIT)
                                                  select session;

                            expiredSessions.ToList().ForEach((session) =>
                             {
                                 logger.Debug("SIPMonitorClientManager Removing inactive session connection to " + session.CustomerUsername + ".");
                                 m_clientSessions.Remove(session.Address);
                                 m_clientsWaiting.Remove(session.Address);
                             });
                        }
                    }
                    catch (Exception removeExcp)
                    {
                        logger.Error("Exception SIPMonitorClientManager RemoveExpiredSessions. " + removeExcp.Message);
                    }

                    Thread.Sleep(REMOVE_EXPIRED_SESSIONS_PERIOD);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorClientManager RemoveExpiredSessions. " + excp.Message);
            }
        }
    }
}
