using System;
using System.Net;
using System.Net.Browser;
using System.Reflection;
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
using System.Windows.Threading;
using SIPSorcery.SIP.App;
using SIPSorcery.Persistence;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.Sockets;
using SIPSorcery.Sys;
using SIPSorcery.SIPSorceryProvisioningClient;

namespace SIPSorcery
{
    public partial class Page : UserControl
    {
        private const int REINITIALISE_WAIT_PERIOD = 10000;
        private const int DEFAULT_WEB_PORT = 80;
        private const int DEFAULT_PROVISIONING_WEBSERVICE_PORT = 8080;
        private const string DEFAULT_PROVISIONING_FILE = "provisioning.svc";
        private const string DEFAULT_NOTIFICATIONS_FILE = "notificationspull.svc";
        private const string DEFAULT_MONITOR_HOST = "www.sipsorcery.com";
        private const string LOCALHOST_MONITOR_HOST = "localhost";
        private const string DEFAULT_PROVISIONING_HOST = "https://www.sipsorcery.com/";
        private const string LOCALHOST_PROVISIONING_HOST = "http://localhost/";

        private string m_dummyOwner = SIPSorceryGUITestPersistor.DUMMY_OWNER;

        private string m_provisioningServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_PROVISIONING_FILE;
        private string m_notificationsServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_NOTIFICATIONS_FILE;
        private string m_sipMonitorHost = DEFAULT_MONITOR_HOST;
        private int m_sipControlMonitorPort = 4502;
        private int m_sipMachineMonitorPort = 4503;
        private DnsEndPoint m_machineMonitorEndPoint;

        private SIPSorceryPersistor m_unauthorisedPersistor;
        private SIPSorceryPersistor m_authorisedPersistor;
        //private SocketClient m_sipEventMonitorClient;
        private ServiceConnectionStatesEnum m_persistorStatus = ServiceConnectionStatesEnum.Initialising;
        private string m_persistorStatusMessage = "Initialising...";
        private ServiceConnectionStatesEnum m_monitorStatus = ServiceConnectionStatesEnum.Initialising;
        private string m_monitorStatusMessage = "Initialising...";
        private bool m_provisioningInitialisationInProgress;
        private bool m_monitorInitialisationInProgress;
        private string m_authId;
        private string m_owner;
        private bool m_sessionExpired = true;   // Set to true after an unauthorised access exception.

        private UserPage m_userPage;

        public Page()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            m_versionTextBlock.Text = Assembly.GetExecutingAssembly().FullName.Split(',')[1];
            //m_versionTextBlock.Text = "version: " + Assembly.GetExecutingAssembly().FullName;
            m_appStatusMessage.Text = "Initialising...";
            //m_loginControl.Visibility = Visibility.Collapsed;
            m_loginControl.CreateNewAccountClicked += CreateNewAccountClicked;
            m_loginControl.Login_External = LoginAsync;

            //int hostPort = Application.Current.Host.Source.Port;
            //if (hostPort == DEFAULT_WEB_PORT || hostPort == 0)
            //{
            //    hostPort = DEFAULT_PROVISIONING_WEBSERVICE_PORT;
            //}

            string server = Application.Current.Host.Source.DnsSafeHost;

            try
            {
                if (server.StartsWith(LOCALHOST_MONITOR_HOST) || Application.Current.Host.Source.Scheme == "file")
                {
                    m_sipMonitorHost = LOCALHOST_MONITOR_HOST;
                    m_provisioningServiceURL = LOCALHOST_PROVISIONING_HOST + DEFAULT_PROVISIONING_FILE;
                    m_notificationsServiceURL = LOCALHOST_PROVISIONING_HOST + DEFAULT_NOTIFICATIONS_FILE;
                }
                
                /*if (server != DEFAULT_MONITOR_HOST)
                {
                    m_sipMonitorHost = server;
                    m_provisioningServiceURL = Application.Current.Host.Source.Scheme + "://" + server + ":" + DEFAULT_PROVISIONING_WEBSERVICE_PORT + "/" + DEFAULT_PROVISIONING_FILE;
                    m_notificationsServiceURL = Application.Current.Host.Source.Scheme + "://" + server + ":" + DEFAULT_PROVISIONING_WEBSERVICE_PORT + "/" + DEFAULT_NOTIFICATIONS_FILE;
                }
                else
                {
                    m_sipMonitorHost = DEFAULT_MONITOR_HOST;
                    m_provisioningServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_PROVISIONING_FILE;
                    m_notificationsServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_NOTIFICATIONS_FILE;
                }*/
            }
            catch
            {
                m_sipMonitorHost = DEFAULT_MONITOR_HOST;
                m_provisioningServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_PROVISIONING_FILE;
                m_notificationsServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_NOTIFICATIONS_FILE;
            }

