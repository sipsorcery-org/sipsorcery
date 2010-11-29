using System.Windows;
using System;

namespace SIPSorcery
{
    public delegate void AppUnhandledExceptionDelegate(ApplicationUnhandledExceptionEventArgs e); 

	public partial class App : Application 
	{
        private const string DISABLE_PROVIDER_REGISTRATIONS_KEY = "DisableProviderRegistrations";

        public static event AppUnhandledExceptionDelegate AppUnhandledException;

        public static bool DisableProviderRegistrations = false;

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

                if (App.Current.Resources[DISABLE_PROVIDER_REGISTRATIONS_KEY] != null)
                {
                    Boolean.TryParse(App.Current.Resources[DISABLE_PROVIDER_REGISTRATIONS_KEY].ToString(), out DisableProviderRegistrations);
                }
            }
		}

		private void OnExit(object sender, EventArgs e) 
		{

		}
	}
}