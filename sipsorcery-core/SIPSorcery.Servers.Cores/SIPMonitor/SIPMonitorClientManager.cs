using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
        private const int MAX_EVENT_QUEUE_SIZE = 100;
        private const int REMOVE_EXPIRED_SESSIONS_NOREQUESTS = 30;         // A session will be closed if there are no notification requests within this period.

        private static ILog logger = AppState.logger;

        //private readonly string m_notificationsUsername = NotifierSubscriptionsManager.SUBSCRIPTIONS_MANAGER_USERNAME;

        private Dictionary<string, List<SIPMonitorClientSession>> m_clientSessions = new Dictionary<string, List<SIPMonitorClientSession>>();   // <Address, sessions>.
        //private Dictionary<string, Action<string>> m_clientsWaiting = new Dictionary<string, Action<string>>();                     // <Address> List of address endpoints that are waiting for a notification.
        private List<string> m_failedSubscriptions = new List<string>();
        private bool m_exit;
        private string m_monitorServerID;
        private UdpClient m_udpEventSender;

        public event Action<string> NotificationReady;
        public event Func<SIPMonitorEvent, bool> MonitorEventReady; 

        public SIPMonitorClientManager(string monitorServerID)
        {
            m_monitorServerID = monitorServerID;
            m_udpEventSender = new UdpClient();
            ThreadPool.QueueUserWorkItem(delegate { RemoveExpiredSessions(); });
        }

        public bool IsAlive()
        {
            return true;
        }

        public string Subscribe(string customerUsername, string adminId, string address, string sessionID, string subject, string filter, int expiry, string udpSocket, out string subscribeError)
        {
            subscribeError = null;

            try
            {
                logger.Debug("SIPMonitorClientManager subscribe for customer=" + customerUsername + ", adminID=" + adminId + ", subject=" + subject + ", filter=" + filter + ".");

                if (customerUsername.IsNullOrBlank() || address.IsNullOrBlank() || subject.IsNullOrBlank())
                {
                    throw new ArgumentException("Subscribe was passed a required parameter that was empty.");
                }

                DateTime? sessionEndTime = (expiry != 0) ? DateTime.Now.AddSeconds(expiry) : (DateTime?)null;
                SIPMonitorClientSession session = new SIPMonitorClientSession(customerUsername, adminId, address, sessionID, sessionEndTime, udpSocket);
                session.SetFilter(subject, filter);

                string endTime = (session.SessionEndTime != null) ? session.SessionEndTime.Value.ToString("HH:mm:ss") : "not set";
                logger.Debug("Filter set for customer=" + customerUsername + ", adminID=" + adminId + ", sessionID=" + sessionID + " as " + session.Filter.GetFilterDescription() + ", end time=" + endTime + ".");

                lock (m_clientSessions)
                {
                    if (!m_clientSessions.ContainsKey(session.Address))
                    {
                        m_clientSessions.Add(session.Address, new List<SIPMonitorClientSession>() { session });
                    }
                    else
                    {
                        m_clientSessions[session.Address].Add(session);
                    }
                }

                // A suscription can belong to a second session on the same address in which case there could already
                // be a pending notificatins request waiting.
                /*Action<string> notificationReady = null;

                lock (m_clientsWaiting)
                {
                    if (m_clientsWaiting.ContainsKey(address))
                    {
                        notificationReady = m_clientsWaiting[address];
                        m_clientsWaiting.Remove(address);
                    }
                }*/

                //if (notificationReady != null)
                //{
                //    logger.Debug("Subscribe Firing notification available callback for address " + address + ".");
                //    notificationReady(address);
                // }

                //logger.Debug("SIPMonitorClientManager subscribed " + session.Address + " " + session.CustomerUsername + ".");

                return sessionID;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPMonitorClientManager Subscribe (" + excp.GetType() + "). " + excp.Message);
                subscribeError = excp.Message;
                return null;
            }
        }

        public void MonitorEventReceived(SIPMonitorEvent monitorEvent)
        {
            try
            {
                monitorEvent.MonitorServerID = m_monitorServerID;
                //Action<string> notificationReady = null;
                //string notificationAddress = null;

                lock (m_clientSessions)
                {
                    foreach (KeyValuePair<string, List<SIPMonitorClientSession>> sessionEntry in m_clientSessions)
                    {
                        string address = sessionEntry.Key;
                        List<SIPMonitorClientSession> sessions = sessionEntry.Value;

                        bool eventAdded = false;

                        foreach (SIPMonitorClientSession session in sessions)
                        {
                            if (session.Filter.ShowSIPMonitorEvent(monitorEvent))
                            {
                                //logger.Debug("Session accepted event " + monitorEvent.ClientType + " for session " + monitorEvent.SessionID + ".");

                                monitorEvent.SessionID = session.SessionID;

                                if (session.UDPSocket != null)
                                {
                                    SendMonitorEventViaUDP(monitorEvent, session.UDPSocket);
                                }
                                else
                                {
                                    if (MonitorEventReady == null || !MonitorEventReady(monitorEvent))
                                    {
                                        lock (session.Events)
                                        {
                                            if (session.Events.Count > MAX_EVENT_QUEUE_SIZE)
                                            {
                                                // Queue has exceeded max allowed size, pop off the oldest event.
                                                session.Events.Dequeue();
                                            }
                                            session.Events.Enqueue(monitorEvent);
                                        }
                                        eventAdded = true;
                                    }
                                }
                            }
                        }

                        //if (eventAdded && m_clientsWaiting.ContainsKey(session.Address))
                        if (eventAdded)
                        {
                            //notificationAddress = session.Address;
                            //notificationReady = m_clientsWaiting[session.Address];

                            if (NotificationReady != null)
                            {
                                NotificationReady(address);
                            }
                        }
                    }
                }

                /*if (notificationReady != null)
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
                }*/
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

                /*lock (m_clientsWaiting)
                {
                    // If there is a callback set for this address the consumer has now received it and it can be removed.
                    if (m_clientsWaiting.ContainsKey(address))
                    {
                        m_clientsWaiting.Remove(address);
                    }
                }*/

                List<SIPMonitorClientSession> sessions = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
                if (sessions != null)
                {
                    sessions = sessions.OrderBy(s => s.LastGetNotificationsRequest).ToList();

                    for (int index = 0; index < sessions.Count; index++)
                    {
                        SIPMonitorClientSession session = sessions[index];
                        session.LastGetNotificationsRequest = DateTime.Now;

                        if (!session.FilterDescriptionNotificationSent && session.SessionType == SIPMonitorClientTypesEnum.Console)
                        {
                            //logger.Debug("First notifications request after new console client filter set.");
                            session.FilterDescriptionNotificationSent = true;
                            sessionId = session.SessionID;
                            SIPMonitorConsoleEvent filterDescriptionEvent = new SIPMonitorConsoleEvent(SIPMonitorServerTypesEnum.Monitor, SIPMonitorEventTypesEnum.Monitor, session.Filter.GetFilterDescription(), session.CustomerUsername);
                            return new List<string>() { filterDescriptionEvent.ToConsoleString(session.AdminId) };
                        }
                        else if (session.Events.Count > 0)
                        {
                            List<string> eventList = new List<string>();
                            sessionId = session.SessionID;
                            lock (session.Events)
                            {
                                while (session.Events.Count > 0)
                                {
                                    SIPMonitorEvent monitorEvent = session.Events.Dequeue();
                                    if (monitorEvent is SIPMonitorConsoleEvent)
                                    {
                                        eventList.Add(((SIPMonitorConsoleEvent)monitorEvent).ToConsoleString(session.AdminId));
                                    }
                                    else
                                    {
                                        eventList.Add(monitorEvent.ToCSV());
                                    }
                                }
                            }

                            return eventList;
                        }
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
                List<SIPMonitorClientSession> sessions = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
                if (sessions != null)
                {
                    foreach (SIPMonitorClientSession session in sessions)
                    {
                        if ((!session.FilterDescriptionNotificationSent && session.SessionType == SIPMonitorClientTypesEnum.Console) ||
                            session.Events.Count > 0)
                        {
                            return true;
                        }
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

        public string ExtendSession(string address, string sessionID, int expiry)
        {
            lock (m_clientSessions)
            {
                List<SIPMonitorClientSession> sessions = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
                if (sessions != null)
                {
                    for (int index = 0; index < sessions.Count; index++)
                    {
                        SIPMonitorClientSession session = sessions[index];

                        if (session.SessionID == sessionID)
                        {
                            session.LastGetNotificationsRequest = DateTime.Now;
                            session.SessionEndTime = (expiry != 0) ? DateTime.Now.AddSeconds(expiry) : (DateTime?)null;

                            string sessionEndTime = (session.SessionEndTime != null) ? session.SessionEndTime.Value.ToString("HH:mm:ss") : session.LastGetNotificationsRequest.AddSeconds(-1 * REMOVE_EXPIRED_SESSIONS_NOREQUESTS).ToString("HH:mm:ss");
                            logger.Debug("Extending session for customer=" + session.CustomerUsername + ", sessionID=" + sessionID + " to " + sessionEndTime + ".");
                        }
                    }
                }
                else
                {
                    return "Session was not found.";
                }
            }

            return null;
        }

        public void CloseSession(string address, string sessionID)
        {
            lock (m_clientSessions)
            {
                List<SIPMonitorClientSession> sessions = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
                if (sessions != null)
                {
                    for (int index = 0; index < sessions.Count; index++)
                    {
                        SIPMonitorClientSession session = sessions[index];

                        if (session.SessionID == sessionID)
                        {
                            logger.Debug("Closing session for customer=" + session.CustomerUsername + ", sessionID=" + sessionID + ".");

                            session.Close();

                            m_clientSessions[address].Remove(session);

                            if (m_clientSessions[address].Count == 0)
                            {
                                m_clientSessions.Remove(address);
                            }
                        }
                    }
                }
            }
        }

        public void CloseConnection(string address)
        {
            List<SIPMonitorClientSession> sessions = (m_clientSessions.ContainsKey(address)) ? m_clientSessions[address] : null;
            if (sessions != null && sessions.Count > 0)
            {
                logger.Debug("Closing all sessions for address=" + address + ".");

                for (int index = 0; index < sessions.Count; index++)
                {
                    CloseSession(address, sessions[index].SessionID);
                }
            }

            if (m_clientSessions.ContainsKey(address))
            {
                m_clientSessions.Remove(address);
            }
        }

        /*public void RegisterListener(string address)
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
        }*/

        /*public void RegisterListener(string address, Action<string> notificationsReady)
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
        }*/

        public void Stop()
        {
            m_exit = true;
        }

        /*private SIPMonitorClientSession GetSession(string customerUsername, string address)
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
        }*/

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
                            var expiredSessions = from sessions in m_clientSessions.Values
                                                    from session in sessions
                                                    where 
                                                    (session.SessionEndTime != null && session.SessionEndTime.Value < DateTime.Now) ||
                                                    (session.SessionEndTime == null && session.LastGetNotificationsRequest < DateTime.Now.AddSeconds(-1 * REMOVE_EXPIRED_SESSIONS_NOREQUESTS))
                                                    select session;

                            expiredSessions.ToList().ForEach((session) =>
                             {
                                 string sessionEndTime = (session.SessionEndTime != null) ? session.SessionEndTime.Value.ToString("HH:mm:ss") : session.LastGetNotificationsRequest.AddSeconds(-1 * REMOVE_EXPIRED_SESSIONS_NOREQUESTS).ToString("HH:mm:ss");
                                 logger.Debug("SIPMonitorClientManager removing inactive session connection for " + session.CustomerUsername + ", start time=" + session.SessionStartTime.ToString("HH:mm:ss") + ", end time=" + sessionEndTime + ".");
                                 m_clientSessions.Remove(session.Address);
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

        private void SendMonitorEventViaUDP(SIPMonitorEvent monitorEvent, string destinationSocket)
        {
            try
            {
                byte[] monitorEventBytes = Encoding.UTF8.GetBytes(monitorEvent.ToCSV());
                m_udpEventSender.Send(monitorEventBytes, monitorEventBytes.Length, IPSocket.ParseSocketString(destinationSocket));
            }
            catch (Exception excp)
            {
                logger.Error("Exception SendMonitorEventViaUDP. " + excp.Message);
            }
        }
    }
}
