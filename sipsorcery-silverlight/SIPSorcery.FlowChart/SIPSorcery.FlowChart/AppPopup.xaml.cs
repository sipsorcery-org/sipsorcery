using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SIPSorcery.Client.FlowChartDemo
{
    public delegate void PopupClosedDelegate();

    public partial class AppPopup : UserControl
	{      
        public event PopupClosedDelegate Closed;


		public AppPopup()
		{
			// Required to initialize variables
			InitializeComponent();
		}

        private void CloseErrorPopup(object sender, RoutedEventArgs e)
        {
            if (Closed != null)
            {
                Closed();
            }
        }
	}
}