            if (server != DEFAULT_MONITOR_HOST)
            {
                m_setSvcLink.Visibility = Visibility.Visible;
                m_provisioningSvcTextBox.Text = m_provisioningServiceURL;
            }

            // Use the Silverlight network stack so that SOAP faults can get through.
            HttpWebRequest.RegisterPrefix(m_provisioningServiceURL, WebRequestCreator.ClientHttp);
            HttpWebRequest.RegisterPrefix(m_notificationsServiceURL, WebRequestCreator.ClientHttp);

            ThreadPool.QueueUserWorkItem(delegate { Initialise(); });

            //UIHelper.SetPluginDimensions(LayoutRoot.RenderSize.Width, LayoutRoot.RenderSize.Height);
        }

        private void Initialise()
        {

#if !BLEND
            m_unauthorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.WebService, m_provisioningServiceURL, null);
            m_machineMonitorEndPoint = new DnsEndPoint(m_sipMonitorHost, m_sipMachineMonitorPort);
#else
            m_unauthorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.GUITest, m_provisioningServiceURL, null);
            //ThreadPool.QueueUserWorkItem(new WaitCallback(m_sipMonitorDisplay.RunHitPointSimulation), null);
#endif

            m_unauthorisedPersistor.TestExceptionComplete += TestExceptionComplete;
            m_unauthorisedPersistor.IsAliveComplete += PersistorIsAliveComplete;
            m_unauthorisedPersistor.AreNewAccountsEnabledComplete += AreNewAccountsEnabledComplete;
            m_unauthorisedPersistor.LoginComplete += LoginComplete;
            m_unauthorisedPersistor.CreateCustomerComplete += CreateCustomerComplete;
            m_createAccountControl.CreateCustomer_External = m_unauthorisedPersistor.CreateCustomerAsync;
            UIHelper.SetVisibility(m_createAccountControl, Visibility.Collapsed);

            InitialiseServices(0);
        }

        private void CreateNewAccountClicked()
        {
            m_createAccountControl.Visibility = (m_createAccountControl.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
            m_logo.Visibility = (m_createAccountControl.Visibility == Visibility.Collapsed) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TestExceptionComplete(System.ComponentModel.AsyncCompletedEventArgs e)
        {
            try
            {
                if (e.Error != null)
                {
                    m_persistorStatus = ServiceConnectionStatesEnum.Error;
                    m_persistorStatusMessage = "Test Exception raw fault result: " + e.Error.Message;
                    UpdateAppStatus();
                }
            }
            catch (Exception excp)
            {
                m_persistorStatus = ServiceConnectionStatesEnum.Error;
                m_persistorStatusMessage = "Test Exception catch result: " + excp.Message;
                UpdateAppStatus();
            }
        }

        /// <summary>
        /// If a connection failure occurs for the provisioning web service or the machine monitoring socket this method
        /// will be called. It will wait for a number of seconds and then try and re-initialise the connections.
        /// </summary>
        private void InitialiseServices(int millisecondsDelay)
        {
            if (!m_provisioningInitialisationInProgress)
            {
                m_provisioningInitialisationInProgress = true;
                m_monitorInitialisationInProgress = true;

                if (millisecondsDelay > 0)
                {
                    Thread.Sleep(millisecondsDelay);
                }

                try
                {
                    //m_unauthorisedPersistor.TestExceptionAsync();
                    m_unauthorisedPersistor.IsAliveAsync();
                }
                catch (Exception provExcp)
                {
                    m_persistorStatusMessage = provExcp.Message;
                    m_persistorStatus = ServiceConnectionStatesEnum.Error;
                    UpdateAppStatus();
                }
            }
        }

        private void UpdateAppStatus()
        {
            if (m_persistorStatus == ServiceConnectionStatesEnum.Error)
            {
                UIHelper.SetFill(m_appStatusIcon, Colors.Red);
                UIHelper.SetText(m_appStatusMessage, m_persistorStatusMessage);

                if (m_userPage != null)
                {
                    m_userPage.SetProvisioningStatusIconColour(Colors.Red);
                    m_userPage.SetProvisioningStatusMessage(m_persistorStatusMessage);
                }
            }
            else if (m_persistorStatus == ServiceConnectionStatesEnum.Ok)
            {
                UIHelper.SetFill(m_appStatusIcon, Colors.Green);
                if (m_provisioningServiceURL == DEFAULT_PROVISIONING_HOST + DEFAULT_PROVISIONING_FILE)
                {
                    UIHelper.SetText(m_appStatusMessage, "Provisioning service ok.");
                }
                else
                {
                    UIHelper.SetText(m_appStatusMessage, "Provisioning service ok.\n" + m_provisioningServiceURL + ".");
                }

                if (m_userPage != null)
                {
                    m_userPage.SetProvisioningStatusIconColour(Colors.Green);
                    if (m_provisioningServiceURL == DEFAULT_PROVISIONING_HOST + DEFAULT_PROVISIONING_FILE)
                    {
                        m_userPage.SetProvisioningStatusMessage("Provisioning service ok.");
                    }
                    else
                    {
                        m_userPage.SetProvisioningStatusMessage("Provisioning service ok: " + m_provisioningServiceURL + ".");
                    }
                }
                else
                {
                    m_unauthorisedPersistor.AreNewAccountsEnabledAsync();
                }
            }
        }

        private void PersistorIsAliveComplete(IsAliveCompletedEventArgs e)
        {
            try
            {
                if (e.Result)
                {
                    m_persistorStatusMessage = null;
                    m_persistorStatus = ServiceConnectionStatesEnum.Ok;
                }
                else
                {
                    m_persistorStatusMessage = "Could not connect to provisioning service on " + m_provisioningServiceURL + ".";
                    m_persistorStatus = ServiceConnectionStatesEnum.Error;
                }
            }
            catch
            {
                //m_persistorStatusMessage = excp.Message;
                m_persistorStatusMessage = "Could not connect to provisioning service on " + m_provisioningServiceURL + ".";
                m_persistorStatus = ServiceConnectionStatesEnum.Error;
            }
            finally
            {
                m_provisioningInitialisationInProgress = false;
                UpdateAppStatus();
            }
        }

        private void AreNewAccountsEnabledComplete(AreNewAccountsEnabledCompletedEventArgs e)
        {
            try
            {
                if (e.Result)
                {
                    m_loginControl.EnableNewAccounts();
                    m_createAccountControl.SetDataEntryEnabled(true);
                }
                else
                {
                    m_loginControl.DisableNewAccounts("New accounts disabled. Please check sipsorcery.wordpress.com for further details.");
                }
            }
            catch (Exception excp)
            {
                string exceptionMessage = (excp.Message.Length > 100) ? excp.Message.Substring(0, 100) : excp.Message;
                m_loginControl.DisableNewAccounts(exceptionMessage);
            }
        }

        private void SIPEventMonitorClient_MonitorConnectionChange(SocketConnectionStatus connectionState)
        {
            try
            {
                m_monitorStatusMessage = connectionState.Message;
                m_monitorStatus = connectionState.ConnectionStatus;
                m_monitorInitialisationInProgress = false;
                UpdateAppStatus();

                // Send the authentication token so the machine socket gets matched to the user.
                //m_sipEventMonitorClient.Send(Encoding.UTF8.GetBytes(m_authId));
            }
            catch (Exception excp)
            {
                m_userPage.LogActivityMessage(MessageLevelsEnum.Error, "Error setting machine socket token, auto refreshes disabled.");
            }
        }

        private void SIPEventMonitorClient_MonitorEventReceived(byte[] data, int bytesRead)
        {
            SIPMonitorMachineEvent machineEvent = SIPMonitorMachineEvent.ParseMachineEventCSV(Encoding.UTF8.GetString(data, 0, bytesRead));

            /*if (machineEvent.RemoteEndPoint != null)
            {
                //m_cityLightsDisplay.WriteMonitorMessage(machineEvent.MachineEventType.ToString() + " " + machineEvent.RemoteEndPoint + " " + machineEvent.Message);
                //m_cityLightsDisplay.PlotHeatMapEvent(machineEvent.MachineEventType, machineEvent.RemoteEndPoint.Address);
            }
            else
            {
                //m_cityLightsDisplay.WriteMonitorMessage(machineEvent.MachineEventType.ToString() + " " + machineEvent.Message);
            }*/
        }

        private void LoginAsync(string username, string password)
        {
#if !BLEND
            m_owner = username.ToLower();
#else
            m_owner = m_dummyOwner;
#endif
            m_unauthorisedPersistor.LoginAsync(username, password);
        }

        private void LoginComplete(LoginCompletedEventArgs e)
        {
            if (e.Error != null)
            {
                m_loginControl.WriteLoginMessage("Error logging in. " + e.Error.Message);
            }
            else
            {
                if (e.Result == null)
                {
                    m_loginControl.WriteLoginMessage("Login failed.");
                }
                else
                {
                    // Set the screen back to the initial state so it's ready for a logout.
                    //UIHelper.SetVisibility(m_appStatusCanvas, Visibility.Collapsed);
                    //UIHelper.SetVisibility(m_loginControl, Visibility.Collapsed);
                    //UIHelper.SetVisibility(m_sipMonitorDisplay, Visibility.Collapsed);
                    UIHelper.SetText(m_appStatusMessage, "Initialising...");
                    UIHelper.SetFill(m_appStatusIcon, Colors.Blue);
                    m_loginControl.DisableNewAccounts(null);
                    UIHelper.SetVisibility(m_provisioningSvcTextBox, Visibility.Collapsed);
                    UIHelper.SetVisibility(m_setSvcLinkApply, Visibility.Collapsed);

                    //UIHelper.RemoveChild(m_topCanvas, m_appStatusCanvas);
                    //UIHelper.RemoveChild(m_topCanvas, m_loginControl);
                    //UIHelper.RemoveChild(m_topCanvas, m_sipMonitorDisplay);

                    m_sessionExpired = false;
                    m_authId = e.Result;

#if !BLEND
                    m_authorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.WebService, m_provisioningServiceURL, m_authId);

                    //m_sipEventMonitorClient = new SocketClient(m_machineMonitorEndPoint);
                    //m_sipEventMonitorClient.SocketDataReceived += SIPEventMonitorClient_MonitorEventReceived;
                    //m_sipEventMonitorClient.SocketConnectionChange += SIPEventMonitorClient_MonitorConnectionChange;
#else
                    m_authorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.GUITest, m_provisioningServiceURL, m_authId);
#endif
                    m_authorisedPersistor.SessionExpired += SessionExpired;
                    m_authorisedPersistor.LogoutComplete += LogoutComplete;
                    m_authorisedPersistor.GetTimeZoneOffsetMinutesComplete += GetTimeZoneOffsetMinutesComplete;
                    m_authorisedPersistor.GetTimeZoneOffsetMinutesAsync();

                    m_loginControl.Clear();
                    m_loginControl.DisableNewAccounts(null);
                    m_createAccountControl.Clear();
                    UIHelper.SetVisibility(m_createAccountControl, Visibility.Collapsed);

                    m_userPage = new UserPage(m_authorisedPersistor, LogoutAsync, m_owner, m_authId, m_notificationsServiceURL);
                    m_mainPageBorder.Content = m_userPage;

                    /*if (m_sipEventMonitorClient != null)
                    {
                        try
                        {
                            m_sipEventMonitorClient.ConnectAsync();
                        }
                        catch (Exception monExcp)
                        {
                            m_monitorStatusMessage = monExcp.Message;
                            m_monitorStatus = ServiceConnectionStatesEnum.Error;
                            m_userPage.LogActivityMessage(MessageLevelsEnum.Warn, "Wasn't able to connect to monitoring socket, automatic refreshes are disabled.");
                        }
                    }*/

                    UpdateAppStatus();
                }
            }
        }

        private void GetTimeZoneOffsetMinutesComplete(GetTimeZoneOffsetMinutesCompletedEventArgs e)
        {
            try
            {
                if (e.Error == null)
                {
                    SIPCDRAsset.TimeZoneOffsetMinutes = e.Result;
                    SIPProviderBinding.TimeZoneOffsetMinutes = e.Result;
                    SIPRegistrarBinding.TimeZoneOffsetMinutes = e.Result;
                }
            }
            catch (Exception excp)
            {
                m_userPage.LogActivityMessage(MessageLevelsEnum.Error, "Exception GetTimeZoneOffsetMinutes. " + excp.Message);
            }
        }

        private void SessionExpired()
        {
            try
            {
                m_sessionExpired = true;
                m_userPage.LogActivityMessage(MessageLevelsEnum.Warn, "Session has expired, please re-login.");

                /*if (m_sipEventMonitorClient != null)
                {
                    m_sipEventMonitorClient.Close();
                    m_monitorStatus = ServiceConnectionStatesEnum.None;
                }*/

                if (m_userPage != null)
                {
                    m_userPage.ShutdownNotifications();
                }

                UpdateAppStatus();
            }
            catch (Exception excp)
            {
                m_userPage.LogActivityMessage(MessageLevelsEnum.Error, "Exception SessionExpired. " + excp.Message);
            }
        }

        private void LogoutAsync(bool sendServerLogout)
        {
            try
            {
                this.TabNavigation = KeyboardNavigationMode.Cycle;
                m_mainPageBorder.Content = m_topCanvas;

                if (sendServerLogout)
                {
                    m_authorisedPersistor.LogoutAsync();
                }

                m_authId = null;
                m_sessionExpired = true;
                m_userPage = null;

                /*if (m_sipEventMonitorClient != null)
                {
                    m_sipEventMonitorClient.Close();
                    m_monitorStatus = ServiceConnectionStatesEnum.None;
                }*/

                UpdateAppStatus();
            }
            catch (Exception excp)
            {
                m_userPage.LogActivityMessage(MessageLevelsEnum.Error, "Exception LogoutAsync. " + excp.Message);
            }
        }

        private void LogoutComplete(System.ComponentModel.AsyncCompletedEventArgs e)
        {
            try
            {
                try
                {
                    if (e.Error == null)
                    {
                        m_loginControl.WriteLoginMessage(String.Empty);
                    }
                    else
                    {
                        throw e.Error;
                    }
                }
                catch (Exception excp)
                {
                    string excpMessage = (excp.InnerException != null) ? excp.InnerException.Message : excp.Message;
                    //m_loginControl.WriteLoginMessage("Error logging out. " + excpMessage);
                }
            }
            finally
            {
                //if (m_sipEventMonitorClient != null)
                //{
                //    m_sipEventMonitorClient.Close();
                //}

                ThreadPool.QueueUserWorkItem(delegate { Initialise(); });
            }
        }

        private void CreateCustomerComplete(System.ComponentModel.AsyncCompletedEventArgs e)
        {
            if (e.Error == null)
            {
                m_loginControl.DisableNewAccounts(null);
            }
            m_createAccountControl.CustomerCreated(e);
        }

        private void DisplaySetServiceTextBox(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            m_provisioningSvcTextBox.Visibility = (m_provisioningSvcTextBox.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
            m_setSvcLinkApply.Visibility = m_provisioningSvcTextBox.Visibility;
        }

        private void ApplySetService(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!m_provisioningSvcTextBox.Text.IsNullOrBlank())
            {
                Uri uriResult = null;
                if (Uri.TryCreate(m_provisioningSvcTextBox.Text, UriKind.Absolute, out uriResult))
                {
                    m_provisioningServiceURL = uriResult.ToString();
                    m_notificationsServiceURL = uriResult.Scheme + "://" + uriResult.Host + ":" + uriResult.Port + "/" + DEFAULT_NOTIFICATIONS_FILE;
                    m_sipMonitorHost = uriResult.Host;

                    // Use the Silverlight network stack so that SOAP faults can get through.
                    HttpWebRequest.RegisterPrefix(m_provisioningServiceURL, WebRequestCreator.ClientHttp);
                    HttpWebRequest.RegisterPrefix(m_notificationsServiceURL, WebRequestCreator.ClientHttp);

                    UIHelper.SetText(m_appStatusMessage, "Attempting to connect to " + m_provisioningServiceURL + ".");
                    Initialise();
                }
                else
                {
                    UIHelper.SetText(m_appStatusMessage, "Could not parse URI from " + m_provisioningSvcTextBox.Text + ".");
                }
            }
        }

        private void AboutLink_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            About about = new About();
            about.Show();
        }

        private void UserControl_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
           // try
           // {
                //document.getElementById("silverlightControlHost").style.height="300px";
                HtmlPage.Plugin.SetProperty("height", e.NewSize.Height);
                //Dispatcher.BeginInvoke(() => { MessageBox.Show("Control resized"); });
            //}
           // catch { }
        }
    }
}