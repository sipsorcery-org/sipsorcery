using System;
using System.Net;
using System.Net.Sockets;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.Silverlight.Services;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public partial class MonitoringConsole : UserControl
    {
        private const int MAX_MONITORING_TEXT_LENGTH = 20000;

        private static readonly string m_defaultFilter = SIPSorceryNotificationClient.DEFAULT_FILTER;
        //private static readonly string m_closeSessionPrefix = NotificationData.CLOSE_SESSION_PREFIX;   // If a notification with this is received it means there has been an error and the notification connection must be closed.
        private readonly SolidColorBrush m_textBoxFocusedBackground = new SolidColorBrush(Colors.White);

        private ActivityMessageDelegate LogActivityMessage_External;

        public bool Initialised { get; private set; }

        private SIPSorceryNotificationClient m_sipNotifierClient;
        private string m_controlfilter;
        private string m_owner;
        private string m_authID;
        private string m_notificationsURL;

        private string m_monitorHost;
        private int m_monitorPort;
        private string m_monitorStatusMessage = "Initialising...";
        private ServiceConnectionStatesEnum m_monitorStatus = ServiceConnectionStatesEnum.Initialising;
        private bool m_monitorInitialisationInProgress;
        private bool m_isConnected = false;
        private Brush m_existingTextBoxBackground;
        private StringBuilder m_monitoringText = new StringBuilder();

        public MonitoringConsole()
        {
            InitializeComponent();
        }

        public MonitoringConsole(ActivityMessageDelegate logActivityMessage)
        {
            InitializeComponent();

            LogActivityMessage_External = logActivityMessage;

            UIHelper.SetVisibility(m_closeSocketButton, Visibility.Collapsed);
            UIHelper.SetVisibility(m_connectSocketButton, Visibility.Visible);
        }

        public void SetNotifierClient(SIPSorceryNotificationClient notificationsClient)
        {
            m_sipNotifierClient = notificationsClient;
            m_sipNotifierClient.ControlEventReceived += (eventStr) => { AppendMonitorText(eventStr); };
        }

        public void Close()
        {
            m_sipNotifierClient.CloseControlSession();
            ConsoleNotificationsClosed();
        }

        private void PollingClientDebugMessage(string message)
        {
            LogActivityMessage_External(MessageLevelsEnum.Monitor, message);
        }

        private void ConsoleNotificationsClosed()
        {
            m_isConnected = false;
            UIHelper.SetVisibility(m_closeSocketButton, Visibility.Collapsed);
            UIHelper.SetVisibility(m_connectSocketButton, Visibility.Visible);
            UIHelper.SetIsEnabled(m_commandEntryTextBox, true);
        }

        private void SIPConsoleClient_MonitorEventReceived(byte[] data, int bytesRead)
        {
            AppendMonitorText(Encoding.UTF8.GetString(data, 0, bytesRead));
        }

        private void CommandEntry_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                string command = m_commandEntryTextBox.Text;
                ConnectConsole(command);
            }
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            Close();
        }

        private void ConnectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_sipNotifierClient != null)
            {
                string filterText = m_commandEntryTextBox.Text;
                ConnectConsole(filterText);
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "A connection to the notification service has not been established. Console monitoring cannot be initiated.");
            }
        }

        private void ConnectConsole(string filterText)
        {
            try
            {
                if (filterText.IsNullOrBlank())
                {
                    filterText = m_defaultFilter;
                }

                SIPMonitorFilter filter = new SIPMonitorFilter(filterText);
                m_controlfilter = filterText.Trim();
                UIHelper.SetIsEnabled(m_commandEntryTextBox, false);
                LogActivityMessage_External(MessageLevelsEnum.Monitor, "Requesting notifications with filter=" + filterText.Trim() + " at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");

                UIHelper.SetVisibility(m_connectSocketButton, Visibility.Collapsed);
                UIHelper.SetVisibility(m_closeSocketButton, Visibility.Visible);
                m_sipNotifierClient.SetControlFilter(m_controlfilter);
            }
            catch (Exception filterExp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Warn, "Invalid filter. " + filterExp.Message);
                ConsoleNotificationsClosed();
            }
        }

        private void ClearButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_monitoringEventsTextBox.Text = String.Empty;
            m_monitoringText.Remove(0, m_monitoringText.Length);
        }

        private void AppendMonitorText(string message)
        {
            if (message == null || message.Trim().Length == 0)
            {
                return;
            }
            else if (this.Dispatcher.CheckAccess())
            {
                if (m_monitoringText.Length + message.Length > MAX_MONITORING_TEXT_LENGTH)
                {
                    m_monitoringText.Remove(0, message.Length);
                }

                m_monitoringText.Append(message);
                m_monitoringEventsTextBox.Text = m_monitoringText.ToString();
                m_monitoringEventsTextBox.Select(m_monitoringEventsTextBox.Text.Length, 1);
            }
            else
            {
                this.Dispatcher.BeginInvoke(new SetTextDelegate(AppendMonitorText), message);
            }
        }

        private void CommandEntryTextBox_LostFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            m_commandEntryTextBox.Background = m_commandEntryTextBox.Background;
        }

        private void CommandEntryTextBox_GotFocus(object sender, System.Windows.RoutedEventArgs e)
        {
            m_existingTextBoxBackground = m_commandEntryTextBox.Background;
            m_commandEntryTextBox.Background = m_textBoxFocusedBackground;
        }
    }
}