using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using SIPSorcery.Sys;

namespace SIPSorcery
{
    public delegate void AppUnhandledExceptionDelegate(ApplicationUnhandledExceptionEventArgs e);

    public partial class App : Application
    {
        //private const string DISABLE_PROVIDER_REGISTRATIONS_KEY = "DisableProviderRegistrations";
        private const string SERVICE_URL_KEY = "ServiceURL";
        private const string NOTIFICATIONS_URL_KEY = "NotificationURL";
        //private const string SHOW_INVITE_PANEL_KEY = "ShowInvitePanel";
        private const string DEFAULT_SIP_DOMAIN_KEY = "DefaultSIPDomain";

        private const string DEFAULT_SIP_DOMAIN = "sipsorcery.com";

        public static event AppUnhandledExceptionDelegate AppUnhandledException;

        //public static bool DisableProviderRegistrations = false;
        //public static bool ShowInvitePanel = false;
        public static string ServiceURL;
        public static string NotificationsURL;
        public static string DefaultSIPDomain;

        public App()
        {
            this.Startup += this.OnStartup;
            this.Exit += this.OnExit;
            this.UnhandledException += new EventHandler<ApplicationUnhandledExceptionEventArgs>(OnAppUnhandledException);

            InitializeComponent();
        }

        private void OnAppUnhandledException(object sender, ApplicationUnhandledExceptionEventArgs e)
        {
            if (AppUnhandledException != null)
            {
                AppUnhandledException(e);
            }
        }

        private void OnStartup(object sender, StartupEventArgs e)
        {
            // Load the main control here
            this.RootVisual = new Page();

            if (e.InitParams != null)
            {
                foreach (var data in e.InitParams)
                {
                    this.Resources.Add(data.Key, data.Value);
                }
            
                //Boolean.TryParse(App.Current.Resources[DISABLE_PROVIDER_REGISTRATIONS_KEY] as string, out DisableProviderRegistrations);
                //Boolean.TryParse(App.Current.Resources[SHOW_INVITE_PANEL_KEY] as string, out ShowInvitePanel);
                ServiceURL = App.Current.Resources[SERVICE_URL_KEY] as string;
                NotificationsURL = App.Current.Resources[NOTIFICATIONS_URL_KEY] as string;
                DefaultSIPDomain = App.Current.Resources[DEFAULT_SIP_DOMAIN_KEY] as string ?? DEFAULT_SIP_DOMAIN;
            }
        }

        private void OnExit(object sender, EventArgs e)
        {

        }
    }
}