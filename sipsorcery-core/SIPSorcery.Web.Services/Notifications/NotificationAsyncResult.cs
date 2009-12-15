using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Web;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using log4net;

namespace SIPSorcery.Web.Services
{

    public class NotificationAsyncResult : IAsyncResult
    {
        private static ILog logger = AppState.logger;

        ManualResetEvent waitHandle = new ManualResetEvent(true);

        public object AsyncState { get; set; }
        public WaitHandle AsyncWaitHandle { get { return this.waitHandle; } }
        public bool CompletedSynchronously { get { return true; } }
        public bool IsCompleted { get { return true; } }

        public NotificationData NotificationData { get; set; }
        public MakeConnection Poll { get; set; }
        public string SessionID { get; set; }
        public string SessionError { get; private set; }

        private GetNotificationsDelegate GetNotifications_External;
        private Action<string> RegisterListenerForWCF_External;
        private Action<string, Action<string>> RegisterListenerInProcess_External;
        private AsyncCallback m_callback;

        public NotificationAsyncResult(
            GetNotificationsDelegate getNotifications,
            Action<string> registerListenerWCF,
            Action<string, Action<string>> registerListenerInProcess,
            MakeConnection poll,
            AsyncCallback callback,
            object state)
        {
            GetNotifications_External = getNotifications;
            RegisterListenerForWCF_External = registerListenerWCF;
            RegisterListenerInProcess_External = registerListenerInProcess;
            this.Poll = poll;
            m_callback = callback;
            this.AsyncState = state;

            GetNotifications();
        }

        /// <summary>
        ///  Used for callbacks initiated by across a WCF channel.
        /// </summary>
        public void NotificationsReady()
        {
            //logger.Debug("NotificationsReady fired for " + Poll.Address + ".");
            GetNotifications();
        }

        /// <summary>
        /// Used for callbacks from an in process object.
        /// </summary>
        /// <param name="address"></param>
        public void NotificationsReady(string address)
        {
            //logger.Debug("NotificationsReady fired for " + Poll.Address + ".");
            GetNotifications();
        }

        public void GetNotifications()
        {
            NotificationData = new NotificationData();

            try
            {
                //logger.Debug("NotificationAsyncResult NotificationsReady.");

                string sessionID;
                string sessionError;
                List<string> notifications = GetNotifications_External(Poll.Address, out sessionID, out sessionError);

                if (sessionError != null)
                {
                    SessionError = sessionError;
                    SessionID = sessionID;

                    // Don't fire the callback, let the connection timeout to stop the really really stupid duplex channel
                    // from retrying.
                    //if (m_callback != null)
                    //{
                    //    m_callback(this);
                    //}
                }
                else if (notifications != null)
                {
                    // If there are pending events fire the callback.
                    SessionID = sessionID;

                    StringBuilder notificationsBuilder = new StringBuilder();
                    notifications.ForEach(s => notificationsBuilder.Append(s));
                    NotificationData.NotificationContent = notificationsBuilder.ToString();

                    //logger.Debug("NotificationAsyncResult notification retrieved for sessionID=" + SessionID + ".");

                    if (m_callback != null)
                    {
                        m_callback(this);
                    }
                }
                else
                {
                    //logger.Debug("NotificationAsyncResult no notifications available.");
                    // There are no pending events, let the publisher know there's an outstanding request and then wait for the callback.
                    //logger.Debug("Notification listener set for " + Poll.Address + ".");
                    if (RegisterListenerForWCF_External != null)
                    {
                        //logger.Debug("Monitor event publisher was a SIPMonitorPublisherProxy.");
                        RegisterListenerForWCF_External(Poll.Address);
                    }
                    else
                    {
                        RegisterListenerInProcess_External(Poll.Address, NotificationsReady);
                    }
                }
            }
            catch (Exception excp)
            {
                logger.Warn("Exception NotificationAsyncResult GetNotifications. " + excp.Message);
                SessionError = "Exception NotificationAsyncResult GetNotifications. " + excp.Message;

                if (m_callback != null)
                {
                    m_callback(this);
                }
            }
        }
    }
}
