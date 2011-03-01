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
        private const string SERVICE_URL_KEY = "ServiceURL";
        private const string SHOW_INVITE_PANEL_KEY = "ShowInvitePanel";
        private const string DEFAULT_PROVISIONING_FILE = "provisioning.svc";
        private const string DEFAULT_NOTIFICATIONS_FILE = "notificationspull.svc";
        private const string DEFAULT_INVITE_SERIVCE = "sipsorceryinvite.svc";
        private const string DEFAULT_PROVISIONING_HOST = "https://www.sipsorcery.com/";

        private string m_dummyOwner = SIPSorceryGUITestPersistor.DUMMY_OWNER;

        private string m_provisioningServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_PROVISIONING_FILE;
        private string m_notificationsServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_NOTIFICATIONS_FILE;
        private string m_inviteServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_INVITE_SERIVCE;

        private SIPSorceryPersistor m_unauthorisedPersistor;
        private SIPSorceryPersistor m_authorisedPersistor;
        private SIPSorceryInvite.SIPSorceryInviteServiceClient m_inviteProxy;
        private ServiceConnectionStatesEnum m_persistorStatus = ServiceConnectionStatesEnum.Initialising;
        private string m_persistorStatusMessage = "Initialising...";
        private bool m_provisioningInitialisationInProgress;
        private string m_authId;
        private string m_owner;
        private bool m_showInvitePanel = true;

        private UserPage m_userPage;

        public Page()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            m_versionTextBlock.Text = Assembly.GetExecutingAssembly().FullName.Split(',')[1].Replace('=', ' ');
            m_provisioningStatusMessage.Content = "Initialising...";
            m_loginControl.CreateNewAccountClicked += CreateNewAccountLinkClicked;
            m_loginControl.Authenticated += Authenticated;
            m_createAccountControl.CancelCreateCustomer_External = CancelCreateCustomer;

            string initShowInvitePanel = App.Current.Resources[SHOW_INVITE_PANEL_KEY] as string;
            if (!initShowInvitePanel.IsNullOrBlank())
            {
                Boolean.TryParse(initShowInvitePanel, out m_showInvitePanel);
            }

            if (m_showInvitePanel)
            {
                m_loginControl.EnableInviteCode();
            }

            string server = Application.Current.Host.Source.DnsSafeHost;

            try
            {
                string initServiceURL = App.Current.Resources[SERVICE_URL_KEY] as string;
                if (!initServiceURL.ToString().IsNullOrBlank())
                {
                    string serviceHost = initServiceURL;
                    m_provisioningServiceURL = serviceHost + DEFAULT_PROVISIONING_FILE;
                    m_notificationsServiceURL = serviceHost + DEFAULT_NOTIFICATIONS_FILE;
                    m_inviteServiceURL = serviceHost + DEFAULT_INVITE_SERIVCE;
                }
            }
            catch
            {
                m_provisioningServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_PROVISIONING_FILE;
                m_notificationsServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_NOTIFICATIONS_FILE;
                m_inviteServiceURL = DEFAULT_PROVISIONING_HOST + DEFAULT_INVITE_SERIVCE;
            }

            // Use the Silverlight network stack so that SOAP faults can get through.
            HttpWebRequest.RegisterPrefix(m_provisioningServiceURL, WebRequestCreator.ClientHttp);
            HttpWebRequest.RegisterPrefix(m_notificationsServiceURL, WebRequestCreator.ClientHttp);
            HttpWebRequest.RegisterPrefix(m_inviteServiceURL, WebRequestCreator.ClientHttp);

            ThreadPool.QueueUserWorkItem(delegate { Initialise(); });

            //UIHelper.SetPluginDimensions(LayoutRoot.RenderSize.Width, LayoutRoot.RenderSize.Height);
        }

        private void Initialise()
        {

#if !BLEND
            m_unauthorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.WebService, m_provisioningServiceURL, null);

            EndpointAddress address = new EndpointAddress(m_inviteServiceURL);
            BasicHttpSecurityMode securitymode = (m_inviteServiceURL.StartsWith("https")) ? BasicHttpSecurityMode.Transport : BasicHttpSecurityMode.None;
            BasicHttpBinding binding = new BasicHttpBinding(securitymode);
            m_inviteProxy = new SIPSorceryInvite.SIPSorceryInviteServiceClient(binding, address);
#else
            m_unauthorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.GUITest, m_provisioningServiceURL, null);
            //ThreadPool.QueueUserWorkItem(new WaitCallback(m_sipMonitorDisplay.RunHitPointSimulation), null);
