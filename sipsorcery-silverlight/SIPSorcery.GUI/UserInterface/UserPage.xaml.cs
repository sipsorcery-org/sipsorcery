using System;
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
using SIPSorcery.SIP.App;
using SIPSorcery.Persistence;
using SIPSorcery.Sockets;

namespace SIPSorcery
{
	public partial class UserPage : UserControl
	{
        public LogoutDelegate Logout_External;

		private SIPSorceryPersistor m_persistor;        // External services that can be used to retrieve and persist the SIP related assets this client deals with.
        //private SIPSorceryManager m_sipServerManager;   // External services that can be used to query transitive state objects (calls, bindings etc.) and initiate actions on SIP server agents.
        private SocketClient m_sipEventMonitorClient;

        private SIPAccountManager m_sipAccountManager;
        private DialPlanManager m_dialPlanManager;
        private SIPProviderManager m_sipProviderManager;
        private SIPCallManager m_sipCallsManager;
        private MonitoringConsole m_monitorConsole;

        private TextBlock m_selectedTextBlock = null;

        private string m_owner;
        private string m_monitorHost;
        private int m_monitorPort;

        public UserPage()
        {
            InitializeComponent();
        }

        public UserPage(SIPSorceryPersistor persistor, SocketClient sipEventMonitorClient, LogoutDelegate logoutDelegate, string owner, string monitorHost, int monitorPort)
		{
			InitializeComponent();

            App.AppUnhandledException += new AppUnhandledExceptionDelegate(AppUnhandledException);

            m_persistor = persistor;
            m_sipEventMonitorClient = sipEventMonitorClient;
            Logout_External = logoutDelegate;
            m_owner = owner;
            m_monitorHost = monitorHost;
            m_monitorPort = monitorPort;

            this.m_activityPorgressBar.Visibility = Visibility.Collapsed;
            this.TabNavigation = KeyboardNavigationMode.Cycle;

            if (m_sipEventMonitorClient != null)
            {
                m_sipEventMonitorClient.SocketDataReceived += new SocketDataReceivedDelegate(SIPEventMonitorClient_MonitorEventReceived);
                m_sipEventMonitorClient.SocketConnectionChange += new SocketConnectionChangeDelegate(SIPEventMonitorClient_MonitorConnectionChange);
            }

            m_dialPlanManager = new DialPlanManager(LogActivityMessage, ShowActivityProgress, m_persistor, m_owner);
            m_dialPlanManager.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_dialPlanManager);

            m_sipAccountManager = new SIPAccountManager(LogActivityMessage, ShowActivityProgress, m_persistor, m_dialPlanManager.GetDialPlanNames, m_owner);
            m_sipAccountManager.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_sipAccountManager);

