using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.DomainServices.Client;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Browser;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Resources;
using SIPSorcery.Persistence;
using SIPSorcery.Entities;
using SIPSorcery.Entities.Services;
using SIPSorcery.Silverlight.Messaging;      
using SIPSorcery.Sys;
using SIPSorcery.Web.Services;
using SIPSorcery.Silverlight.Services;

namespace SIPSorcery
{
    public partial class UserPage : UserControl
    {
        private const string DEFAULT_HELP_URL = "http://www.sipsorcery.com/help";
        private const string DEFAULT_HELP_OPTIONS = "width=600,height=500,scrollbars=1";
        private const int HELP_POPUP_WIDTH = 600;
        private const int HELP_POPUP_HEIGHT = 500;
        private const int NOTIFICATION_RECONNECT_DELAY = 15000;     // Time in milliseconds to wait after connection to notifications service is lost before attempting to reconnect.
        private const string WILDCARD_EVENT_FILTER = "*";
        private const int MAX_RIA_LIST_SIZE = 100;                  // Maximum size for SIP Domains and SIP DialPlan list requests.

        public LogoutDelegate Logout_External;

        private SIPSorceryNotificationClient m_sipNotifierClient;
        private SIPAccountManager m_sipAccountManager;
        private DialPlanManager m_dialPlanManager;
        private SIPProviderManager m_sipProviderManager;
        private SIPCallManager m_sipCallsManager;
        private MonitoringConsole m_monitorConsole;
        private CustomerSettingsControl m_customerSettings;

        private TextBlock m_selectedTextBlock = null;

        private string m_owner;
        private string m_notificationsURL;

        public UserPage()
        {
            InitializeComponent();
        }

        public UserPage(
            LogoutDelegate logoutDelegate, 
            string owner,  
            string notificationsURL,
            SIPEntitiesDomainContext riaContext)
        {
            InitializeComponent();

            App.AppUnhandledException += new AppUnhandledExceptionDelegate(AppUnhandledException);

            Logout_External = logoutDelegate;
            m_owner = owner;
            m_notificationsURL = notificationsURL;

            this.TabNavigation = KeyboardNavigationMode.Cycle;

            Initialise(riaContext);
            
            // Get the customer record so the API key can be used to connect to the notifications service.
            riaContext.Load(riaContext.GetCustomerQuery(), LoadBehavior.RefreshCurrent, GetCustomerCompleted, riaContext);

            m_dialPlanManager = new DialPlanManager(LogActivityMessage, m_owner, riaContext);
            m_dialPlanManager.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_dialPlanManager);

            m_sipAccountManager = new SIPAccountManager(LogActivityMessage, m_owner, riaContext);
            m_sipAccountManager.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_sipAccountManager);

