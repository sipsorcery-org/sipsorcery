using System;
using System.Net;
using System.Net.Sockets;
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
using SIPSorcery.SIP.App;
using SIPSorcery.Sockets;

namespace SIPSorcery
{
	public partial class MonitoringConsole : UserControl
	{
        private const int MAX_MONITORING_TEXT_LENGTH = 20000;

        private readonly SolidColorBrush m_textBoxFocusedBackground = new SolidColorBrush(Colors.White);

        private ActivityMessageDelegate LogActivityMessage_External;

        public bool Initialised { get; private set; }

        private SocketClient m_sipConsoleClient;
        private DnsEndPoint m_sipConsoleEndPoint;
        private string m_owner;

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
            InitializeComponent();;
        }

        public MonitoringConsole(
            ActivityMessageDelegate logActivityMessage, 
            string owner, 
            string host, 
            int port)
		{
			InitializeComponent();

            LogActivityMessage_External = logActivityMessage;
            m_owner = owner;
            m_monitorHost = host;
            m_monitorPort = port;
            
            m_sipConsoleEndPoint = new DnsEndPoint(m_monitorHost, m_monitorPort);
            m_sipConsoleClient = new SocketClient(m_sipConsoleEndPoint);
            m_sipConsoleClient.SocketDataReceived += new SocketDataReceivedDelegate(SIPConsoleClient_MonitorEventReceived);
            m_sipConsoleClient.SocketConnectionChange += new SocketConnectionChangeDelegate(SIPConsoleClient_MonitorConnectionChange);

            UIHelper.SetVisibility(m_closeSocketButton, Visibility.Collapsed);
            UIHelper.SetVisibility(m_connectSocketButton, Visibility.Visible);
		}

        public void Close()
        {
            try
            {
                if (m_isConnected)
                {
                    m_sipConsoleClient.Close();
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception Close DialPlanManager. " + excp.Message);
            }
        }

        private void ConnectSocket(object state)
        {

#if !BLEND
            LogActivityMessage_External(MessageLevelsEnum.Info, "Attempting to connect to " + m_monitorHost + ":" + m_monitorPort + ".");
            m_sipConsoleClient.ConnectAsync();
#else
            LogActivityMessage_External(MessageLevelsEnum.Warn, "The monitor console is disabled in this test version.");
#endif

        }

        private void SIPConsoleClient_MonitorConnectionChange(SocketConnectionStatus connectionState)
        {
            m_monitorStatusMessage = connectionState.Message;
            m_monitorStatus = connectionState.ConnectionStatus;
            m_monitorInitialisationInProgress = false;

            if (m_monitorStatus == ServiceConnectionStatesEnum.Ok)
            {
                LogActivityMessage_External(MessageLevelsEnum.Info, "Connection to " + m_sipConsoleEndPoint + " established.");
                UIHelper.SetVisibility(m_closeSocketButton, Visibility.Visible);
                UIHelper.SetVisibility(m_connectSocketButton, Visibility.Collapsed);
                m_isConnected = true;
            }
            else
            {
                LogActivityMessage_External(MessageLevelsEnum.Info, "Connection to " + m_sipConsoleEndPoint + " closed.");
                UIHelper.SetVisibility(m_closeSocketButton, Visibility.Collapsed);
                UIHelper.SetVisibility(m_connectSocketButton, Visibility.Visible);
                m_isConnected = false;
            }
        }

        private void SIPConsoleClient_MonitorEventReceived(byte[] data, int bytesRead)
        {
            AppendMonitorText(Encoding.UTF8.GetString(data, 0, bytesRead));
        }

        private void CommandEntry_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (e.Key == Key.Enter)
                {
                    string command = m_commandEntryTextBox.Text;

                    if (command != null && command.Trim().Length > 0)
                    {
                        m_commandEntryTextBox.Text = String.Empty;
                        AppendMonitorText(command);
                        
                        byte[] sendBuffer = Encoding.UTF8.GetBytes(command + "\n");
                        m_sipConsoleClient.Send(sendBuffer);
                    }
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception Socket Send. " + excp.Message);
            }
        }

        private void CloseButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (m_isConnected)
            {
                try
                {
                    m_sipConsoleClient.Close();
                }
                catch (Exception excp)
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "Exception CloseSocket. " + excp.Message);
                }
                finally
                {
                    m_isConnected = false;
                    UIHelper.SetVisibility(m_closeSocketButton, Visibility.Collapsed);
                    UIHelper.SetVisibility(m_connectSocketButton, Visibility.Visible);
                }
            }
        }

        private void ConnectButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!m_isConnected)
            {
                UIHelper.SetVisibility(m_connectSocketButton, Visibility.Collapsed);
                ThreadPool.QueueUserWorkItem(new WaitCallback(ConnectSocket), null);
            }
        }

        private void ClearButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            m_monitoringEventsTextBox.Text = String.Empty;
            m_monitoringText.Remove(0, m_monitoringText.Length);
        }
           
        private void AppendMonitorText(string message)
        {
            if(message == null || message.Trim().Length == 0)
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