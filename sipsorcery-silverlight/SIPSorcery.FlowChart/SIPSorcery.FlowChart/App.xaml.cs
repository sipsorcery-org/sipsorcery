using System.Windows;
using System;

namespace SIPSorcery.Client.FlowChartDemo
{
	public partial class App : Application 
	{

		public App() 
		{
			this.Startup += this.OnStartup;
			this.Exit += this.OnExit;

			InitializeComponent();
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