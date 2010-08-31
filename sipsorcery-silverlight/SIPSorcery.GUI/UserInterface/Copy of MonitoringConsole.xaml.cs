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

namespace SIPSorcery
{
	public partial class MonitoringConsole : UserControl
	{
        private const int MAX_SOCKET_BUFFER_SIZE = 4096;        // Max amount of data that can be recived from the socket on a single read.
        private const int MAX_MONITORING_TEXT_LENGTH = 20000;   // Maximum length of the string that will be displayed in the monitoring text box.

        private readonly SolidColorBrush m_textBoxFocusedBackground = new SolidColorBrush(Colors.White);

        private ActivityMessageDelegate LogActivityMessage_External;

        public bool Initialised { get; private set; }

        private Socket m_socket;
        private byte[] m_socketBuffer = new byte[MAX_SOCKET_BUFFER_SIZE];
        private string m_owner;

        private string m_monitorHost;
        private int m_monitorPort;
        private bool m_isConnected = false;
        private StringBuilder m_monitoringText = new StringBuilder(MAX_MONITORING_TEXT_LENGTH);
        private Brush m_existingTextBoxBackground;

        public MonitoringConsole()
        {
            InitializeComponent();
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

            m_closeSocketButton.Visibility = Visibility.Visible;
		}

        private void ConnectSocket(object state)
        {

#if !BLEND

            DnsEndPoint ep = new DnsEndPoint(m_monitorHost, m_monitorPort);
            m_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            SocketAsyncEventArgs socketConnectionArgs = new SocketAsyncEventArgs();
            socketConnectionArgs.UserToken = m_socket.RemoteEndPoint;
            socketConnectionArgs.RemoteEndPoint = ep;
            socketConnectionArgs.Completed += SocketConnect_Completed;

            LogActivityMessage_External(MessageLevelsEnum.Info, "Attempting to connect to " + m_monitorHost + ":" + m_monitorPort + ".");

            m_socket.ConnectAsync(socketConnectionArgs);
#else
            LogActivityMessage_External(MessageLevelsEnum.Warn, "The monitor console is disabled in this test version.");
#endif

        }

        private void SocketConnect_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                m_isConnected = (e.SocketError == SocketError.Success);

                if (m_isConnected)
                {
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Successfully connected to " + m_monitorHost + ":" + m_monitorPort + ".");
                    SetButtonContent(m_closeSocketButton, "Close");
                    UIHelper.SetVisibility(m_closeSocketButton, Visibility.Visible);

                    SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                    receiveArgs.SetBuffer(m_socketBuffer, 0, MAX_SOCKET_BUFFER_SIZE);
                    receiveArgs.Completed += SocketRead_Completed;
                    m_socket.ReceiveAsync(receiveArgs);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Warn, "Connection to " + m_monitorHost + ":" + m_monitorPort + " failed.");
                    SetButtonContent(m_closeSocketButton, "Connect");
                    UIHelper.SetVisibility(m_closeSocketButton, Visibility.Visible);
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception SocketConnect_Completed. " + excp.Message);
            }
        }

        private void SocketRead_Completed(object sender, SocketAsyncEventArgs e)
        {
            try
            {
                int bytesRead = e.BytesTransferred;
                if (bytesRead > 0)
                {
                    string data = Encoding.UTF8.GetString(m_socketBuffer, 0, bytesRead);
                    AppendMonitorText(data);

                    SocketAsyncEventArgs receiveArgs = new SocketAsyncEventArgs();
                    receiveArgs.SetBuffer(m_socketBuffer, 0, MAX_SOCKET_BUFFER_SIZE);
                    receiveArgs.Completed += SocketRead_Completed;
                    m_socket.ReceiveAsync(receiveArgs);
                }
                else
                {
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Connection to " + m_monitorHost + ":" + m_monitorPort + " has been closed.");
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage_External(MessageLevelsEnum.Error, "Exception SocketRead_Completed. " + excp.Message);
            }
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
                        SocketAsyncEventArgs sendArgs = new SocketAsyncEventArgs();
                        sendArgs.SetBuffer(sendBuffer, 0, sendBuffer.Length);
                        m_socket.SendAsync(sendArgs);
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
                    m_isConnected = false;
                    m_socket.Close();
                    LogActivityMessage_External(MessageLevelsEnum.Info, "Connection closed.");
                    m_closeSocketButton.Content = "Connect";
                }
                catch (Exception excp)
                {
                    LogActivityMessage_External(MessageLevelsEnum.Error, "Exception CloseSocket. " + excp.Message);
                }
            }
            else
            {
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

        private void SetButtonContent(Button button, string content)
        {
            if (this.Dispatcher.CheckAccess())
            {
                button.Content = content;
            }
            else
            {
                this.Dispatcher.BeginInvoke(new SetButtonContentDelegate(SetButtonContent), button, content);
            }
        }
	}
}