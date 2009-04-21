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
    public delegate void ContentEditorClosedDelegate(UIElement diagram, string content);

	public partial class DiagramContentEditor : UserControl
	{
        private UIElement m_diagram;
        private string m_originalContent;

        public event ContentEditorClosedDelegate Closed;

		public DiagramContentEditor()
		{
			// Required to initialize variables
			InitializeComponent();
		}

        public void Display(UIElement diagram, string title, string content)
        {
            m_diagram = diagram;
            m_originalContent = content;
            m_diagramTitle.Text = (title != null) ? title : "No title";
            m_diagramContent.Text = (content != null) ? content : String.Empty;
            this.Visibility = Visibility.Visible;
        }

        private void CancelButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
            if (Closed != null)
            {
                Closed(m_diagram, m_originalContent);
            }
        }

        private void UpdateButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
           this.Visibility = Visibility.Collapsed;
            if (Closed != null)
            {
                Closed(m_diagram, m_diagramContent.Text);
            }
        }
	}
}