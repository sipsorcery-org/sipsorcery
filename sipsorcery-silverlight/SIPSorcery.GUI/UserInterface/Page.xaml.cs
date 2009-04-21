using System;
using System.Net;
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
using SIPSorcery.Sockets;

namespace SIPSorcery
{  
	public partial class Page : UserControl
	{
        private const int REINITIALISE_WAIT_PERIOD = 10000;

        private string m_dummyOwner = SIPSorceryGUITestPersistor.DUMMY_OWNER;

        private string m_provisioningServiceURL; // = "http://localhost:8080/provisioning";
        //private string m_provisioningServiceURL = "http://sipsorcery.com:8080/provisioning";
        private string m_sipMonitorHost; // = "localhost";
        //private string m_sipMonitorHost = "sipsorcery.com";
        private int m_sipControlMonitorPort = 4502;
        private int m_sipMachineMonitorPort = 4503;
        private DnsEndPoint m_machineMonitorEndPoint;

        private SIPSorceryPersistor m_unauthorisedPersistor;
        private SIPSorceryPersistor m_authorisedPersistor;
        private SocketClient m_sipEventMonitorClient;
        private ServiceConnectionStatesEnum m_persistorStatus = ServiceConnectionStatesEnum.Initialising;
        private string m_persistorStatusMessage = "Initialising...";
        private ServiceConnectionStatesEnum m_monitorStatus = ServiceConnectionStatesEnum.Initialising;
        private string m_monitorStatusMessage = "Initialising...";
        private bool m_provisioningInitialisationInProgress;
        private bool m_monitorInitialisationInProgress;
        private string m_authId;
        private string m_owner;

        private UserPage m_userPage;

        public Page()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            m_appStatusMessage.Text = "Initialising...";
            m_loginControl.Visibility = Visibility.Collapsed;

            m_provisioningServiceURL = "http://" + Application.Current.Host.Source.DnsSafeHost + ":8080/provisioning";
            m_sipMonitorHost = Application.Current.Host.Source.DnsSafeHost;

            ThreadPool.QueueUserWorkItem(new WaitCallback(Initialise), null);

            //UIHelper.SetPluginDimensions(LayoutRoot.RenderSize.Width, LayoutRoot.RenderSize.Height);
        }

        private void Initialise(object state)
        {

#if !BLEND
            m_unauthorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.WebService, m_provisioningServiceURL, null);

            m_machineMonitorEndPoint = new DnsEndPoint(m_sipMonitorHost, m_sipMachineMonitorPort);
            m_sipEventMonitorClient = new SocketClient(m_machineMonitorEndPoint);
            //m_sipEventMonitorClient = new SocketClient(new IPEndPoint(IPAddress.Parse("127.0.0.1"), m_sipMachineMonitorPort));
            m_sipEventMonitorClient.SocketDataReceived += new SocketDataReceivedDelegate(SIPEventMonitorClient_MonitorEventReceived);
            m_sipEventMonitorClient.SocketConnectionChange += new SocketConnectionChangeDelegate(SIPEventMonitorClient_MonitorConnectionChange);
#else
            m_unauthorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.GUITest, m_provisioningServiceURL, null);
            //ThreadPool.QueueUserWorkItem(new WaitCallback(m_sipMonitorDisplay.RunHitPointSimulation), null);
