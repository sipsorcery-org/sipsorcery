using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;

namespace SIPSorcery.Silverlight.Messaging
{
    public class PollingDuplexClient
    {
        private const int BUFFER_SIZE = 8192;

        private Action<string> DebugMessage_External;
        private PubSubClient m_client;
        private string m_authID;
        private string m_serverURL;
        private string m_subject;
        private string m_filter;
        private Guid m_address;

        public MethodInvoker Closed;
        public MethodInvokerStrArg NotificationReceived;

        public PollingDuplexClient(Action<string> debugMessage, string authID, string serverURL)
        {
            DebugMessage_External = debugMessage;
            m_authID = authID;
            m_serverURL = serverURL;
            m_address = Guid.NewGuid();
        }

        public void Subscribe(string subject, string filter)
        {
            PollingDuplexHttpSecurityMode securitymode = (m_serverURL.StartsWith("https")) ? PollingDuplexHttpSecurityMode.Transport : PollingDuplexHttpSecurityMode.None;
            SIPSorcerySecurityHeader securityHeader = new SIPSorcerySecurityHeader(m_authID);
            PullNotificationHeader notificationsHeader = new PullNotificationHeader(m_address.ToString());
            SIPSorceryCustomHeader sipSorceryHeader = new SIPSorceryCustomHeader(new List<MessageHeader>() { securityHeader, notificationsHeader });
            PollingDuplexCustomHeaderBinding notifierBinding = new PollingDuplexCustomHeaderBinding(sipSorceryHeader, securitymode) { UseTextEncoding = true };
            m_client = new PubSubClient(notifierBinding, new EndpointAddress(new Uri(m_serverURL)));
            m_client.InnerChannel.Faulted += ChannelFaulted;
            m_client.InnerChannel.Closed += ChannelClosed;
            m_client.NotifyReceived += NotifyReceived;
            m_client.CloseSessionReceived += CloseSessionReceived;
            //DebugMessage_External("Polling Duplex client created, sessionID=" + m_client.InnerChannel.SessionId + ", timeout=" + m_client.InnerChannel.OperationTimeout.TotalSeconds + "s.");

            m_subject = subject;
            m_filter = filter;
            m_client.SubscribeAsync(m_subject, m_filter);
        }

        private void CloseSessionReceived(object sender, NotifyReceivedEventArgs e)
        {
            DebugMessage_External("CloseSessionReceived.");
            Close();
        }

        private void ChannelClosed(object sender, EventArgs e)
        {
            //LogActivityMessage(MessageLevelsEnum.Warn, "Machine Event Notifier channel closed.");
            Close();
        }

        public void Close()
        {
            try
            {
                if (m_client != null)
                {
                    try
                    {
                         m_client.SubscribeAsync(m_subject, null);
                    }
                    catch { }

                    m_client.CloseAsync();
                    m_client = null;

                    if (Closed != null)
                    {
                        Closed();
                    }
                }
            }
            catch
            {
                //LogActivityMessage(MessageLevelsEnum.Error, "Exception closing notification channel. " + excp.Message);
            }
        }

        private void NotifyReceived(object sender, NotifyReceivedEventArgs e)
        {
            try
            {
                if (e.Error == null)
                {
                    string notificationText = e.request.GetBody<NotificationData>().NotificationContent;

                    if (notificationText.IsNullOrBlank() || notificationText == NotificationData.NOTIFICATION_CLOSE_CONTENT)
                    {
                        DebugMessage_External("Blank or close notify message received=" + notificationText + ".");
                        //LogActivityMessage(MessageLevelsEnum.Warn, "Unknown error on machine notification session.");
                        Close();
                    }
                    /*else if (notificationText.StartsWith(m_closeSessionPrefix)) {
                        // This indicates an error has occurred creating the subscription and the session must be closed.
                        string errorMessage = notificationText.Replace(m_closeSessionPrefix, String.Empty);
                        LogActivityMessage(MessageLevelsEnum.Warn, "Error on machine notification session. " + errorMessage + ".");
                        CloseNotificationChannel(true);
                    }*/
                    else
                    {
                        if (NotificationReceived != null)
                        {
                            NotificationReceived(notificationText);
                        }

                        // Only machine events will be received on this channel.
                        //SIPMonitorEvent monitorEvent = SIPMonitorEvent.ParseEventCSV(notificationText);
                        //SIPEventMonitorClient_MonitorEventReceived((SIPMonitorMachineEvent)monitorEvent);
                    }
                }
                else
                {
                    //LogActivityMessage(MessageLevelsEnum.Error, "NotifyReceived Error: " + e.Error.Message);
                    Close();
                }
            }
            catch (Exception excp)
            {
                //LogActivityMessage(MessageLevelsEnum.Error, "Exception NotifyReceived. " + excp.Message);
                Close();
            }
        }

        private void ChannelFaulted(object sender, EventArgs e)
        {
            Close();
        }
    }
}