#endif

            m_unauthorisedPersistor.TestExceptionComplete += TestExceptionComplete;
            m_unauthorisedPersistor.IsAliveComplete += PersistorIsAliveComplete;
            m_unauthorisedPersistor.AreNewAccountsEnabledComplete += AreNewAccountsEnabledComplete;
            //m_unauthorisedPersistor.CheckInviteCodeComplete +=CheckInviteCodeComplete;

            m_unauthorisedPersistor.CreateCustomerComplete += CreateCustomerComplete;
            m_createAccountControl.CreateCustomer_External = m_unauthorisedPersistor.CreateCustomerAsync;

            m_loginControl.SetProxy(m_unauthorisedPersistor, m_inviteProxy);

            InitialiseServices(0);
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
                UIHelper.SetText(m_provisioningStatusMessage, m_persistorStatusMessage);

                if (m_userPage != null)
                {
                    m_userPage.SetProvisioningStatusIconColour(Colors.Red);
                    m_userPage.SetProvisioningStatusMessage(m_persistorStatusMessage);
                }
            }
            else if (m_persistorStatus == ServiceConnectionStatesEnum.Ok)
            {
                UIHelper.SetFill(m_appStatusIcon, Colors.Green);
                UIHelper.SetText(m_provisioningStatusMessage, "Provisioning service ok: " + m_provisioningServiceURL + ".");

                if (m_userPage != null)
                {
                    m_userPage.SetProvisioningStatusIconColour(Colors.Green);
                    m_userPage.SetProvisioningStatusMessage("Provisioning service ok: " + m_provisioningServiceURL + ".");
                }
                //else
                //{
                //    m_unauthorisedPersistor.AreNewAccountsEnabledAsync();
                //}
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
                    m_loginControl.EnableCreateAccount();
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

        private void SIPEventMonitorClient_MonitorEventReceived(byte[] data, int bytesRead)
        {
            SIPMonitorMachineEvent machineEvent = SIPMonitorMachineEvent.ParseMachineEventCSV(Encoding.UTF8.GetString(data, 0, bytesRead));
        }

        private void Authenticated(string username, string authID)
        {
            UIHelper.SetText(m_provisioningStatusMessage, "Initialising...");
            UIHelper.SetFill(m_appStatusIcon, Colors.Blue);
            //m_loginControl.DisableNewAccounts(null);

            m_owner = username;
            m_authId = authID;

#if !BLEND
            m_authorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.WebService, m_provisioningServiceURL, m_authId);
#else
            m_authorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.GUITest, m_provisioningServiceURL, m_authId);
#endif
            m_authorisedPersistor.SessionExpired += SessionExpired;
            m_authorisedPersistor.LogoutComplete += LogoutComplete;
            m_authorisedPersistor.GetTimeZoneOffsetMinutesComplete += GetTimeZoneOffsetMinutesComplete;
            m_authorisedPersistor.GetTimeZoneOffsetMinutesAsync();

            m_loginControl.Clear();
            //m_loginControl.DisableNewAccounts(null);
            m_createAccountControl.Clear();
            UIHelper.SetVisibility(m_createAccountControl, Visibility.Collapsed);

            m_userPage = new UserPage(m_authorisedPersistor, LogoutAsync, m_owner, m_authId, m_notificationsServiceURL);
            m_mainPageBorder.Content = m_userPage;

            UpdateAppStatus();
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
                m_userPage.LogActivityMessage(MessageLevelsEnum.Warn, "Session has expired, please re-login.");

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
                m_userPage = null;

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
            m_createAccountControl.CustomerCreated(e);
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

        /// <summary>
        /// Click handler for Cancel button on Create Customer control.
        /// </summary>
        private void CancelCreateCustomer()
        {
            m_createAccountControl.Visibility = System.Windows.Visibility.Collapsed;
            m_createAccountControl.ClearError();
            m_loginControl.Clear();
            m_loginControl.Visibility = System.Windows.Visibility.Visible;
        }

        /// <summary>
        /// Click handler for Create New Account link on hte Create Customer control.
        /// </summary>
        /// <param name="inviteCode">If an invite code is required to create an account then this parameter
        /// holds a validated invite code.</param>
        private void CreateNewAccountLinkClicked(string inviteCode)
        {
            m_loginControl.Clear();
            m_loginControl.Visibility = System.Windows.Visibility.Collapsed;
            m_createAccountControl.ClearError();
            m_createAccountControl.InviteCode = inviteCode;
            m_createAccountControl.Visibility = System.Windows.Visibility.Visible;
        }
    }
}