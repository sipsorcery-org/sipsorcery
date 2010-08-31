using System.Windows;
using System;

namespace SIPSorcery
{
    public delegate void AppUnhandledExceptionDelegate(ApplicationUnhandledExceptionEventArgs e); 

	public partial class App : Application 
	{
        public static event AppUnhandledExceptionDelegate AppUnhandledException;

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
		}

		private void OnExit(object sender, EventArgs e) 
		{

		}
	}
}