#endif

            m_unauthorisedPersistor.IsAliveComplete += new IsAliveCompleteDelegate(PersistorIsAliveComplete);
            m_unauthorisedPersistor.LoginComplete += new LoginCompleteDelegate(LoginComplete);
            m_loginControl.Login_External = LoginAsync;

            InitialiseServices(0);
        }

        /// <summary>
        /// If a connection failure occurs for the provisioning web service or the machine monitoring socket this method
        /// will be called. It will wait for a number of seconds and then try and re-initialise the connections.
        /// </summary>
        private void InitialiseServices(object state)
        {
            m_provisioningInitialisationInProgress = true;
            m_monitorInitialisationInProgress = true;

            int millisecondsDelay = (int)state;
            if (millisecondsDelay > 0)
            {
                Thread.Sleep(millisecondsDelay);
            }

            try
            {
                m_unauthorisedPersistor.IsAliveAsync();
            }
            catch (Exception provExcp)
            {
                m_persistorStatusMessage = provExcp.Message;
                m_persistorStatus = ServiceConnectionStatesEnum.Error;
                UpdateAppStatus();
            }

            if (m_sipEventMonitorClient != null && m_monitorStatus != ServiceConnectionStatesEnum.Ok)
            {
                try
                {
                    m_sipEventMonitorClient.ConnectAsync();
                }
                catch (Exception monExcp)
                {
                    m_monitorStatusMessage = monExcp.Message;
                    m_monitorStatus = ServiceConnectionStatesEnum.Error;
                    UpdateAppStatus();
                }
            }
        }

        private void UpdateAppStatus()
        {
            bool reinitialisationRequired = false;

            if (m_persistorStatus == ServiceConnectionStatesEnum.Error)
            {
                UIHelper.SetFill(m_appStatusIcon, Colors.Red);
                UIHelper.SetText(m_appStatusMessage, m_persistorStatusMessage);

                if (m_userPage != null)
                {
                    m_userPage.SetAppStatusIconColour(Colors.Red);
                    m_userPage.SetAppStatusMessage(m_persistorStatusMessage);
                }

                reinitialisationRequired = true;
            }
            else if (m_persistorStatus == ServiceConnectionStatesEnum.Ok)
            {
                if (m_userPage == null || m_authId == null)
                {
                    UIHelper.SetVisibility(m_loginControl, Visibility.Visible);
                }

                if (m_monitorStatus == ServiceConnectionStatesEnum.Error)
                {
                    UIHelper.SetFill(m_appStatusIcon, Colors.Orange);
                    UIHelper.SetText(m_appStatusMessage, m_monitorStatusMessage);

                    if (m_userPage != null)
                    {
                        m_userPage.SetAppStatusIconColour(Colors.Orange);
                        m_userPage.SetAppStatusMessage(m_monitorStatusMessage);
                    }

                    reinitialisationRequired = true;
                }
                else if (m_monitorStatus == ServiceConnectionStatesEnum.Ok)
                {
                    UIHelper.SetFill(m_appStatusIcon, Colors.Green);
                    UIHelper.SetText(m_appStatusMessage, "Ready");
                    UIHelper.SetFocus(m_loginControl);

                    if (m_userPage != null)
                    {
                        m_userPage.SetAppStatusIconColour(Colors.Green);
                        m_userPage.SetAppStatusMessage("Ready");
                    }
                }
                else
                {
                    UIHelper.SetFill(m_appStatusIcon, Colors.Yellow);
                    UIHelper.SetText(m_appStatusMessage, "Provisioning service ok");
                    UIHelper.SetFocus(m_loginControl);

                    if (m_userPage != null)
                    {
                        m_userPage.SetAppStatusIconColour(Colors.Yellow);
                        m_userPage.SetAppStatusMessage("Provisioning service ok");
                    }
                }
            }

            if (reinitialisationRequired && !m_monitorInitialisationInProgress && !m_provisioningInitialisationInProgress)
            {
                ThreadPool.QueueUserWorkItem(new WaitCallback(InitialiseServices), REINITIALISE_WAIT_PERIOD);
            }
        }

        private void PersistorIsAliveComplete(SIPSorcery.SIPSorceryProvisioningClient.IsAliveCompletedEventArgs e)
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
                    m_persistorStatusMessage = "Could not connect to provisioning service.";
                    m_persistorStatus = ServiceConnectionStatesEnum.Error;
                }
            }
            catch
            {
                //m_persistorStatusMessage = excp.Message;
                m_persistorStatusMessage = "Could not connect to provisioning service.";
                m_persistorStatus = ServiceConnectionStatesEnum.Error;
            }
            finally
            {
                m_provisioningInitialisationInProgress = false;
                UpdateAppStatus();
            }
        }

        private void SIPEventMonitorClient_MonitorConnectionChange(SocketConnectionStatus connectionState)
        {
            m_monitorStatusMessage = connectionState.Message;
            m_monitorStatus = connectionState.ConnectionStatus;
            m_monitorInitialisationInProgress = false;
            UpdateAppStatus();
        }

        private void SIPEventMonitorClient_MonitorEventReceived(byte[] data, int bytesRead)
        {
            SIPMonitorMachineEvent machineEvent = SIPMonitorMachineEvent.ParseMachineEventCSV(Encoding.UTF8.GetString(data, 0, bytesRead));

            if (machineEvent.RemoteEndPoint != null)
            {
                //m_cityLightsDisplay.WriteMonitorMessage(machineEvent.MachineEventType.ToString() + " " + machineEvent.RemoteEndPoint + " " + machineEvent.Message);
                //m_cityLightsDisplay.PlotHeatMapEvent(machineEvent.MachineEventType, machineEvent.RemoteEndPoint.Address);
            }
            else
            {
                //m_cityLightsDisplay.WriteMonitorMessage(machineEvent.MachineEventType.ToString() + " " + machineEvent.Message);
            }
        }

        private void LoginAsync(string username, string password)
        {
#if !BLEND
            m_owner = username;
#else
            m_owner = m_dummyOwner;
#endif
            m_unauthorisedPersistor.LoginAsync(username, password);
        }

        private void LoginComplete(SIPSorcery.SIPSorceryProvisioningClient.LoginCompletedEventArgs e)
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
                    //UIHelper.SetVisibility(m_appStatusCanvas, Visibility.Collapsed);
                    //UIHelper.SetVisibility(m_loginControl, Visibility.Collapsed);
                    //UIHelper.SetVisibility(m_sipMonitorDisplay, Visibility.Collapsed);
                    UIHelper.SetText(m_appStatusMessage, "Initialising...");
                    UIHelper.SetFill(m_appStatusIcon, Colors.Blue);
                    m_loginControl.Clear();

                    //UIHelper.RemoveChild(m_topCanvas, m_appStatusCanvas);
                    //UIHelper.RemoveChild(m_topCanvas, m_loginControl);
                    //UIHelper.RemoveChild(m_topCanvas, m_sipMonitorDisplay);
                    
                    m_authId = e.Result;

