using System;
using System.Collections.Generic;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using SIPSorcery.SIPSorceryNotificationService;
using SIPSorcery.Silverlight;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using System.Threading;

namespace SIPSorcery.Silverlight.Services
{
    public class SIPSorceryNotificationClient
    {
        private const int MINIMUM_POLL_PERIOD = 1000;
        private const int POST_FAILURE_POLL_PERIOD = 10000; // If the poll returns an error this is the new period until it is successfuly again.
        public const string DEFAULT_FILTER = "event *";
        private const int POLL_RECHECK_COUNT = 1000;

        private ActivityMessageDelegate LogActivityMessage_External;
        private NotificationsClient m_notificationClient;
        private int m_pollPeriod = MINIMUM_POLL_PERIOD;
        private string m_address;
        private string m_machineEventSessionID;
        private string m_controlEventSessionID;
        private bool m_pollRequestHadError;
        private bool m_exit;

        public event SIPMonitorMachineEventReceivedDelegate MachineEventReceived;
        public event Action<string> ControlEventReceived;
        public event ServiceStatusChangeDelegate StatusChanged;

        private bool m_isConnected;
        public bool IsConnected
        {
            get { return m_isConnected;}
        }

        public SIPSorceryNotificationClient(ActivityMessageDelegate logActivityMessage, string serverURL, string authid)
        {
            LogActivityMessage_External = logActivityMessage;
            m_address = Guid.NewGuid().ToString();
            BasicHttpSecurityMode securitymode = (serverURL.StartsWith("https")) ? BasicHttpSecurityMode.Transport : BasicHttpSecurityMode.None;
            SIPSorcerySecurityHeader securityHeader = new SIPSorcerySecurityHeader(authid);
            PullNotificationHeader notificationHeader = new PullNotificationHeader(m_address);
            SIPSorceryCustomHeader sipSorceryHeader = new SIPSorceryCustomHeader(new List<MessageHeader>() { securityHeader, notificationHeader });
            BasicHttpCustomHeaderBinding binding = new BasicHttpCustomHeaderBinding(sipSorceryHeader, securitymode);

            EndpointAddress address = new EndpointAddress(serverURL);
            m_notificationClient = new NotificationsClient(binding, address);

            m_notificationClient.IsAliveCompleted += IsAliveCompleted;
            m_notificationClient.GetPollPeriodCompleted += GetPollPeriodCompleted;
            m_notificationClient.SubscribeCompleted += SubscribeCompleted;
            m_notificationClient.GetNotificationsCompleted += GetNotificationsCompleted;
        }

        public void Connect()
        {
            m_notificationClient.IsAliveAsync();
        }

        public void Close()
        {
            if (m_machineEventSessionID != null || m_controlEventSessionID != null)
            {
                m_notificationClient.CloseConnectionAsync();
            }

            m_exit = true;
        }

        public void SetControlFilter(string filter)
        {
            if (m_isConnected)
            {
                Subscribe(SIPMonitorClientTypesEnum.Console.ToString(), filter);
            }
            else
            {
                throw new ApplicationException("The control filter cannot be set without the notifier server being connected.");
            }
        }

        public void CloseControlSession()
        {
            m_notificationClient.CloseSessionAsync(m_controlEventSessionID);
        }

        private void Poll()
        {
            try
            {
                int count = 0;
                Thread.Sleep(m_pollPeriod);

                while (!m_exit)
                {
                    m_notificationClient.GetNotificationsAsync();
                    count++;

                    if (!m_pollRequestHadError)
                    {
                        if (count % POLL_RECHECK_COUNT == 0)
                        {
                            m_notificationClient.GetPollPeriodAsync();
                        }

                        Thread.Sleep(m_pollPeriod);
                    }
                    else
                    {
                        Thread.Sleep(POST_FAILURE_POLL_PERIOD);
                    }
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception polling notifications service. " + excp.Message);
                m_isConnected = false;
            }
        }

        private void IsAliveAsync()
        {
            m_notificationClient.IsAliveAsync();
        }

        private void IsAliveCompleted(object sender, IsAliveCompletedEventArgs e)
        {
            if (e.Error == null)
            {
                //LogActivityMessage_External(MessageLevelsEnum.Monitor, "Notifications service connected.");
                if (StatusChanged != null)
                {
                    StatusChanged(ServiceConnectionStatesEnum.Ok, null);
                }

                m_notificationClient.GetPollPeriodAsync();
                m_notificationClient.SubscribeAsync(SIPMonitorClientTypesEnum.Machine.ToString(), DEFAULT_FILTER);
            }
            else
            {
                if (StatusChanged != null)
                {
                    StatusChanged(ServiceConnectionStatesEnum.Error, "Error contacting notifications service. " + e.Error.Message);
                }
            }
        }

        private void GetPollPeriodCompleted(object sender, GetPollPeriodCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                m_pollPeriod = e.Result;
                if (m_pollPeriod < MINIMUM_POLL_PERIOD)
                {
                    m_pollPeriod = MINIMUM_POLL_PERIOD;
                }
            }
        }

        private void Subscribe(string subject, string filter)
        {
            m_notificationClient.SubscribeAsync(subject, filter);
        }

        private void SubscribeCompleted(object sender, SubscribeCompletedEventArgs e)
        {
            if (e.Error == null)
            {
                if (!m_isConnected)
                {
                    m_machineEventSessionID = e.Result;
                    ThreadPool.QueueUserWorkItem(delegate { Poll(); });
                    m_isConnected = true;
                }
                else
                {
                    m_controlEventSessionID = e.Result;
                }
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "Error subscribing for notifications. " + e.Error.Message);
            }
        }

        private void GetNotificationsCompleted(object sender, GetNotificationsCompletedEventArgs e)
        {
            if (e.Error == null)
            {
                if (m_pollRequestHadError)
                {
                    m_pollRequestHadError = false;

                    if (StatusChanged != null)
                    {
                        StatusChanged(ServiceConnectionStatesEnum.Ok, null);
                    }
                }

                if (e.Result != null && e.Result.Count > 0)
                {
                    foreach (KeyValuePair<string, List<string>> notificationSet in e.Result)
                    {
                        if (notificationSet.Key == m_machineEventSessionID && MachineEventReceived != null)
                        {
                            foreach (string machineEventStr in notificationSet.Value)
                            {
                                MachineEventReceived(SIPMonitorMachineEvent.ParseMachineEventCSV(machineEventStr));
                            }
                        }
                        else if (notificationSet.Key == m_controlEventSessionID && ControlEventReceived != null)
                        {
                            foreach (string controlEventStr in notificationSet.Value)
                            {
                                ControlEventReceived(controlEventStr);
                            }
                        }
                    }
                }
            }
            else
            {
                if (!m_pollRequestHadError)
                {
                    m_pollRequestHadError = true;
                    //LogActivityMessage_External(MessageLevelsEnum.Warn, "Error retrieving notifications. " + e.Error.Message);
                    
                    if (StatusChanged != null)
                    {
                        StatusChanged(ServiceConnectionStatesEnum.Error, "Error retrieving notifications. " + e.Error.Message);
                    }
                }
            }
        }
    }
}