            m_sipProviderManager = new SIPProviderManager(LogActivityMessage, ShowActivityProgress, m_persistor, m_owner);
            m_sipProviderManager.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_sipProviderManager);

            m_sipCallsManager = new SIPCallManager(LogActivityMessage, ShowActivityProgress, m_persistor, m_owner);
            m_sipCallsManager.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_sipCallsManager);

            m_monitorConsole = new MonitoringConsole(LogActivityMessage, m_owner, m_monitorHost, m_monitorPort);
            m_monitorConsole.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_monitorConsole);

            SetActive(m_sipAccountManager);
            SetSelectedTextBlock(m_sipAccountsLink);
  		}

        private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            m_dialPlanManager.Initialise();
            m_sipAccountManager.Initialise();
        }

        public void SetAppStatusIconColour(Color colour)
        {
            UIHelper.SetFill(m_appStatusIcon, colour);
        }

        public void SetAppStatusMessage(string message)
        {
            UIHelper.SetText(m_appStatusMessage, message);
        }

        private void AppUnhandledException(ApplicationUnhandledExceptionEventArgs e)
        {
            LogActivityMessage(MessageLevelsEnum.Error, e.ExceptionObject.Message);
        }

        private void SIPEventMonitorClient_MonitorEventReceived(byte[] data, int bytesRead)
        {
            SIPMonitorMachineEvent machineEvent = SIPMonitorMachineEvent.ParseMachineEventCSV(Encoding.UTF8.GetString(data, 0, bytesRead));

            try
            {
                if (machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingRemoval ||
                    machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate)
                {
                    LogActivityMessage(MessageLevelsEnum.Monitor, "Registrar binding update notification received");

                    if (m_sipAccountManager != null)
                    {
                        m_sipAccountManager.SIPMonitorMachineEventHandler(machineEvent);
                    }
                }
                else if (machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPAccountDelete ||
                    machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPAccountUpdate)
                {
                    LogActivityMessage(MessageLevelsEnum.Monitor, "SIP Account change notification received");

                    if (m_sipAccountManager != null)
                    {
                        m_sipAccountManager.SIPMonitorMachineEventHandler(machineEvent);
                    }
                }
                else if (machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingUpdate ||
                    machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingRemoval)
                {
                    LogActivityMessage(MessageLevelsEnum.Monitor, "SIP Registration Agent change notification received");

                    if (m_sipProviderManager != null)
                    {
                        m_sipProviderManager.SIPMonitorMachineEventHandler(machineEvent);
                    }
                }
                else if (machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPDialogueCreated ||
                    machineEvent.MachineEventType == SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved)
                {
                    LogActivityMessage(MessageLevelsEnum.Monitor, "SIP call change notification received");

                    if (m_sipCallsManager != null)
                    {
                        m_sipCallsManager.SIPMonitorMachineEventHandler(machineEvent);
                    }
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage(MessageLevelsEnum.Error, "Exception handling monitor event. " + excp.Message);
            }
        }

        private void SIPEventMonitorClient_MonitorConnectionChange(SocketConnectionStatus connectionState)
        {
            if (connectionState.ConnectionStatus == ServiceConnectionStatesEnum.Error)
            {
                LogActivityMessage(MessageLevelsEnum.Warn, connectionState.Message);
            }
        }

        private void SetActive(UserControl control)
        {
            m_sipAccountManager.Visibility = (m_sipAccountManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_dialPlanManager.Visibility = (m_dialPlanManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_sipProviderManager.Visibility = (m_sipProviderManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_sipCallsManager.Visibility = (m_sipCallsManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_monitorConsole.Visibility = (m_monitorConsole == control) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SIPAccountsLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SetActive(m_sipAccountManager);
            SetSelectedTextBlock(m_sipAccountsLink);

            if (!m_sipAccountManager.Initialised)
            {
                m_sipAccountManager.Initialise();
            }
        }

        private void DialPlansLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SetActive(m_dialPlanManager);
            SetSelectedTextBlock(m_dialplansLink);

            if (!m_dialPlanManager.Initialised)
            {
                m_dialPlanManager.Initialise();
            }
        }

        private void SIPProvidersLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SetActive(m_sipProviderManager);
            SetSelectedTextBlock(m_sipProvidersLink);

            if (!m_sipProviderManager.Initialised)
            {
                m_sipProviderManager.Initialise();
            }
        }

        private void MonitorLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SetActive(m_monitorConsole);
            SetSelectedTextBlock(m_monitorLink);
        }
       
        private void CallsLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SetActive(m_sipCallsManager);
            SetSelectedTextBlock(m_callsLink);

            if (!m_sipCallsManager.Initialised)
            {
                m_sipCallsManager.Initialise();
            }
        }

        private void CityLightsLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            LogActivityMessage(MessageLevelsEnum.Warn, "The City Lights control is not yet implemented.");
        }

        private void LogoutLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            m_sipAccountManager.Close();
            m_dialPlanManager.Close();
            m_sipCallsManager.Close();
            m_monitorConsole.Close();
            SetSelectedTextBlock(null);
            Logout_External(true);
        }

        private void DeleteAccountLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e) {
            try {
                MessageBoxResult confirmDelete = MessageBox.Show("Important! If you have active SIP Provider bindings you need to deactivate them BEFORE deleting your account.\nPress Ok to delete all your account details.", "Confirm Delete", MessageBoxButton.OKCancel);
                if (confirmDelete == MessageBoxResult.OK) {
                    m_persistor.DeleteCustomerComplete += (eargs) => { Logout_External(false); };
                    m_persistor.DeleteCustomerAsync(m_owner);
                }
            }
            catch (Exception excp) {
                LogActivityMessage(MessageLevelsEnum.Error, "Exception deleting account. " + excp.Message);
            }
        }

        /// <summary>
        /// Highlights the menu option links when clicked on.
        /// </summary>
        /// <param name="selectedTextBlock">The TextBlock the user clicked on.</param>
        private void SetSelectedTextBlock(TextBlock selectedTextBlock)
        {
            if (m_selectedTextBlock != null)
            {
                m_selectedTextBlock.Foreground = new SolidColorBrush(Color.FromArgb(0xff, 0xA0, 0xF9, 0x27));
                m_selectedTextBlock.TextDecorations = TextDecorations.Underline;
            }

            if (selectedTextBlock != null)
            {
                m_selectedTextBlock = selectedTextBlock;
                m_selectedTextBlock.Foreground = new SolidColorBrush(Colors.Purple);
                m_selectedTextBlock.TextDecorations = null;
            }
        }

        public void LogActivityMessage(MessageLevelsEnum level, string message) {
            UIHelper.AppendToActivityLog(m_activityLogScrollViewer, m_activityTextBlock, level, message);
        }

        private void ShowActivityProgress(double? progress)
        {
            try
            {
                if (progress == null)
                {
                    UIHelper.SetVisibility(m_activityPorgressBar, Visibility.Collapsed);
                }
                else
                {
                    //UIHelper.SetVisibility(m_activityPorgressBar, Visibility.Visible);
                    UIHelper.SetProgressBarValue(m_activityPorgressBar, progress.Value);
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage(MessageLevelsEnum.Error, "Exception ShowActivityProgress. " + excp.Message);
            }
        }
	}
}