#if !BLEND
                    m_authorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.WebService, m_provisioningServiceURL, m_authId);
#else
                    m_authorisedPersistor = SIPSorceryPersistorFactory.CreateSIPSorceryPersistor(SIPPersistorTypesEnum.GUITest, m_provisioningServiceURL, m_authId);
#endif

                    m_authorisedPersistor.LogoutComplete += new LogoutCompleteDelegate(LogoutComplete);

                    m_userPage = new UserPage(m_authorisedPersistor, m_sipEventMonitorClient, LogoutAsync, m_owner, m_sipMonitorHost, m_sipControlMonitorPort);
                    //UIHelper.AddChild(m_topCanvas, m_userPage);
                    //UIHelper.SetBorderChild(m_mainPageBorder, m_userPage);
                    //UIHelper.RemoveBorderChild(m_mainPageBorder);
                    m_mainPageBorder.Content = m_userPage;
                    UpdateAppStatus();
                }
            }
        }

        private void LogoutAsync()
        {
            m_topCanvas.Children.Remove(m_userPage);
            this.TabNavigation = KeyboardNavigationMode.Cycle;
            m_authorisedPersistor.LogoutAsync();
            m_authId = null;
            UpdateAppStatus();
        }

        private void LogoutComplete(System.ComponentModel.AsyncCompletedEventArgs e)
        {
            try
            {
                //UIHelper.SetBorderChild(m_mainPageBorder, m_topCanvas);
                m_mainPageBorder.Content = m_topCanvas;
                //UIHelper.SetVisibility(m_appStatusCanvas, Visibility.Visible);
                //UIHelper.SetVisibility(m_sipMonitorDisplay, Visibility.Visible);

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
                    m_loginControl.WriteLoginMessage("Error logging out. " + excpMessage);
                }
            }
            finally
            {
                if (m_sipEventMonitorClient != null)
                {
                    m_sipEventMonitorClient.Close();
                }

                ThreadPool.QueueUserWorkItem(new WaitCallback(Initialise), null);
            }
        }
    }
}