using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Channels;
using System.ServiceModel.Description;
using System.Text;
using System.Threading;
using SIPSorcery.CRM;
using SIPSorcery.Persistence;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, InstanceContextMode = InstanceContextMode.Single)]
    [CallbackBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, UseSynchronizationContext = false)]
    [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Allowed)]
    public class SIPNotifierService : IPubSub, ISIPMonitorNotificationReady, INotifications
    {
        private static string PULL_NOTIFICATION_POLL_PERIOD_APPSETTING_KEY = "PullNotificaitonPollPeriod";
        private static int MINIMUM_PULL_NOTIFICATION_POLL_PERIOD = 1000;

        private ILog logger = AppState.logger;

        private ISIPMonitorPublisher m_sipMonitorEventPublisher;
        private CustomerSessionManager m_customerSessionManager;
        private SIPMonitorPublisherProxy m_publisherWCFProxy;

        private int m_pullNotificationPollPeriod = MINIMUM_PULL_NOTIFICATION_POLL_PERIOD;
        private Dictionary<string, NotificationAsyncResult> m_pendingNotifications = new Dictionary<string, NotificationAsyncResult>();
        //private Dictionary<string, SIPNotifierClientSession> m_notifierSessions = new Dictionary<string, SIPNotifierClientSession>();   // List of subscriptions that were rejected and that need to be faulted when a connection attempt is made.

        public SIPNotifierService()
        {
            SIPSorceryConfiguration sipSorceryConfig = new SIPSorceryConfiguration();
            m_customerSessionManager = new CustomerSessionManager(sipSorceryConfig);
            InitialiseProxy();
        }

        public SIPNotifierService(ISIPMonitorPublisher sipMonitorPublisher, CustomerSessionManager customerSessionManager)
        {
            SIPSorceryConfiguration sipSorceryConfig = new SIPSorceryConfiguration();
            m_sipMonitorEventPublisher = sipMonitorPublisher;
            m_customerSessionManager = customerSessionManager;
        }

        private void Initialise(SIPSorceryConfiguration sipSorceryConfig)
        {
            string pollPeriodStr = sipSorceryConfig.GetAppSetting(PULL_NOTIFICATION_POLL_PERIOD_APPSETTING_KEY);
            if (!pollPeriodStr.IsNullOrBlank())
            {
                Int32.TryParse(pollPeriodStr, out m_pullNotificationPollPeriod);
                if (m_pullNotificationPollPeriod < MINIMUM_PULL_NOTIFICATION_POLL_PERIOD)
                {
                    m_pullNotificationPollPeriod = MINIMUM_PULL_NOTIFICATION_POLL_PERIOD;
                }
            }
        }

        private void InitialiseProxy()
        {
            InstanceContext callbackContext = new InstanceContext(this);
            m_publisherWCFProxy = new SIPMonitorPublisherProxy(callbackContext);
            ((ICommunicationObject)m_publisherWCFProxy).Faulted += PublisherWCFProxyChannelFaulted;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>This occurs when the channel to the SIP monitoring server that is publishing the events
        /// is faulted. This can occur if the SIP monitoring server is shutdown which will close the socket.</remarks>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void PublisherWCFProxyChannelFaulted(object sender, EventArgs e)
        {
            try
            {
                logger.Warn("SIPNotifierService received a fault on Publisher WCF proxy channel, comms state=" + m_publisherWCFProxy.State + ", " + m_publisherWCFProxy.InnerChannel.State + ".");

                try
                {
                    if (m_publisherWCFProxy.State != CommunicationState.Faulted)
                    {
                        try
                        {
                            m_publisherWCFProxy.Close();
                        }
                        catch (Exception closeExcp)
                        {
                            logger.Warn("Exception PublisherWCFProxyChannelFaulted Close. " + closeExcp.Message);
                            m_publisherWCFProxy.Abort();
                        }
                    }
                    else
                    {
                        m_publisherWCFProxy.Abort();
                    }
                }
                catch (Exception abortExcp)
                {
                    logger.Error("Exception PublisherWCFProxyChannelFaulted Abort. " + abortExcp.Message);
                }

                InstanceContext callbackContext = new InstanceContext(this);
                m_publisherWCFProxy = new SIPMonitorPublisherProxy(callbackContext);
                ((ICommunicationObject)m_publisherWCFProxy).Faulted += PublisherWCFProxyChannelFaulted;

                FlushPendingNotifications();
            }
            catch (Exception excp)
            {
                logger.Error("Exception PublisherWCFProxyChannelFaulted. " + excp.Message);
                throw;
            }
        }

        public void Subscribe(string topic)
        {
            //SIPNotifierClientSession notifierSession = null;
            string sessionID = null;

            try
            {
                PollingDuplexSession session = OperationContext.Current.GetPollingDuplexSession();
                sessionID = session.SessionId;

                /*lock (m_notifierSessions)
                {
                    if (m_notifierSessions.ContainsKey(session.Address))
                    {
                        notifierSession = m_notifierSessions[session.Address];
                        if (notifierSession.HasErroredSession)
                        {
                            // Not tkaing any further action for errored session.
                            notifierSession = null;
                            logger.Debug("Not taking any further action on errored connection " + session.Address + ".");
                        }
                        else
                        {
                            notifierSession.AddSessionID(session.SessionId);
                        }
                    }
                    else
                    {
                        notifierSession = new SIPNotifierClientSession(session);
                        m_notifierSessions.Add(session.Address, notifierSession);
                    }
                }*/

                Customer customer = AuthenticateRequest();
                if (customer != null)
                {
                    string customerUsername = customer.CustomerUsername;
                    string adminId = customer.AdminId;

                    logger.Debug("SIPNotifierService received Subscribe request for customer=" + customerUsername + ", adminid=" + adminId + " and filter=" + topic + ".");

                    GetPublisher().Subscribe(customerUsername, adminId, session.Address, session.SessionId, topic);
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPNotifierService Subscribe. " + excp);

                /*try
                {
                    if (notifierSession != null && sessionID != null)
                    {
                        notifierSession.SetSessionError(sessionID, excp.Message);

                        if (m_pendingNotifications.ContainsKey(notifierSession.Address))
                        {
                            m_pendingNotifications[notifierSession.Address].NotificationsReady();
                        }
                    }
                }
                catch (Exception failedSubExcp)
                {
                    logger.Error("Exception SIPNotifierService Subscribe processing failed subscription. " + failedSubExcp.Message);
                }*/
            }
        }

        public void Publish(string topic, string content)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginMakeConnect(MakeConnection poll, AsyncCallback callback, object state)
        {
            logger.Debug("BeginMakeConnect " + poll.Address + ".");
            NotificationAsyncResult notificationAR = new NotificationAsyncResult(
                GetNotificationsFromPublisher,
                RegisterListenerWithPublisher,
                RegisterListenerWithPublisher,
                poll,
                callback,
                state);

            lock (m_pendingNotifications)
            {
                if (m_pendingNotifications.ContainsKey(poll.Address))
                {
                    m_pendingNotifications.Remove(poll.Address);
                }
                m_pendingNotifications.Add(poll.Address, notificationAR);
            }

            return notificationAR;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// Throwing an exception in this method does not result in a message being sent to the client.
        /// </remarks>
        /// <param name="result"></param>
        /// <returns></returns>
        public Message EndMakeConnect(IAsyncResult result)
        {
            try
            {
                NotificationAsyncResult asyncResult = result as NotificationAsyncResult;
                if (asyncResult == null)
                {
                    throw new ArgumentException("Invalid async result", "result");
                }

                logger.Debug("EndMakeConnect sessionID=" + asyncResult.SessionID + ", address=" + asyncResult.Poll.Address + ".");

                lock (m_pendingNotifications)
                {
                    m_pendingNotifications.Remove(asyncResult.Poll.Address);
                }

                Message response = null;
                if (asyncResult.NotificationData != null)
                {
                    if (asyncResult.SessionError != null)
                    {
                        //logger.Debug("EndMakeConnect session error, sessionID=" + "faulting channel: " + asyncResult.SessionError + ".");
                        //response = Message.CreateMessage(MessageVersion.Default, NotificationData.CLOSE_ACTION, NotificationData.NOTIFICATION_CLOSE_CONTENT);
                        //response = asyncResult.Poll.PrepareResponse(response, asyncResult.SessionID);
                        logger.Debug("EndMakeConnect session error, sessionID=" + asyncResult.SessionID + "had a fault, " + asyncResult.SessionError + ", not responding to keep the client from retrying.");
                    }
                    else
                    {
                        response = Message.CreateMessage(MessageVersion.Default, NotificationData.NOTIFICATION_ACTION, asyncResult.NotificationData);
                        response = asyncResult.Poll.PrepareResponse(response, asyncResult.SessionID);
                    }
                }
                else
                {
                    response = asyncResult.Poll.CreateEmptyResponse();
                }

                return response;
            }
            catch (Exception excp)
            {
                logger.Error("Exception SIPNotifierService EndMakeConnect. " + excp.Message);
                return null;
                // throw;
            }
        }

        /// <summary>
        /// Callback method that will be used by the publisher to let this service know a notification
        /// is ready for a particular client connection.
        /// </summary>
        /// <param name="address">The address of the client whose notification is ready.</param>
        public void NotificationReady(string address)
        {
            try
            {
                logger.Debug("SIPNotifierService NotificationReady for " + address + ".");

                if (m_pendingNotifications.ContainsKey(address))
                {
                    m_pendingNotifications[address].NotificationsReady();
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception NotificationReady. " + excp.Message);
            }
        }

        public bool IsAlive()
        {
            return true;
        }

        public int GetPollPeriod()
        {
            return m_pullNotificationPollPeriod;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="filter"></param>
        /// <returns>If the set filter is successful a session ID otherwise null.</returns>
        public string Subscribe(string subject, string filter)
        {
            Customer customer = AuthenticateRequest();
            if (customer != null)
            {
                PullNotificationHeader notificationHeader = PullNotificationHeader.ParseHeader(OperationContext.Current);

                if (notificationHeader != null)
                {
                    string customerUsername = customer.CustomerUsername;
                    string adminId = customer.AdminId;

                    logger.Debug("SIPNotifierService received SetFilter request for customer=" + customerUsername +
                        ", adminid=" + adminId + ", subject=" + subject + ", filter=" + filter + ".");

                    return GetPublisher().Subscribe(customerUsername, adminId, notificationHeader.Address, subject, filter);
                }
                else
                {
                    throw new ApplicationException("The notification header was missing from the SetFilter request.");
                }
            }
            else
            {
                throw new UnauthorizedAccessException("The SetFilter request was not authorised, please re-login.");
            }
        }

        public void CloseSession(string sessionID)
        {
            PullNotificationHeader notificationHeader = PullNotificationHeader.ParseHeader(OperationContext.Current);

            if (notificationHeader != null)
            {
                GetPublisher().CloseSession(notificationHeader.Address, sessionID);
            }
            else
            {
                throw new ApplicationException("The notification header was missing from the CloseSession request.");
            }
        }

        public Dictionary<string, List<string>> GetNotifications()
        {
            try
            {
                PullNotificationHeader notificationHeader = PullNotificationHeader.ParseHeader(OperationContext.Current);

                if (notificationHeader != null)
                {
                    string sessionID;
                    string sessionError;

                    List<string> notifications = GetPublisher().GetNotifications(notificationHeader.Address, out sessionID, out sessionError);

                    if (sessionError != null)
                    {
                        throw new ApplicationException(sessionError);
                    }
                    else if (notifications == null || notifications.Count == 0)
                    {
                        return null;
                    }
                    else
                    {
                        return new Dictionary<string, List<string>>() { { sessionID, notifications } };
                    }
                }
                else
                {
                    throw new ApplicationException("The GetNotifications request was missing a required " + PullNotificationHeader.NOTIFICATION_HEADER_NAME + " header.");
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception GetNotifications. " + excp.Message);
                throw;
            }
        }

        public void CloseConnection()
        {
            PullNotificationHeader notificationHeader = PullNotificationHeader.ParseHeader(OperationContext.Current);

            if (notificationHeader != null)
            {
                GetPublisher().CloseConnection(notificationHeader.Address);
            }
            else
            {
                throw new ApplicationException("The notification header was missing from the Close request.");
            }
        }

        private void FlushPendingNotifications()
        {
            try
            {
                logger.Debug("Flushing " + m_pendingNotifications.Count + " pending notifications.");

                for (int index = 0; index < m_pendingNotifications.Count; index++)
                {
                    try
                    {
                        string pendingAddress = m_pendingNotifications.Keys.ToArray()[index];
                        if (m_pendingNotifications[pendingAddress] != null)
                        {
                            m_pendingNotifications[pendingAddress].NotificationsReady();
                        }
                    }
                    catch (Exception flushItemExcp)
                    {
                        logger.Error("Exception flushing pending notification. " + flushItemExcp.Message);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Error("Exception FlushPendingNotifications. " + excp.Message);
            }
        }

        private ISIPMonitorPublisher GetPublisher()
        {
            if (m_sipMonitorEventPublisher != null)
            {
                return m_sipMonitorEventPublisher;
            }
            else
            {
                return m_publisherWCFProxy;
            }
        }

        private List<string> GetNotificationsFromPublisher(string address, out string sessionID, out string sessionError)
        {
            try
            {
                //SIPNotifierClientSession notifierSession = (m_notifierSessions.ContainsKey(address)) ? m_notifierSessions[address] : null;

                //if (notifierSession != null && m_notifierSessions[address].IsBad())
                //{
                //    m_notifierSessions[address].GetFirstBadSession(out sessionID, out sessionError);
                //    return null;
                //}
                //else
                // {
                try
                {
                    return GetPublisher().GetNotifications(address, out sessionID, out sessionError);
                }
                catch (Exception excp)
                {
                    logger.Error("Exception SIPNotifierService. " + excp.Message);
                    sessionID = null; // (notifierSession != null && notifierSession.GetFirstSessionID() != null) ? notifierSession.GetFirstSessionID() : Guid.Empty.ToString();
                    sessionError = excp.Message;
                    return null;
                }
                //}
            }
            catch (FaultException faultExcp)
            {
                logger.Error("SIPNotifierService GetNotifications FaultException. " + faultExcp);
                throw;
            }
            catch (CommunicationException comExcp)
            {
                logger.Error("SIPNotifierService CommunicationException FaultException. " + comExcp);
                InitialiseProxy();
                throw;
            }
            catch (Exception excp)
            {
                logger.Error("SIPNotifierService CommunicationException Exception. " + excp);
                throw;
            }
        }

        private void RegisterListenerWithPublisher(string address)
        {
            try
            {
                GetPublisher().RegisterListener(address);
            }
            catch (FaultException faultExcp)
            {
                logger.Error("SIPNotifierService RegisterListener FaultException. " + faultExcp);
                throw;
            }
            catch (CommunicationException comExcp)
            {
                logger.Error("SIPNotifierService RegisterListener FaultException. " + comExcp);
                InitialiseProxy();
                throw;
            }
            catch (Exception excp)
            {
                logger.Error("SIPNotifierService RegisterListener Exception. " + excp);
                throw;
            }
        }

        /// <summary>
        /// This method is used only when the publisher is in the same process and a function delegate
        /// can be used as a callback.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="notificationsReady"></param>
        private void RegisterListenerWithPublisher(string address, Action<string> notificationsReady)
        {
            GetPublisher().RegisterListener(address, notificationsReady);
        }

        private Customer AuthenticateRequest()
        {
            Customer customer = null;

            SIPSorcerySecurityHeader securityheader = SIPSorcerySecurityHeader.ParseHeader(OperationContext.Current);
            if (securityheader == null)
            {
                throw new UnauthorizedAccessException("The security header was missing from the request.");
            }
            else
            {
                CustomerSession customerSession = m_customerSessionManager.Authenticate(securityheader.AuthID);

                if (customerSession != null && !customerSession.Expired)
                {
                    string sessionUsername = customerSession.CustomerUsername;
                    customer = m_customerSessionManager.CustomerPersistor.Get(c => c.CustomerUsername == sessionUsername);
                }
            }

            return customer;
        }
    }
}