            m_sipProviderManager = new SIPProviderManager(LogActivityMessage, m_owner, riaContext);
            m_sipProviderManager.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_sipProviderManager);

            m_sipCallsManager = new SIPCallManager(LogActivityMessage, m_owner, riaContext);
            m_sipCallsManager.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_sipCallsManager);

            m_monitorConsole = new MonitoringConsole(LogActivityMessage);
            m_monitorConsole.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_monitorConsole);

            m_customerSettings = new CustomerSettingsControl(LogActivityMessage, Logout_External, m_owner, riaContext);
            m_customerSettings.Visibility = Visibility.Collapsed;
            m_mainCanvas.Children.Add(m_customerSettings);

            SetActive(m_sipAccountManager);
            SetSelectedTextBlock(m_sipAccountsLink);
        }

        private void Initialise(SIPEntitiesDomainContext riaContext)
        {
            if (riaContext.SIPDomains.Count() == 0)
            {
                var query = riaContext.GetSIPDomainsQuery().OrderBy(x => x.Domain).Skip(0).Take(MAX_RIA_LIST_SIZE);
                query.IncludeTotalCount = true;
                riaContext.Load(query, LoadBehavior.RefreshCurrent, (lo) => { LogActivityMessage(MessageLevelsEnum.Info, lo.TotalEntityCount + " domains successfully loaded."); }, null);
            }

            if (riaContext.SIPDialPlans.Count() == 0)
            {
                var query = riaContext.GetSIPDialplansQuery().OrderBy(x => x.DialPlanName).Skip(0).Take(MAX_RIA_LIST_SIZE);
                query.IncludeTotalCount = true;
                riaContext.Load(query, LoadBehavior.RefreshCurrent, (lo) => { LogActivityMessage(MessageLevelsEnum.Info, lo.TotalEntityCount + " dialplans successfully loaded."); }, null);
            }

            //riaContext.GetTimeZoneOffsetMinutes(GetTimeZoneOffsetComplete, null);
        }

        //private void GetTimeZoneOffsetComplete(InvokeOperation<int> io)
        //{
        //    if (io.HasError)
        //    {
        //        LogActivityMessage(MessageLevelsEnum.Error, "Error getting timezone offset minutes. " + io.Error.Message);
        //        io.MarkErrorAsHandled();
        //    }
        //    else
        //    {
        //        int offsetMinutes = io.Value;

        //        //LogActivityMessage(MessageLevelsEnum.Info, "Timezone offset minutes " + offsetMinutes + ".");

        //        CDR.TimeZoneOffsetMinutes = offsetMinutes;
        //        SIPProviderBinding.TimeZoneOffsetMinutes = offsetMinutes;
        //        SIPRegistrarBinding.TimeZoneOffsetMinutes = offsetMinutes;
        //    }
        //}

        private void GetCustomerCompleted(LoadOperation<Customer> lo)
        {
            if (lo.HasError)
            {
                LogActivityMessage(MessageLevelsEnum.Warn, "Error loading API key for notifications service. " + lo.Error.Message);
                lo.MarkErrorAsHandled();
            }
            else
            {
                SIPEntitiesDomainContext riaContext = (SIPEntitiesDomainContext)lo.UserState;
                Customer customer = riaContext.Customers.FirstOrDefault();

                if (customer == null)
                {
                    LogActivityMessage(MessageLevelsEnum.Warn, "User record could not be loaded when attempting to retrieve API key for notifications service.");
                }
                else
                {
                    CDR.TimeZoneOffsetMinutes = customer.TimeZoneOffsetMinutes;
                    SIPProviderBinding.TimeZoneOffsetMinutes = customer.TimeZoneOffsetMinutes;
                    SIPRegistrarBinding.TimeZoneOffsetMinutes = customer.TimeZoneOffsetMinutes;
                    SIPAccount.TimeZoneOffsetMinutes = customer.TimeZoneOffsetMinutes;
                    m_serviceLevelTextBlock.Text = customer.ServiceLevel;

                    m_sipNotifierClient = new SIPSorceryNotificationClient(LogActivityMessage, m_notificationsURL, customer.APIKey);
                    m_sipNotifierClient.StatusChanged += NotificationsServiceStatusChanged;
                    m_sipNotifierClient.MachineEventReceived += SIPEventMonitorClient_MonitorEventReceived;
                    m_monitorConsole.SetNotifierClient(m_sipNotifierClient);
                    m_sipNotifierClient.Connect();
                }
            }
        }

        private void NotificationsServiceStatusChanged(ServiceConnectionStatesEnum serviceStatus, string statusMessage)
        {
            if (serviceStatus == ServiceConnectionStatesEnum.Ok)
            {
                UIHelper.SetFill(m_notificationsStatusIcon, Colors.Green);
                UIHelper.SetText(m_notificationsStatusMessage, "Notifications service ok. " + m_notificationsURL);
            }
            else
            {
                UIHelper.SetFill(m_notificationsStatusIcon, Colors.Red);
                UIHelper.SetText(m_notificationsStatusMessage, statusMessage + " " + m_notificationsURL);
            }
        }

        public void ShutdownNotifications()
        {
            try
            {
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

        private void PollingClientDebugMessage(string message)
        {
            LogActivityMessage(MessageLevelsEnum.Monitor, message);
        }

        private void UserControl_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Show the SIP account manager as the intial screen.
            SIPAccountsLink_MouseLeftButtonUp(null, null);
        }

        public void SetProvisioningStatusIconColour(Color colour)
        {
            UIHelper.SetFill(m_provisioningStatusIcon, colour);
        }

        public void SetProvisioningStatusMessage(string message)
        {
            UIHelper.SetText(m_provisioningStatusMessage, message);
        }

        private void AppUnhandledException(ApplicationUnhandledExceptionEventArgs e)
        {
            LogActivityMessage(MessageLevelsEnum.Error, e.ExceptionObject.Message);
            e.Handled = true;
        }

        private void SIPEventMonitorClient_MonitorEventReceived(SIPSorcery.SIP.App.SIPMonitorMachineEvent machineEvent)
        {
            try
            {
                if (machineEvent.MachineEventType == SIPSorcery.SIP.App.SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingRemoval ||
                    machineEvent.MachineEventType == SIPSorcery.SIP.App.SIPMonitorMachineEventTypesEnum.SIPRegistrarBindingUpdate)
                {
                    LogActivityMessage(MessageLevelsEnum.Monitor, "Registrar binding update notification received");

                    if (m_sipAccountManager != null)
                    {
                        UIHelper.HandleMonitorEvent(m_sipAccountManager.SIPMonitorMachineEventHandler, machineEvent);
                    }
                }
                else if (machineEvent.MachineEventType == SIPSorcery.SIP.App.SIPMonitorMachineEventTypesEnum.SIPAccountDelete ||
                    machineEvent.MachineEventType == SIPSorcery.SIP.App.SIPMonitorMachineEventTypesEnum.SIPAccountUpdate)
                {
                    LogActivityMessage(MessageLevelsEnum.Monitor, "SIP Account change notification received");

                    if (m_sipAccountManager != null)
                    {
                        UIHelper.HandleMonitorEvent(m_sipAccountManager.SIPMonitorMachineEventHandler, machineEvent);
                    }
                }
                else if (machineEvent.MachineEventType == SIPSorcery.SIP.App.SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingUpdate ||
                    machineEvent.MachineEventType == SIPSorcery.SIP.App.SIPMonitorMachineEventTypesEnum.SIPRegistrationAgentBindingRemoval)
                {
                    LogActivityMessage(MessageLevelsEnum.Monitor, "SIP Registration Agent change notification received");

                    if (m_sipProviderManager != null)
                    {
                        UIHelper.HandleMonitorEvent(m_sipProviderManager.SIPMonitorMachineEventHandler, machineEvent);
                    }
                }
                else if (machineEvent.MachineEventType == SIPSorcery.SIP.App.SIPMonitorMachineEventTypesEnum.SIPDialogueCreated ||
                    machineEvent.MachineEventType == SIPSorcery.SIP.App.SIPMonitorMachineEventTypesEnum.SIPDialogueRemoved)
                {
                    LogActivityMessage(MessageLevelsEnum.Monitor, "SIP call change notification received");

                    if (m_sipCallsManager != null)
                    {
                        UIHelper.HandleMonitorEvent(m_sipCallsManager.SIPMonitorMachineEventHandler, machineEvent);
                    }
                }
            }
            catch (Exception excp)
            {
                LogActivityMessage(MessageLevelsEnum.Error, "Exception handling monitor event. " + excp.Message);
            }
        }

        private void SetActive(UserControl control)
        {
            m_sipAccountManager.Visibility = (m_sipAccountManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_dialPlanManager.Visibility = (m_dialPlanManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_sipProviderManager.Visibility = (m_sipProviderManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_sipCallsManager.Visibility = (m_sipCallsManager == control) ? Visibility.Visible : Visibility.Collapsed;
            m_monitorConsole.Visibility = (m_monitorConsole == control) ? Visibility.Visible : Visibility.Collapsed;
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

        private void LogoutLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (m_sipNotifierClient != null)
            {
                m_sipNotifierClient.Close();
            }
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
            catch
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
    }
}