using System;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Browser;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SIPSorcery.Persistence;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.SIP.App;
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
//using SIPSorcery.Sockets;
using SIPSorcery.Silverlight.Services;

namespace SIPSorcery
{
    public partial class UserPage : UserControl
    {
        //private const string DEFAULT_HELP_URL = "https://www.sipsorcery.com/help.html";
        private const string DEFAULT_HELP_URL = "http://www.sipsorcery.com/help";
        private const string DEFAULT_HELP_OPTIONS = "width=600,height=500,scrollbars=1";
        private const int HELP_POPUP_WIDTH = 600;
        private const int HELP_POPUP_HEIGHT = 500;
        private const int INITIAL_DISPLAY_EXTEND_SESSION = 2700000;     // Display the extend session button after 45 minutes initially.
        private const int SUBSEQUENT_DISPLAY_EXTEND_SESSION = 3600000;  // Display the extend session button after 60 minutes subsequently.
        private const int EXTEND_SESSION_INCREMENTS = 60;               // The number of minutes to extend the session by.
        private const int NOTIFICATION_RECONNECT_DELAY = 15000;         // Time in milliseconds to wait after connection to notifications service is lost before attempting to reconnect.
        private const string WILDCARD_EVENT_FILTER = "*";

        public LogoutDelegate Logout_External;

        private SIPSorceryPersistor m_persistor;        // External services that can be used to retrieve and persist the SIP related assets this client deals with.
        private SIPSorceryNotificationClient m_sipNotifierClient;
        //private SIPSorceryManager m_sipServerManager;   // External services that can be used to query transitive state objects (calls, bindings etc.) and initiate actions on SIP server agents.
        //private SocketClient m_sipEventMonitorClient;

        private SIPAccountManager m_sipAccountManager;
        private DialPlanManager m_dialPlanManager;
        private SIPProviderManager m_sipProviderManager;
        private SIPCallManager m_sipCallsManager;
        private MonitoringConsole m_monitorConsole;
        private SIPSwitchboard m_switchboard;
        private CustomerSettingsControl m_customerSettings;
        private Timer m_sessionTimer;
        private Timer m_expiredTimer;
        private Timer m_notificationsTimer;

        private TextBlock m_selectedTextBlock = null;

        private string m_owner;
        private string m_authId;
        private string m_notificationsURL;
        private bool m_exit;
        //private string m_monitorHost;
        //private int m_monitorPort;

        public UserPage()
        {
            InitializeComponent();
        }

        public UserPage(
            SIPSorceryPersistor persistor, 
            LogoutDelegate logoutDelegate, 
            string owner, 
            string authId, 
            string notificationsURL)
        {
            InitializeComponent();

            App.AppUnhandledException += new AppUnhandledExceptionDelegate(AppUnhandledException);

            m_persistor = persistor;
            //m_sipEventMonitorClient = sipEventMonitorClient;
            Logout_External = logoutDelegate;
            m_owner = owner;
            m_authId = authId;
            m_notificationsURL = notificationsURL;
            //m_monitorHost = monitorHost;
            //m_monitorPort = monitorPort;
            m_sessionTimer = new Timer(delegate { UIHelper.SetVisibility(m_extendSessionButton, Visibility.Visible); }, null, INITIAL_DISPLAY_EXTEND_SESSION, Timeout.Infinite);
            m_expiredTimer = new Timer(delegate { SessionExpired(); }, null, SUBSEQUENT_DISPLAY_EXTEND_SESSION, Timeout.Infinite);

            this.m_activityPorgressBar.Visibility = Visibility.Collapsed;
            this.TabNavigation = KeyboardNavigationMode.Cycle;
            this.m_extendSessionButton.Visibility = Visibility.Collapsed;
            m_persistor.ExtendSessionComplete += ExtendSessionComplete;
            m_persistor.IsAliveComplete += PersistorIsAliveComplete;

            m_sipNotifierClient = new SIPSorceryNotificationClient(LogActivityMessage, m_notificationsURL, m_authId);
            m_sipNotifierClient.StatusChanged += NotificationsServiceStatusChanged;
            m_sipNotifierClient.MachineEventReceived += SIPEventMonitorClient_MonitorEventReceived;

            //if (m_sipEventMonitorClient != null)
            //{
            //    m_sipEventMonitorClient.SocketDataReceived += new SocketDataReceivedDelegate(SIPEventMonitorClient_MonitorEventReceived);
            //    m_sipEventMonitorClient.SocketConnectionChange += new SocketConnectionChangeDelegate(SIPEventMonitorClient_MonitorConnectionChange);
            //}

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

            m_monitorConsole = new MonitoringConsole(LogActivityMessage, m_sipNotifierClient);
            m_monitorConsole.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_monitorConsole);

            m_switchboard = new SIPSwitchboard(LogActivityMessage, m_owner);
            m_switchboard.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_switchboard);

