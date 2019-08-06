using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Browser;
using System.Reflection;
using System.ServiceModel;
using System.ServiceModel.DomainServices;
using System.ServiceModel.DomainServices.Client;
using System.ServiceModel.DomainServices.Client.ApplicationServices;
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
using SIPSorcery.Entities;
using SIPSorcery.Entities.Services;
using SIPSorcery.Persistence;
using SIPSorcery.Silverlight.Messaging;
using SIPSorcery.Sockets;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public partial class Page : UserControl
    {
        private const int REINITIALISE_WAIT_PERIOD = 10000;
        private const int DEFAULT_WEB_PORT = 80;
        private const string DEFAULT_NOTIFICATIONS_FILE = "notificationspull.svc";
        private const string ENTITIES_SERVICE_URL = "clientbin/SIPSorcery-Entities-Services-SIPEntitiesDomainService.svc";
        private const string DEFAULT_SERVICE_HOST = "https://www.sipsorcery.com/ria/";
        private const string DEFAULT_NOTIFICATIONS_HOST = "https://www.sipsorcery.com/";

        public static List<string> TimeZones;

        private string m_notificationsServiceURL = null;
        private string m_entitiesServiceURL = null;

        private SIPEntitiesDomainContext m_riaContext;
        private ServiceConnectionStatesEnum m_persistorStatus = ServiceConnectionStatesEnum.Initialising;
        private string m_persistorStatusMessage = "Initialising...";
        private bool m_provisioningInitialisationInProgress;
        private string m_owner;
        //private string m_serviceURL;
        //private string m_notificationsURL;

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
            m_createAccountControl.CloseClicked += CancelCreateCustomer;
            m_createAccountControl.CustomerCreated += CustomerCreated;

            //m_serviceURL = App.ServiceURL;
            //m_notificationsURL = App.NotificationsURL;

            if (!String.IsNullOrEmpty(App.ServiceURL))
            {
                m_entitiesServiceURL = App.ServiceURL + ENTITIES_SERVICE_URL;
            }
            else
            {
                m_entitiesServiceURL = DEFAULT_SERVICE_HOST + ENTITIES_SERVICE_URL;
            }

            if (!String.IsNullOrEmpty(App.NotificationsURL))
            {
                m_notificationsServiceURL = App.NotificationsURL + DEFAULT_NOTIFICATIONS_FILE;
            }
            else
            {
                m_notificationsServiceURL = DEFAULT_NOTIFICATIONS_HOST + DEFAULT_NOTIFICATIONS_FILE;
            }
            
            //if (!m_serviceURL.IsNullOrBlank())
            //{
            //    m_entitiesServiceURL = m_serviceURL + ENTITIES_SERVICE_URL;
            //}

            //if (!m_notificationsURL.IsNullOrBlank())
            //{
            //    m_notificationsServiceURL = m_notificationsURL + DEFAULT_NOTIFICATIONS_FILE;
            //}

            // Use the Silverlight network stack so that SOAP faults can get through.
            HttpWebRequest.RegisterPrefix(m_notificationsServiceURL, WebRequestCreator.ClientHttp);

            if (m_riaContext == null)
            {
                CreateDomainContext();
            }

            m_createAccountControl.SetRIAContext(m_riaContext);

            ThreadPool.QueueUserWorkItem(delegate { Initialise(); });
                
            //UIHelper.SetPluginDimensions(LayoutRoot.RenderSize.Width, LayoutRoot.RenderSize.Height);
        }

        private void CreateDomainContext()
        {
            if (!m_entitiesServiceURL.IsNullOrBlank())
            {
                m_riaContext = new SIPEntitiesDomainContext(new Uri(m_entitiesServiceURL));
            }
            else
            {
                m_riaContext = new SIPEntitiesDomainContext();
            }
        }

        private void Initialise()
        {
            m_loginControl.SetProxy(m_riaContext);
            InitialiseServices(0);
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
                    m_riaContext.IsAlive(IsAliveComplete, null);
                }
                catch (Exception provExcp)
                {
                    m_persistorStatusMessage = provExcp.Message;
                    m_persistorStatus = ServiceConnectionStatesEnum.Error;
                    UpdateAppStatus();
                }
            }
        }

        private void IsAliveComplete(InvokeOperation<bool> op)
        {
            if (op.HasError)
            {
                m_persistorStatusMessage = op.Error.Message;
                m_persistorStatus = ServiceConnectionStatesEnum.Error;
                op.MarkErrorAsHandled();
            }
            else
            {
                m_persistorStatusMessage = null;
                m_persistorStatus = ServiceConnectionStatesEnum.Ok;
                
            }

            m_provisioningInitialisationInProgress = false;
            UpdateAppStatus();
        }

        /// <summary>
        /// If a class needs the timezones and the list is not already populated they will call the RIA method to populate the list.
        /// </summary>
        public static void GetTimeZonesCompleted(InvokeOperation<IEnumerable<string>> op)
        {
            if (op.HasError)
            {
                op.MarkErrorAsHandled();
            }
            else
            {
                TimeZones = op.Value.ToList();

                if (op.UserState != null && op.UserState is Action)
                {
                    ((Action)op.UserState)();
                }
            }
        }

        private void UpdateAppStatus()
        {
            if (m_persistorStatus == ServiceConnectionStatesEnum.Error)
            {
                UIHelper.SetFill(m_appStatusIcon, Colors.Red);
                UIHelper.SetText(m_provisioningStatusMessage, m_persistorStatusMessage + " " + m_entitiesServiceURL);

                if (m_userPage != null)
                {
                    m_userPage.SetProvisioningStatusIconColour(Colors.Red);
                    m_userPage.SetProvisioningStatusMessage(m_persistorStatusMessage + " " + m_entitiesServiceURL);
                }
            }
            else if (m_persistorStatus == ServiceConnectionStatesEnum.Ok)
            {
                UIHelper.SetFill(m_appStatusIcon, Colors.Green);
                UIHelper.SetText(m_provisioningStatusMessage, "Provisioning service ok. " + m_entitiesServiceURL);

                if (m_userPage != null)
                {
                    m_userPage.SetProvisioningStatusIconColour(Colors.Green);
                    m_userPage.SetProvisioningStatusMessage("Provisioning service ok. " + m_entitiesServiceURL);
                }

                if (m_loginControl.Visibility == System.Windows.Visibility.Visible)
                {
                    m_loginControl.FocusOnUsername();
                }
            }
        }

        private void Authenticated(string username, string authID)
        {
            UIHelper.SetText(m_provisioningStatusMessage, "Initialising...");
            UIHelper.SetFill(m_appStatusIcon, Colors.Blue);

            m_owner = username;

            m_loginControl.Clear();
            m_createAccountControl.Reset();
            UIHelper.SetVisibility(m_createAccountControl, Visibility.Collapsed);

            m_userPage = new UserPage(LogoutAsync, m_owner, m_notificationsServiceURL, m_riaContext);
            m_mainPageBorder.Content = m_userPage;

            UpdateAppStatus();
        }

        private void LogoutAsync(bool sendServerLogout)
        {
            try
            {
                this.TabNavigation = KeyboardNavigationMode.Cycle;
                m_mainPageBorder.Content = m_topCanvas;

                if (sendServerLogout)
                {
                    m_riaContext.Load<User>(m_riaContext.LogoutQuery(), LoadBehavior.RefreshCurrent, LogoutComplete, null);
                }

                m_userPage = null;

                UpdateAppStatus();
            }
            catch (Exception excp)
            {
                m_userPage.LogActivityMessage(MessageLevelsEnum.Error, "Exception LogoutAsync. " + excp.Message);
            }
        }

        private void LogoutComplete(LoadOperation op)
        {
            if (op.HasError)
            {
                op.MarkErrorAsHandled();
            }

            CreateDomainContext();

            ThreadPool.QueueUserWorkItem(delegate { Initialise(); });
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
            m_loginControl.Clear();
            m_loginControl.Visibility = System.Windows.Visibility.Visible;
        }

        /// <summary>
        /// Handler for the evnet that's fired on the Create Customer control once a new customer record is created.
        /// </summary>
        private void CustomerCreated(string createdMessage)
        {
            m_createAccountControl.Visibility = System.Windows.Visibility.Collapsed;
            m_loginControl.Clear();
            m_loginControl.Visibility = System.Windows.Visibility.Visible;
            m_loginControl.WriteLoginMessage(createdMessage);
        }

        /// <summary>
        /// Click handler for Create New Account link on the Create Customer control.
        /// </summary>
        /// <param name="inviteCode">If an invite code is required to create an account then this parameter
        /// holds a validated invite code.</param>
        private void CreateNewAccountLinkClicked(string inviteCode)
        {
            m_loginControl.Clear();
            m_loginControl.Visibility = System.Windows.Visibility.Collapsed;
            m_createAccountControl.InviteCode = inviteCode;
            m_createAccountControl.Visibility = System.Windows.Visibility.Visible;
        }
    }
}