            m_customerSettings = new CustomerSettingsControl(LogActivityMessage, Logout_External, m_persistor, m_owner);
            m_customerSettings.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_customerSettings);

            SetActive(m_sipAccountManager);
            SetSelectedTextBlock(m_sipAccountsLink);

            m_persistor.IsAliveAsync();
            m_sipNotifierClient.Connect();
        }

        private void NotificationsServiceStatusChanged(ServiceConnectionStatesEnum serviceStatus, string statusMessage)
        {
            if (serviceStatus == ServiceConnectionStatesEnum.Ok)
            {
                UIHelper.SetFill(m_notificationsStatusIcon, Colors.Green);
                UIHelper.SetText(m_notificationsStatusMessage, "Notifications service ok.");
            }
            else
            {
                UIHelper.SetFill(m_notificationsStatusIcon, Colors.Red);
                UIHelper.SetText(m_notificationsStatusMessage, statusMessage);
            }
        }

        public void ShutdownNotifications()
        {
            try
            {
                CloseNotificationChannel(false);
                if (m_monitorConsole != null)
                {
                    m_monitorConsole.Close();
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage(MessageLevelsEnum.Error, "Exception ShutdownNotifications. " + excp.Message);
            }
        }

        private void PersistorIsAliveComplete(SIPSorcery.SIPSorceryProvisioningClient.IsAliveCompletedEventArgs e)
        {
            /*try
            {
                if (!m_exit)
                {
                    if (e.Error == null && m_sipNotifierClient == null)
                    {
                        m_sipNotifierClient = new PollingDuplexClient(PollingClientDebugMessage, m_authId, m_notificationsURL);
                        m_sipNotifierClient.NotificationReceived += (notificationText) => {
                            SIPMonitorEvent monitorEvent = SIPMonitorEvent.ParseEventCSV(notificationText);
                            SIPEventMonitorClient_MonitorEventReceived((SIPMonitorMachineEvent)monitorEvent);};
                        m_sipNotifierClient.Closed += () => {
                            //if (m_sipNotifierClient != null)
                            //{
                            //    LogActivityMessage(MessageLevelsEnum.Warn, "Machine monitor notification channel closed at " + DateTime.Now.ToString("dd MMM yyyy HH:mm:ss") + ".");
                            //}
                            CloseNotificationChannel(true);
                        };
                        m_sipNotifierClient.Subscribe(SIPMonitorClientTypesEnum.Machine.ToString(), WILDCARD_EVENT_FILTER);
                    }
                    else
                    {
                        //LogActivityMessage(MessageLevelsEnum.Warn, "Connection to notification server failed.");
                        if (!m_exit)
                        {
                            m_notificationsTimer = new Timer(delegate { m_persistor.IsAliveAsync(); }, null, NOTIFICATION_RECONNECT_DELAY, Timeout.Infinite);
                        }
                    }
                }
            }
            catch
            {
                // LogActivityMessage(MessageLevelsEnum.Warn, "Connection to notification server failed.");
                if (!m_exit)
                {
                    m_notificationsTimer = new Timer(delegate { m_persistor.IsAliveAsync(); }, null, NOTIFICATION_RECONNECT_DELAY, Timeout.Infinite);
                }
            }*/
        }

        private void PollingClientDebugMessage(string message)
        {
            LogActivityMessage(MessageLevelsEnum.Monitor, message);
        }

        private void CloseNotificationChannel(bool reconnect)
        {
            /*try
            {
                if (m_sipNotifierClient != null)
                {
                    m_sipNotifierClient.Close();
                    m_sipNotifierClient = null;

                    if (reconnect && !m_exit)
                    {
                        m_notificationsTimer = new Timer(delegate { m_persistor.IsAliveAsync(); }, null, NOTIFICATION_RECONNECT_DELAY, Timeout.Infinite);
                    }
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage(MessageLevelsEnum.Error, "Exception closing machine notification channel. " + excp.Message);
            }*/
        }

        private void ExtendSessionComplete(System.ComponentModel.AsyncCompletedEventArgs e)
        {
            try
            {
                if (e.Error == null)
                {
                    // Session was successfully extended, hide the button and reset the timer.
                    UIHelper.SetVisibility(m_extendSessionButton, Visibility.Collapsed);
                    m_expiredTimer.Dispose();
                    m_sessionTimer = new Timer(delegate { UIHelper.SetVisibility(m_extendSessionButton, Visibility.Visible); }, null, INITIAL_DISPLAY_EXTEND_SESSION, UInt32.MaxValue);
                    m_expiredTimer = new Timer(delegate { SessionExpired(); }, null, SUBSEQUENT_DISPLAY_EXTEND_SESSION, Timeout.Infinite);
                }
                else
                {
                    // Session was not successfully extended, hide the button and DON'T reset the timer.
                    LogActivityMessage(MessageLevelsEnum.Warn, e.Error.Message);
                    m_sessionTimer.Dispose();
                    m_expiredTimer.Dispose();
                    SessionExpired();
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage(MessageLevelsEnum.Error, "Exception ExtendSessionComplete. " + excp.Message);
            }
        }

        private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            m_dialPlanManager.Initialise();
            m_sipAccountManager.Initialise();
        }

        public void SetProvisioningStatusIconColour(Color colour)
        {
            UIHelper.SetFill(m_provisioningStatusIcon, colour);
        }

        public void SetProvisioningStatusMessage(string message)
        {
            UIHelper.SetText(m_provisioningStatusMessage, message);
        }

        public void SetNotificationsStatusIconColour(Color colour)
        {
            UIHelper.SetFill(m_notificationsStatusIcon, colour);
        }

        public void SetNotificationsStatusMessage(string message)
        {
            UIHelper.SetText(m_notificationsStatusMessage, message);
        }

        private void AppUnhandledException(ApplicationUnhandledExceptionEventArgs e)
        {
            LogActivityMessage(MessageLevelsEnum.Error, e.ExceptionObject.Message);
        }

        private void SIPEventMonitorClient_MonitorEventReceived(SIPMonitorMachineEvent machineEvent)
        {
            //SIPMonitorMachineEvent machineEvent = SIPMonitorMachineEvent.ParseMachineEventCSV(Encoding.UTF8.GetString(data, 0, bytesRead));

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

        //private void SIPEventMonitorClient_MonitorConnectionChange(SocketConnectionStatus connectionState)
        //{
        //    if (connectionState.ConnectionStatus == ServiceConnectionStatesEnum.Error)
        //    {
        //        LogActivityMessage(MessageLevelsEnum.Warn, connectionState.Message);
        //    }
        //}

        private void SetActive(UserControl control)
        {
            m_sipAccountManager.Visibility = (m_sipAccountManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_dialPlanManager.Visibility = (m_dialPlanManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_sipProviderManager.Visibility = (m_sipProviderManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_sipCallsManager.Visibility = (m_sipCallsManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_monitorConsole.Visibility = (m_monitorConsole == control) ? Visibility.Visible : Visibility.Collapsed;
            m_switchboard.Visibility = (m_switchboard == control) ? Visibility.Visible : Visibility.Collapsed;
            m_customerSettings.Visibility = (m_customerSettings == control) ? Visibility.Visible : Visibility.Collapsed;
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

        private void SessionExpired()
        {
            UIHelper.SetVisibility(m_extendSessionButton, Visibility.Collapsed);
            LogActivityMessage(MessageLevelsEnum.Warn, "Your login session has expired, please make a copy of any changes you have made and re-login.");
            m_sessionTimer.Dispose();
            m_expiredTimer.Dispose();
            m_sipNotifierClient.Close();
        }

        private void LogoutLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            m_exit = true;
            m_sipAccountManager.Close();
            m_dialPlanManager.Close();
            m_sipCallsManager.Close();
            //m_monitorConsole.Close();
            m_sipNotifierClient.Close();
            CloseNotificationChannel(false);
            SetSelectedTextBlock(null);
            Logout_External(true);
        }

        private void HelpLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                Uri helpURI = new Uri(DEFAULT_HELP_URL);
                HtmlPage.Window.Navigate(new Uri(DEFAULT_HELP_URL), "SIPSorceryHelp", DEFAULT_HELP_OPTIONS);

                /*HtmlPopupWindowOptions options = new HtmlPopupWindowOptions();
                options.Width = HELP_POPUP_WIDTH;
                options.Height = HELP_POPUP_HEIGHT;
                options.Scrollbars = true;

                if (HtmlPage.IsPopupWindowAllowed)
                {
                    HtmlPage.PopupWindow(new Uri(DEFAULT_HELP_URL), "SIPSorceryHelp", options);
                }
                else
                {
                    LogActivityMessage(MessageLevelsEnum.Warn, "Unable to display help, popup windows have been disabled by your browser. The help page is available at " + DEFAULT_HELP_URL + ".");
                }*/
            }
            catch(Exception excp)
            {
                LogActivityMessage(MessageLevelsEnum.Warn, "Unable to display help, you may have popup windows disabled. Alternatively navigate to " + DEFAULT_HELP_URL + ".");
                //LogActivityMessage(MessageLevelsEnum.Error, "Exception displaying help. " + excp.Message);
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
                UIHelper.SetTextBlockDisplayLevel(m_selectedTextBlock, MessageLevelsEnum.None);
                m_selectedTextBlock.TextDecorations = TextDecorations.Underline;
            }

            if (selectedTextBlock != null)
            {
                UIHelper.SetTextBlockDisplayLevel(selectedTextBlock, MessageLevelsEnum.Selected);
                selectedTextBlock.TextDecorations = null;
                m_selectedTextBlock = selectedTextBlock;
            }
        }

        public void LogActivityMessage(MessageLevelsEnum level, string message)
        {
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

        private void SwitchboardLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SetActive(m_switchboard);
            SetSelectedTextBlock(m_switchboardLink);
            m_switchboard.Start();
        }

        private void SettingsLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SetActive(m_customerSettings);
            SetSelectedTextBlock(m_settingsLink);
            m_customerSettings.Load();
        }

        private void AboutLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            About about = new About();
            about.Show();
        }

        private void ExtendSessionButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            UIHelper.SetVisibility(m_extendSessionButton, Visibility.Collapsed);
            m_persistor.ExtendSessionAsync(EXTEND_SESSION_INCREMENTS);
        }
    }
}